# Bounded Contexts

**Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)
**Status:** DRAFT v2 · **Due:** 29 Apr 2026
**Grounded in:** [docs/shared/nopcommerce-context-pack.md](README.md)
**See also:** [context-map.md](context-map.md) · [quality-attribute-scenarios.md](quality-attribute-scenarios.md) · [README.md](README.md)

---

## 1. Domain Overview

VerdeMart's commerce core (nopCommerce) must coordinate with operational systems it does not own: a warehouse management system (OpenBoxes), a shipping carrier (WireMock-simulated), and asynchronous propagation infrastructure (RabbitMQ). The domain therefore splits along **operational responsibility lines**, not along nopCommerce's current technical layers.

We model **five bounded contexts**. Three live inside the nopCommerce monolith (with clarified internal boundaries); two are extracted to make ownership and failure modes explicit.

The investigation of the cloned nopCommerce codebase (commit `ed4c133`) confirms that the contexts below align with seams that already exist in the code — the model is **not invented**, it makes existing concepts explicit. Specific code citations appear inline below and are consolidated in the [context pack](README.md).

---

## 2. Bounded Contexts

| # | Context | Location | Owns | Does NOT own |
|---|---------|----------|------|--------------|
| 1 | **Catalog** | Inside nopCommerce | Product definitions, SKUs, pricing, descriptions, categories | Stock levels, reservations |
| 2 | **Order Management** | Inside nopCommerce | Order lifecycle, customer intent, payment authorization, order state machine | Physical stock truth, fulfillment execution |
| 3 | **Identity** | Inside nopCommerce (cross-cutting) | Customer accounts, authentication, session | Authorization policy for staff/back-office (out of scope cut) |
| 4 | **Inventory / Warehouse** | Extracted (sync service + OpenBoxes) | Authoritative stock truth, reservations, stock movements, replenishment | Pricing, customer-facing availability presentation |
| 5 | **Shipping / Fulfillment** | Extracted (sync service + WireMock carrier) | Carrier dispatch, tracking, delivery state | Order intent, payment |

### 2.1 Responsibility detail per context

**Catalog** — read-heavy. Owns [`Product`](../../nopCommerce/src/Libraries/Nop.Core/Domain/Catalog/Product.cs), categories, and product attributes. Publishes product changes as events. Customer-facing stock display is a *projection* enriched from Inventory's published stream, not a direct query. *Today, the storefront synchronously computes available stock as `StockQuantity − ReservedQuantity` inside [`ProductService.GetTotalStockQuantityAsync` (line 1454)](../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L1454) — our projection makes that computation eventually-consistent and external-source-aware.*

**Order Management** — owns the order aggregate ([`Order.cs`](../../nopCommerce/src/Libraries/Nop.Core/Domain/Orders/Order.cs)) and its state machine: `Placed → Reserved → Confirmed → Fulfilled → Shipped → Delivered` (with `Cancelled` / `Compensated` branches). It accepts orders **optimistically** and reconciles via async confirmation from Inventory. This is the central architectural bet — it is what makes the system useful when the warehouse is degraded. *Today the state machine is implicit, scattered across [`OrderProcessingService.CheckAndSaveOrderStatusAsync` (line 1480)](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1480) with no `IOrderWorkflow` abstraction; making it explicit is part of our evolution.*

**Identity** — keeps current nopCommerce ASP.NET Core Identity. Treated as cross-cutting to avoid scope inflation. Out of scope: federated SSO across channels (honest scope cut, called out in the roadmap).

**Inventory / Warehouse** — independently deployable sync service in front of OpenBoxes. Owns its own database (no shared DB across boundaries — assignment hard constraint). Exposes a small published language: `StockReserved`, `StockReleased`, `StockLevelChanged`, `ReservationRejected`. Consumes `OrderPlaced`. *The reservation concept already exists in nopCommerce as [`ProductWarehouseInventory.ReservedQuantity` (line 26)](../../nopCommerce/src/Libraries/Nop.Core/Domain/Catalog/ProductWarehouseInventory.cs#L26); we extract the operational responsibility to its own service rather than inventing a new model.*

**Shipping / Fulfillment** — extracted integration that talks to the WireMock-simulated carrier. Consumes `OrderConfirmed`, publishes `ShipmentDispatched` and `ShipmentDelivered`. WireMock lets us inject latency, 5xx, and contradictory states without a real carrier dependency. *Today shipment is created manually by an admin in [`OrderProcessingService.ShipAsync` (line 2204)](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L2204) and carrier rate calls happen synchronously in-request (see [`UPSService.GetRatesAsync`](../../nopCommerce/src/Plugins/Nop.Plugin.Shipping.UPS/Services/UPSService.cs)) — a slow carrier slows checkout. Our extracted Shipping context isolates that pressure.*
