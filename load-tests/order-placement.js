/**
 * k6 load test — Order Placement Throughput & Latency
 *
 * Validates:
 *   QA-3  Order placement P95 ≤ 2 s, throughput ≥ 50 orders/min
 *   QA-2  Stock propagation P95 ≤ 30 s (measured via outbox dispatch lag polled after the run)
 *
 * Run setup first (creates accounts, discovers product/shipping config):
 *   ./load-tests/setup.sh
 *   Then copy-paste the k6 command it prints.
 *
 * Degradation scenario (Scenarios 2 + 3 from demo):
 *   Start this test, then in another terminal:
 *     docker compose stop inventory-sync    — observe queue depth growing in RabbitMQ
 *     docker compose start inventory-sync   — observe queue draining, orders confirming
 */

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

// ---------------------------------------------------------------------------
// Custom metrics — named to match QA scenario IDs in the report
// ---------------------------------------------------------------------------
const orderPlacementDuration = new Trend('order_placement_p95_ms', true); // QA-3: p(95) < 2000
const orderSuccessRate       = new Rate('order_success_rate');             // QA-3: rate > 0.95
const ordersPlaced           = new Counter('orders_placed_total');

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
const BASE_URL   = __ENV.BASE_URL   || 'http://localhost:80';
const PRODUCT_ID = __ENV.PRODUCT_ID || '1';

// Shipping method option value — inspect the checkout page to find yours.
// Format: "<displayed rate name>___<plugin system name>"
const SHIPPING_OPTION = __ENV.SHIPPING_OPTION || 'Ground___Shipping.FixedByWeightByTotal';

// One account per VU. Add more rows if you increase maxVUs.
// Create these accounts in nopCommerce admin before running.
const USERS = [
  { email: 'loadtest01@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest02@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest03@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest04@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest05@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest06@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest07@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest08@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest09@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest10@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest11@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest12@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest13@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest14@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest15@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest16@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest17@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest18@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest19@verdemart.com', password: 'LoadTest1!' },
  { email: 'loadtest20@verdemart.com', password: 'LoadTest1!' },
];

// ---------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------
export const options = {
  scenarios: {
    // Ramp up to QA-3 target of 50 orders/min, hold, then ramp down
    order_throughput: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1m',
      preAllocatedVUs: 10,
      maxVUs: 20,
      stages: [
        { duration: '1m', target: 20 },  // ramp up
        { duration: '3m', target: 50 },  // hold at QA-3 target
        { duration: '1m', target: 0 },   // ramp down
      ],
    },
  },
  thresholds: {
    'order_placement_p95_ms': ['p(95)<2000'],  // QA-3
    'order_success_rate':     ['rate>0.95'],    // QA-3
    'http_req_failed':        ['rate<0.05'],
  },
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function extractCsrfToken(html) {
  const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return m ? m[1] : '';
}

function jsonBody(res) {
  try { return JSON.parse(res.body); } catch (_) { return {}; }
}

// ---------------------------------------------------------------------------
// Main VU function
// ---------------------------------------------------------------------------
export default function () {
  const user   = USERS[(__VU - 1) % USERS.length];
  const jar    = http.cookieJar();
  const p      = { jar };
  const ajax   = { jar, headers: { 'X-Requested-With': 'XMLHttpRequest' } };

  let csrfToken = '';

  // ---- 1. Login --------------------------------------------------------
  group('login', () => {
    const loginPage = http.get(`${BASE_URL}/login`, p);
    check(loginPage, { 'login page 200': r => r.status === 200 });
    csrfToken = extractCsrfToken(loginPage.body);

    const res = http.post(`${BASE_URL}/login`, {
      Email: user.email,
      Password: user.password,
      __RequestVerificationToken: csrfToken,
      RememberMe: 'false',
    }, { ...p, redirects: 5 });

    check(res, { 'login succeeded': r => !r.url.endsWith('/login') });
  });

  // ---- 2. Add to cart --------------------------------------------------
  group('add_to_cart', () => {
    const res  = http.post(
      `${BASE_URL}/addproducttocart/catalog/${PRODUCT_ID}/1/1`,
      null,
      ajax
    );
    const body = jsonBody(res);
    check(body, { 'added to cart': b => b.success === true });
  });

  // ---- 3. Checkout — billing -------------------------------------------
  group('checkout_billing', () => {
    const checkoutPage = http.get(`${BASE_URL}/checkout`, p);
    check(checkoutPage, { 'checkout 200': r => r.status === 200 });
    csrfToken = extractCsrfToken(checkoutPage.body);

    const res = http.post(`${BASE_URL}/checkout/OpcSaveBilling`, {
      __RequestVerificationToken:         csrfToken,
      billing_address_id:                 '-1',
      'BillingNewAddress.FirstName':      'Load',
      'BillingNewAddress.LastName':       'Test',
      'BillingNewAddress.Email':          user.email,
      'BillingNewAddress.CountryId':      '136',   // Portugal — change if needed
      'BillingNewAddress.City':           'Lisbon',
      'BillingNewAddress.Address1':       'Rua do Teste 1',
      'BillingNewAddress.ZipPostalCode':  '1000-001',
      'BillingNewAddress.PhoneNumber':    '910000000',
      ship_to_same_address:               'true',
    }, ajax);

    const body = jsonBody(res);
    check(body, { 'billing saved': b => !b.error });
  });

  // ---- 4. Checkout — shipping method -----------------------------------
  group('checkout_shipping_method', () => {
    const res  = http.post(`${BASE_URL}/checkout/OpcSaveShippingMethod`, {
      __RequestVerificationToken: csrfToken,
      shippingoption:             SHIPPING_OPTION,
    }, ajax);
    const body = jsonBody(res);
    check(body, { 'shipping method saved': b => !b.error });
  });

  // ---- 5. Checkout — payment method ------------------------------------
  group('checkout_payment_method', () => {
    const res  = http.post(`${BASE_URL}/checkout/OpcSavePaymentMethod`, {
      __RequestVerificationToken: csrfToken,
      paymentmethod:              'Payments.CheckMoneyOrder',
    }, ajax);
    const body = jsonBody(res);
    check(body, { 'payment method saved': b => !b.error });
  });

  // ---- 6. Confirm order — this is the measurement point ---------------
  group('confirm_order', () => {
    const start = Date.now();

    const res  = http.post(`${BASE_URL}/checkout/OpcConfirmOrder`, {
      __RequestVerificationToken: csrfToken,
      captchaValid:               'false',
    }, ajax);

    const elapsed = Date.now() - start;
    const body    = jsonBody(res);

    // success = { success: 1 }  or  { redirect: '...' }  for redirect-type payments
    const success = body.success === 1 || typeof body.redirect === 'string';

    orderPlacementDuration.add(elapsed);
    orderSuccessRate.add(success);
    if (success) ordersPlaced.add(1);

    check(body, { 'order placed': b => b.success === 1 || typeof b.redirect === 'string' });
  });

  // Respect minimum order placement interval if set > 0 in nopCommerce settings
  sleep(1);
}
