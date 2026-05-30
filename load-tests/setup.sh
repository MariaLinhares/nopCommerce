#!/usr/bin/env bash
# load-tests/setup.sh
#
# Prepares nopCommerce for the k6 load test in one shot:
#   1. Disables the per-customer order placement interval (DB)
#   2. Verifies Check/Money Order payment method is active (DB)
#   3. Discovers a valid in-stock product ID (DB)
#   4. Discovers the shipping option string (DB)
#   5. Registers 20 test accounts via the nopCommerce registration form
#
# Run from the repo root:
#   ./load-tests/setup.sh
#
# Requirements:
#   - docker compose up -d (all services healthy)
#   - curl

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:80}"
DB_CONTAINER="nopcommerce_mssql_server"
SA_PASSWORD="nopCommerce_db_password"
PASSWORD="LoadTest1!"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
ok()   { echo -e "${GREEN}[OK]${NC}  $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }

# ---------------------------------------------------------------------------
# Helper: run sqlcmd inside the SQL Server container
# ---------------------------------------------------------------------------
sqlcmd() {
  docker exec "$DB_CONTAINER" \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -No "$@"
}

# ---------------------------------------------------------------------------
# Helper: run a query and return the single trimmed result value
# ---------------------------------------------------------------------------
query_scalar() {
  sqlcmd -Q "SET NOCOUNT ON; $1" -h -1 2>/dev/null | tr -d ' \r\n'
}

# ---------------------------------------------------------------------------
# 0. Sanity checks
# ---------------------------------------------------------------------------
echo ""
echo "==> Checking Docker services..."

docker ps --filter "name=$DB_CONTAINER" --filter "status=running" --format "{{.Names}}" \
  | grep -q "$DB_CONTAINER" || fail "SQL Server container '$DB_CONTAINER' is not running. Run: docker compose up -d"

curl -s --max-time 5 "$BASE_URL" > /dev/null \
  || fail "nopCommerce is not reachable at $BASE_URL. Run: docker compose up -d"

ok "Services are up"

# ---------------------------------------------------------------------------
# 1. Disable minimum order placement interval
# ---------------------------------------------------------------------------
echo ""
echo "==> Disabling minimum order placement interval..."

sqlcmd -Q "
  IF EXISTS (SELECT 1 FROM Setting WHERE Name = 'ordersettings.minimumorderplacementinterval')
    UPDATE Setting SET Value = '0' WHERE Name = 'ordersettings.minimumorderplacementinterval';
  ELSE
    INSERT INTO Setting (Name, Value, StoreId) VALUES ('ordersettings.minimumorderplacementinterval', '0', 0);
" 2>/dev/null

ok "MinimumOrderPlacementInterval = 0"

# ---------------------------------------------------------------------------
# 2. Ensure Check/Money Order payment method is active
# ---------------------------------------------------------------------------
echo ""
echo "==> Checking payment methods..."

CMO_ACTIVE=$(query_scalar "
  SELECT TOP 1 CAST(IsActive AS VARCHAR)
  FROM PaymentMethod
  WHERE SystemName = 'Payments.CheckMoneyOrder'
" 2>/dev/null || echo "")

if [ "$CMO_ACTIVE" = "1" ]; then
  ok "Check/Money Order is already active"
elif [ "$CMO_ACTIVE" = "0" ]; then
  sqlcmd -Q "UPDATE PaymentMethod SET IsActive = 1 WHERE SystemName = 'Payments.CheckMoneyOrder'" 2>/dev/null
  ok "Check/Money Order activated"
else
  warn "Could not verify Check/Money Order — table may differ. Enable it manually in Admin → Payment Methods."
fi

# ---------------------------------------------------------------------------
# 3. Discover in-stock product ID
# ---------------------------------------------------------------------------
echo ""
echo "==> Finding an in-stock published product..."

PRODUCT_ID=$(query_scalar "
  SELECT TOP 1 CAST(Id AS VARCHAR)
  FROM Product
  WHERE Published = 1 AND Deleted = 0 AND (StockQuantity > 50 OR ManageInventoryMethodId = 0)
  ORDER BY DisplayOrder, Id
")

[ -n "$PRODUCT_ID" ] || fail "No published in-stock product found. Add at least one product in nopCommerce admin."
ok "PRODUCT_ID = $PRODUCT_ID"

# ---------------------------------------------------------------------------
# 4. Discover shipping option string
# ---------------------------------------------------------------------------
echo ""
echo "==> Discovering shipping option string..."

# ShippingMethod names come from the ShippingMethod table; the plugin system
# name is stored in the plugin that loaded those methods. For FixedByWeightByTotal
# the system name is Shipping.FixedByWeightByTotal. For FixedOrByCountryStateZip
# it is Shipping.FixedOrByCountryStateZip.
SHIPPING_METHOD_NAME=$(query_scalar "
  SELECT TOP 1 CAST(Name AS VARCHAR(200))
  FROM ShippingMethod
  WHERE Deleted = 0
  ORDER BY DisplayOrder, Id
" 2>/dev/null || echo "")

# Determine which shipping plugin is active
SHIPPING_PLUGIN=$(query_scalar "
  SELECT TOP 1 CAST(SystemName AS VARCHAR(200))
  FROM PluginDescriptor
  WHERE IsEnabled = 1 AND SystemName LIKE 'Shipping.%'
  ORDER BY DisplayOrder, Id
" 2>/dev/null || echo "Shipping.FixedByWeightByTotal")

if [ -n "$SHIPPING_METHOD_NAME" ] && [ -n "$SHIPPING_PLUGIN" ]; then
  SHIPPING_OPTION="${SHIPPING_METHOD_NAME}___${SHIPPING_PLUGIN}"
  ok "SHIPPING_OPTION = $SHIPPING_OPTION"
else
  SHIPPING_OPTION="Ground___Shipping.FixedByWeightByTotal"
  warn "Could not auto-detect shipping option — using default: $SHIPPING_OPTION"
  warn "If the test fails at checkout_shipping_method, check the value by inspecting"
  warn "the checkout page in a browser and updating SHIPPING_OPTION in the k6 run command."
fi

# ---------------------------------------------------------------------------
# 5. Register 20 test accounts
# ---------------------------------------------------------------------------
echo ""
echo "==> Registering 20 test accounts..."

COOKIE_JAR=$(mktemp)
CREATED=0
SKIPPED=0

for i in $(seq 1 20); do
  NUM=$(printf "%02d" "$i")
  EMAIL="loadtest${NUM}@verdemart.com"

  # Check if account already exists
  EXISTS=$(query_scalar "
    SELECT TOP 1 CAST(Id AS VARCHAR) FROM Customer WHERE Email = '${EMAIL}' AND Deleted = 0
  " 2>/dev/null || echo "")

  if [ -n "$EXISTS" ]; then
    SKIPPED=$((SKIPPED + 1))
    continue
  fi

  # GET registration page → extract CSRF token
  REG_HTML=$(curl -s -c "$COOKIE_JAR" "$BASE_URL/register")
  TOKEN=$(echo "$REG_HTML" | grep -oP 'name="__RequestVerificationToken"[^>]*value="\K[^"]+' 2>/dev/null \
       || echo "$REG_HTML" | grep -oP 'value="\K[^"]+(?="[^>]*name="__RequestVerificationToken")' 2>/dev/null \
       || echo "")

  if [ -z "$TOKEN" ]; then
    warn "Could not extract CSRF token for $EMAIL — skipping"
    continue
  fi

  # POST registration
  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" \
    -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
    -X POST "$BASE_URL/register" \
    -L \
    --data-urlencode "FirstName=Load" \
    --data-urlencode "LastName=Test${NUM}" \
    --data-urlencode "Email=$EMAIL" \
    --data-urlencode "Password=$PASSWORD" \
    --data-urlencode "ConfirmPassword=$PASSWORD" \
    --data-urlencode "__RequestVerificationToken=$TOKEN" \
    --data-urlencode "Gender=M")

  if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "302" ]; then
    CREATED=$((CREATED + 1))
    echo "    Created  $EMAIL"
  else
    warn "Registration returned HTTP $HTTP_CODE for $EMAIL"
  fi

  # Reset cookie jar between users so sessions don't bleed
  > "$COOKIE_JAR"
done

rm -f "$COOKIE_JAR"

ok "Accounts: $CREATED created, $SKIPPED already existed"

# ---------------------------------------------------------------------------
# Done — print the k6 run command
# ---------------------------------------------------------------------------
echo ""
echo "========================================================"
echo " Setup complete. Run the load test with:"
echo ""
echo "   k6 run \\"
echo "     --env BASE_URL=$BASE_URL \\"
echo "     --env PRODUCT_ID=$PRODUCT_ID \\"
echo "     --env SHIPPING_OPTION=\"$SHIPPING_OPTION\" \\"
echo "     load-tests/order-placement.js"
echo ""
echo " For the degradation demo (Scenarios 2 + 3):"
echo "   Start the test above, then in another terminal:"
echo "   docker compose stop inventory-sync    # watch queue depth grow"
echo "   docker compose start inventory-sync   # watch queue drain"
echo "========================================================"
echo ""
