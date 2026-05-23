# Quality Attribute Scenarios

**Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)
**Author:** Person 2 · **Status:** DRAFT v2 · **Due:** 29 Apr 2026
**Grounded in:** [docs/shared/nopcommerce-context-pack.md](../shared/nopcommerce-context-pack.md)
**See also:** [bounded-contexts.md](bounded-contexts.md) · [context-map.md](context-map.md) · [README.md](README.md)

---

Format: Source / Stimulus / Artifact / Environment / Response / Response Measure.

All numeric measures are **starting targets** to be validated by Person 4's feasibility spike and refined before final delivery.

---

## QA-1 — Availability (mandatory pressure point)

| Element | Description |
|---|---|
| **Source** | Warehouse subsystem (OpenBoxes or the Inventory service in front of it) |
| **Stimulus** | Becomes unreachable or returns 5xx / timeouts for sustained period |
| **Artifact** | Order Management — order placement path |
| **Environment** | Production-equivalent runtime, normal customer traffic |
| **Response** | Order Management accepts new orders **optimistically**, persists them in `Placed` state, enqueues an `OrderPlaced` event via the **outbox**. Customer receives a confirmation that does not promise dispatch. Catalog continues to serve last-known stock view with a staleness indicator. |
| **Response Measure** | ≥ **99% of order-placement requests succeed** during a warehouse outage of up to **15 minutes**. Zero data loss in the outbox. Reservation reconciliation completes within **5 minutes** of warehouse recovery. |

