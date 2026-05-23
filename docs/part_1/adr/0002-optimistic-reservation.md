# ADR-2 — Stock truth via Optimistic Reservation with Compensation

**Status:** Accepted · **Date:** 2026-05-03 · **Author:** Person 4
**Related:** [QA-1 (availability)](../../person2/quality-attribute-scenarios.md#qa-1--availability-mandatory-pressure-point), [QA-4 (recoverability)](../../person2/quality-attribute-scenarios.md#qa-4--recoverability-compensation-on-rejection), [Iteration 1 / 3](../../person3/add-framework.md), [ADR-1](0001-transactional-outbox.md)

---

## Context

Today, [`OrderProcessingService.MoveShoppingCartItemsToOrderItemsAsync` line 1337](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1337) calls `_productService.AdjustInventoryAsync(product, -sc.Quantity, ...)` synchronously, in the same call chain as the order insert. If the warehouse is unreachable, slow, or simulates an out-of-stock failure, **order acceptance fails**. This is exactly the pressure point [QA-1](../../person2/quality-attribute-scenarios.md#qa-1--availability-mandatory-pressure-point) names.

The codebase already contains the primitive we need to do better. [`ProductWarehouseInventory.cs`](../../../nopCommerce/src/Libraries/Nop.Core/Domain/Catalog/ProductWarehouseInventory.cs) carries both `StockQuantity` and `ReservedQuantity` (line 26). [`ProductService.ReserveInventoryAsync` line 392](../../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L392) increments `ReservedQuantity` without touching `StockQuantity`; [`BookReservedInventoryAsync` line ~1828](../../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L1828) — called from `ShipAsync` — moves reserved → booked. The storefront already shows `available = stock − reserved` ([`GetTotalStockQuantityAsync` line 1456-1458](../../../nopCommerce/src/Libraries/Nop.Services/Catalog/ProductService.cs#L1456)). The reservation pattern exists; the order placement path simply does not use it as a *seam*.

The lecturer's vocabulary (slides 02.02 p.43): *"Saga — a sequence of local transactions plus compensating actions to undo earlier work when a later step fails."* That is exactly the shape required when order acceptance is decoupled from the warehouse: optimistic local commit → async confirm → compensate on failure.

## Decision

Adopt **optimistic reservation with compensation** as the standard order-acceptance flow:

1. `PlaceOrderAsync` accepts the order optimistically into `Placed` state and writes an `OrderPlaced` outbox row (per [ADR-1](0001-transactional-outbox.md)). The synchronous `AdjustInventoryAsync` call at [`OrderProcessingService.cs:1337`](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1337) is removed from this path.
2. The **Inventory Sync Service** consumes `OrderPlaced`, attempts `ReserveInventoryAsync`-equivalent logic against its own DB and OpenBoxes via ACL, and publishes either `StockReserved` (success) or `ReservationRejected` (insufficient stock, SKU discontinued, persistent OpenBoxes rejection after retries).
3. Order Management consumes the response:
   - On `StockReserved` → transition to `Confirmed`, publish `OrderConfirmed` for the Shipping service.
   - On `ReservationRejected` → transition to a new explicit `Compensated` state, release payment authorization, send customer notification.
4. The compensation path is **idempotent on the rejection's `EventId`** — a duplicate redelivery has no additional effect (no double payment release, no double email). This is required because [ADR-1](0001-transactional-outbox.md) gives at-least-once delivery.
5. The customer-facing UI surfaces the interim `Placed` state as *"order received, confirming with warehouse"* (existing copy patterns in nopCommerce already accommodate this — `OrderStatus` enum just needs new transitions).

The standardisation is deliberate. nopCommerce currently has **two** stock paths: single-warehouse (`Product.StockQuantity` mutated immediately) and multi-warehouse (reservation). The new flow uses the reservation shape for **both** — the single-warehouse case is modeled as a one-warehouse special case of the multi-warehouse logic, eliminating the divergence.

## Alternatives considered

### A. Pessimistic synchronous stock check at order placement (today's behaviour) — REJECTED

The current code: `AdjustInventoryAsync` at [`OrderProcessingService.cs:1337`](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1337) decrements stock during order placement. Pros: stock truth is enforced before the customer sees a confirmation; no compensation logic needed. Cons: re-couples acceptance to warehouse availability — the exact QA-1 failure mode. The team's investigation confirmed there is no way to soften this without breaking the synchronous contract: every alternative we explored (cached stock, retries, circuit breakers around `AdjustInventoryAsync`) leaks broken state into the order on failure. Rejected because it directly contradicts the architectural bet.

### B. Distributed lock per SKU (Redis / ZooKeeper) — REJECTED

Acquire a per-SKU lock at the start of order placement to serialise concurrent reservations and prevent overbooking. Pros: pessimistic guarantees on stock truth across instances. Cons: introduces a new coordination dependency (Redis cluster or ZooKeeper) and a new failure mode (lock-broker outage) for what is fundamentally an availability problem; the demo runs single-instance ([Risk R4](../risk-plan.md)) so the lock buys nothing. The lecturer's framing applies (DDD slides p.41): *"aggregate boundaries are scaling decisions, not only modeling decisions"* — adding a distributed lock here would be a *scaling* decision applied to a problem the demo's scale does not have. Rejected as scope inflation.

### C. Eventually-consistent acceptance without compensation ("oversell, sort it out later") — REJECTED

Optimistically accept all orders, never reject. Pros: simplest flow, highest acceptance rate. Cons: business-policy unacceptable — VerdeMart cannot ship product they do not have, and the assignment's mandatory recovery use case ([QA-4](../../person2/quality-attribute-scenarios.md#qa-4--recoverability-compensation-on-rejection)) explicitly requires a visible compensation path. Rejected because it ducks the mandatory pressure-point recovery requirement.

### D. Pre-reservation at "add to cart" — DEFERRED

Move the reservation earlier in the funnel so by checkout the stock is already held. Pros: avoids the rejection-after-payment case in the common path. Cons: introduces cart-expiry mechanics (reservations leak if carts are abandoned), changes a customer-visible behaviour (the cart now affects stock visibility), and is a much larger scope change than the assignment justifies. **Deferred to future work**, not rejected on principle.

## Consequences

**Positive:**
- Order acceptance survives warehouse degradation. QA-1's pressure point is materially addressed.
- The compensation path is **explicit, named, and auditable**. The `Compensated` order state is a first-class lifecycle endpoint with a state-machine transition trigger (`ReservationRejected.EventId`) and a customer-facing notification — exactly what [QA-4](../../person2/quality-attribute-scenarios.md#qa-4--recoverability-compensation-on-rejection) requires.
- Reuses an existing in-codebase primitive (`ReservedQuantity`) — Person 4 can defend in Q&A: *we standardised, we did not invent.*

**Negative / new concerns:**
- Customers can now experience a state we do not have today: an order accepted, then rejected. The customer-facing copy and email templates need explicit work; this is operational debt the architecture report must call out as a UX cost of the reliability gain.
- The `Compensated` state must be added to the implicit FSM scattered across [`CheckAndSaveOrderStatusAsync` line 1484](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1484). Making the state machine explicit (or at least adding the new branch cleanly) is part of [Iteration 3](../../person3/add-framework.md#iteration-3--shipping-integration-and-compensation) and is non-trivial.
- Compensation idempotency is a hard rule — see [ADR-1 consequences](0001-transactional-outbox.md#consequences). A bug here results in double payment release. Validation: integration test that delivers the same `ReservationRejected` twice and asserts a single payment-void call.
- The **single-warehouse stock path** must be migrated to the reservation shape too. This is a non-trivial code change and a real risk that we are quietly absorbing into ADR-2 to keep the surface area honest. If migration proves disruptive, an honest fallback is to keep single-warehouse synchronous and only route multi-warehouse products through the new path — at the cost of two flows in production. Decision flagged for Part 2.
