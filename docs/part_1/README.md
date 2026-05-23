# Key Takeaways — One-Page Cheat Sheet

**Scenario:** C — Omnichannel Commerce Core · **Framework:** ADD
**Surrounding systems:** OpenBoxes (WMS), WireMock (carrier), RabbitMQ
**Reliability mechanism:** Transactional outbox + retry

See deliverables: [bounded-contexts](02-bounded-contexts.md), [context-map](03-context-map.md), [QA scenarios](04-quality-attribute-scenarios.md), [ADD framework](05-add-framework.md), [target architecture](06-target-architecture.md), [feasibility spike](07-feasibility-spike.md), [risk plan](08-risk-plan.md), [ADRs](adr/).

---

## The architectural bet, in one sentence

> **Replace the synchronous stock decrement inside `OrderProcessingService` with an outbox-published event, so order acceptance survives warehouse degradation and reconciles on recovery.**

That single change — and everything it pulls along — *is* the assignment.

---

## The 5 facts that drive every decision

1. **Reservation already exists** — [`ProductWarehouseInventory.ReservedQuantity`](../nopCommerce/src/Libraries/Nop.Core/Domain/Catalog/ProductWarehouseInventory.cs#L26). We standardize an existing concept, not invent one.
2. **The tangle is one line** — [`OrderProcessingService.cs:1337`](../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1337) calls `AdjustInventoryAsync` synchronously inside `MoveShoppingCartItemsToOrderItemsAsync`. 
3. **Dual-write is provable today** — [`EntityRepository.cs:341-350`](../nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs#L341) commits, then publishes. Quote this in ADR #1.
4. **No existing message broker** — zero refs to RabbitMQ, MassTransit, Hangfire. We're first.
5. **Plugin + `IScheduleTask` is enough** — outbox dispatcher fits as a normal nopCommerce plugin. No core fork needed.

---

## What we leverage (already in the codebase)

| What we need | What's already there |
|---|---|
| Reservation primitive | `ReservedQuantity`, `ReserveInventoryAsync`, `BookReservedInventoryAsync` |
| Background dispatcher | `IScheduleTask` + `TaskScheduler` (in-process polling) |
| DI extension point | `INopStartup` (registered by plugin) |
| Domain events | `IEventPublisher` + `IConsumer<T>` (in-process, sync) |
| Test harness for spike | `BaseNopTest` with SQLite in-memory + auto-migrations |
| Carrier abstraction precedent | `IShippingRateComputationMethod` (UPS plugin as template) |

---

## What we add (the actual work)

- A plugin `Nop.Plugin.Misc.OmnichannelOutbox` that:
  - registers a wrapping `IEventPublisher` writing to an `OutboxMessage` table
  - registers an `IScheduleTask` dispatcher that drains the outbox to RabbitMQ
- An **Inventory Sync Service** (own DB) consuming `OrderPlaced`, talking to OpenBoxes via ACL
- A **Shipping Service** (own DB) consuming `OrderConfirmed`, talking to WireMock via ACL
- A **Catalog stock projection** consumer of `StockLevelChanged` (lives inside the monolith)
- An explicit `Compensated` state in the order state machine

---

## Top risks

| # | Risk | Status | Mitigation |
|---|---|---|---|
| **R2** | `PlaceOrderAsync` does not run in an ambient `TransactionScope` | **CONFIRMED** by [spike](07-feasibility-spike.md) — only `EntityRepository` bulk variants wrap, and even those publish events after `Complete()` | Outbox plugin wraps the order-creation section in `TransactionScope` via an `IOrderProcessingService` decorator. Honest fallback: at-least-once + idempotent consumers. See [ADR-1](adr/0001-transactional-outbox.md). |
| **R1** | Wrapping `IEventPublisher` globally may collide with unrelated consumers (Brevo, Omnisend, …) | Open | Whitelist a fixed set of cross-context event types; all others pass through unchanged. |
| **R3** | `IScheduleTask` polling cadence vs. QA-2's 30 s P95 propagation target | Open — Part 2 measurement | Cadence at 5–10 s; measure under k6 load. |
| **R4** | Default cache is in-memory → no horizontal scaling | Acknowledged scope cut | Single-instance demo; Redis is future work. |
| **R5** | Outbox table grows without bound | Open | Daily retention `IScheduleTask` deletes dispatched messages older than 7 days. |

---

## Honest scope cuts (call these out, don't hide them)

- **Identity** stays as-is (no federated SSO across channels)
- **Payment** folded into Order Management
- **Shipping = Fulfillment** (one extracted service, not two)
- **Single nopCommerce instance** for the demo (Redis is future work)
- Outbox lives **inside the monolith process**, not as a separate worker

---

## What "good" looks like at the demo

1. Place an order → see outbox row → see RabbitMQ message → see Inventory reserve stock → order moves to `Confirmed` (happy path)
2. Kill the warehouse → place orders → orders accepted, queued in outbox, customer not blocked
3. Restart the warehouse → outbox drains → orders reconcile within minutes
4. Force a rejection → order transitions to `Compensated`, payment released, customer notified
5. Show audit trail: outbox table, RabbitMQ DLQ, order state transitions with timestamps

If steps 2 and 3 work live in the presentation, the assignment's mandatory pressure point is satisfied.