**Grounded in code:** today, the same architectural pressure manifests with the carrier — `Nop.Plugin.Shipping.UPS` calls UPS over synchronous HTTP from inside the request thread ([UPSService.cs:109-294](../../nopCommerce/src/Plugins/Nop.Plugin.Shipping.UPS/Services/UPSService.cs#L109)), with no retry or circuit breaker. A slow UPS = a slow checkout. Our scenario's pressure (warehouse degradation) has the **same shape**, and we explicitly choose a different architectural response: decouple via outbox + RabbitMQ instead of replicating the synchronous-in-request precedent.

---

## QA-2 — Consistency (stock visibility)

| Element | Description |
|---|---|
| **Source** | Warehouse operator or external stock movement (POS, manual count, replenishment) |
| **Stimulus** | Stock level for a SKU changes outside nopCommerce |
| **Artifact** | Catalog stock-view projection |
| **Environment** | Normal operation |
| **Response** | Inventory publishes `StockLevelChanged`; Catalog updates its read model |
| **Response Measure** | P95 propagation lag from warehouse change to Catalog visibility ≤ **30 seconds** under normal load. During RabbitMQ degradation, lag may grow but **must not exceed 5 minutes**, and the UI must surface a "stock data may be delayed" indicator when lag breaches 60 s. |

**Grounded in code:** nopCommerce already computes available stock by subtracting reservations on read ([`ProductService.GetTotalStockQuantityAsync` line 1454-1456](../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L1454)). The data shape we need (per-warehouse stock + reservations) exists; the change is *where* the truth lives (Inventory service, not the local DB) and *how* it propagates (events, not direct table reads). The 30 s P95 target is achievable because the existing [`IScheduleTask` runner](../../nopCommerce/src/Libraries/Nop.Services/ScheduleTasks/TaskScheduler.cs) — which our outbox dispatcher will piggyback on — already polls at sub-minute cadence by default.

---

## QA-3 — Performance (order placement throughput)

| Element | Description |
|---|---|
| **Source** | Customers placing orders via web storefront |
| **Stimulus** | Sustained order placement load |
| **Artifact** | Order Management endpoint + outbox write path |
| **Environment** | Normal operation, all subsystems healthy |
| **Response** | Order is persisted, outbox row written in same DB transaction, HTTP 201 returned to customer. Async dispatch of `OrderPlaced` happens out of band. |
| **Response Measure** | P95 end-to-end order placement latency ≤ **800 ms** at sustained **50 orders/minute**. P99 ≤ **1.5 s**. No order loss, no duplicate orders (idempotency key on the outbox dispatcher). |

**Grounded in code — and an open caveat:** the order placement path today is [`OrderProcessingService.PlaceOrderAsync` (line 1567)](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1567), which calls 30+ injected services and synchronously decrements stock at [line 1333](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1333). Our redesign **removes** the synchronous stock call from this path (replaced by an outbox write), which is a latency *improvement*, not a regression. **Caveat:** the investigation flagged that this method does not appear to run inside an explicit `TransactionScope`, so the outbox-in-same-transaction guarantee depends on Person 4's spike (Risk R2 in the context pack) confirming we can wrap the relevant section. If the spike shows otherwise, the response measure assumes "at-least-once delivery with idempotent consumers" instead of "exactly-once."

**Default-deployment caveat:** out of the box, nopCommerce uses an in-process `MemoryDistributedCacheManager` and **cannot horizontally scale** without configuring Redis ([ServiceCollectionExtensions.cs:197-235](../../nopCommerce/src/Presentation/Nop.Web.Framework/Infrastructure/Extensions/ServiceCollectionExtensions.cs#L197)). For the demo, single-instance is the honest target; multi-instance throughput claims would require Redis, which we should call out as future work, not part of this assignment.

---

## QA-4 — Recoverability (compensation on rejection)

| Element | Description |
|---|---|
| **Source** | Inventory service |
| **Stimulus** | After an order is accepted optimistically, Inventory cannot reserve stock (insufficient quantity, SKU discontinued, or persistent rejection after retries) |
| **Artifact** | Order Management state machine + customer notification path |
| **Environment** | Normal operation OR after warehouse recovery |
| **Response** | Order Management consumes `ReservationRejected`, transitions order to `Compensated`, releases payment authorization, sends customer notification. The compensation is itself idempotent — replay of the same rejection event has no additional effect. |
| **Response Measure** | 100% of rejected orders reach `Compensated` state within **2 minutes** of the rejection event, with payment release confirmed and a customer notification dispatched. Audit trail records every state transition with timestamp and trigger. |

**Grounded in code:** the existing reservation/booking lifecycle ([`ProductService.ReserveInventoryAsync` line 391](../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L391) and [`BookReservedInventoryAsync` line 1804](../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L1804)) gives us a natural compensation primitive: a rejection means the reservation is released without ever being booked. The order state machine today only handles the happy path implicitly through `CheckAndSaveOrderStatusAsync`; the new `Compensated` state and its transition need to be added explicitly (P3's target architecture). Idempotency is enforced via a deduplication key on the `ReservationRejected` event ID, stored alongside the order.

---

## QA-5 — Operability (optional 5th, suggested addition)

> Drop this if the four above already feel rich enough. Including it strengthens the "evidence pack" requirement (assignment §4.5).

| Element | Description |
|---|---|
| **Source** | On-call operator |
| **Stimulus** | Customer reports an order stuck in `Placed` |
| **Artifact** | Logging, tracing, and dashboards across Order Mgmt, RabbitMQ, Inventory |
| **Environment** | Any |
| **Response** | Operator can trace the order's journey: outbox row, RabbitMQ delivery, Inventory consumer log, retry attempts, current state. |
| **Response Measure** | Mean time to identify the failed step ≤ **5 minutes** using only the operator dashboard and correlation ID. |

**Grounded in code:** today, [`EventPublisher.PublishAsync` (lines 20-52)](../../nopCommerce/src/Libraries/Nop.Services/Events/EventPublisher.cs#L20) catches and logs consumer exceptions but does not propagate them. An operator investigating a stuck order has only `ILogger` output to work from — there is no correlation ID flowing through, no per-event dispatch table, no DLQ. Our outbox table and a RabbitMQ DLQ explicitly create the audit trail this scenario assumes.

---

## Traceability hooks for Person 3 and Person 4

These are deliberate hooks so the downstream work can cite this document directly:

- **QA-1** drives the **outbox pattern** decision (Person 4 ADR #1) and the **optimistic reservation** decision (Person 4 ADR #2).
- **QA-2** drives the choice of **event-driven stock projection** vs. synchronous query (Person 3 target architecture).
- **QA-3** drives the **transactional outbox** design (no dual-write) and informs DB sizing.
- **QA-4** drives the **compensation** branch in the order state machine and the **idempotency** requirement on consumers.
- **No-shared-DB** rule across contexts (see [context-map.md §3](context-map.md#3-data-ownership-rules)) is the architectural constraint that forces the extracted Inventory and Shipping services to own their data.
