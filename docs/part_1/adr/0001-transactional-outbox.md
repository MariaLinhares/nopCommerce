# ADR-1 — Reliable cross-context messaging via Transactional Outbox

**Status:** Accepted · **Date:** 2026-05-03 · **Author:** Person 4
**Related:** [QA-1 (availability)](../../person2/quality-attribute-scenarios.md#qa-1--availability-mandatory-pressure-point), [QA-3 (performance)](../../person2/quality-attribute-scenarios.md#qa-3--performance-order-placement-throughput), [Iteration 1](../../person3/add-framework.md#iteration-1--decouple-order-placement-from-inventory), [Risk R1, R2](../risk-plan.md), [Feasibility spike](../feasibility-spike.md)

---

## Context

The architectural bet of Scenario C is that the commerce core must remain useful when the warehouse is degraded — orders are accepted now, stock truth reconciles asynchronously. That bet only pays off if the message that tells the Inventory service "an order was placed" is **never lost** between the DB commit and the broker. Today nopCommerce loses that message on the slightest failure. From [`EntityRepository.cs:341-350`](../../../nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs#L341-L350):

```csharp
public virtual async Task InsertAsync(TEntity entity, bool publishEvent = true)
{
    ArgumentNullException.ThrowIfNull(entity);
    await _dataProvider.InsertEntityAsync(entity);          // DB commit
    if (publishEvent)
        await _eventPublisher.EntityInsertedAsync(entity);  // event AFTER commit
}
```

This is a textbook **dual-write**: the DB and the event publication are two unrelated commits and any failure between them produces divergence. `OrderProcessingService.cs:1625` makes the dual-write concrete for `OrderPlacedEvent`: the event publishes *after* the order, the order items, and the inventory mutations have all been committed (independently — see the [feasibility spike](../feasibility-spike.md) for evidence that `PlaceOrderAsync` does not run in an ambient `TransactionScope`).

There is no message broker today; this is the team's first integration with one ([context pack §3.4 fact 4](../../shared/nopcommerce-context-pack.md)). Whatever pattern we pick will set the precedent for every future cross-context event.

The lecturer's heuristic (slides 02.02 p.50) is the framing: *"Has money moved, or might money move? Use orchestration. Is this a downstream reaction to a confirmed transaction? Use choreography."* Order acceptance is orchestrated within the monolith (within one transactional boundary). Stock reservation, shipping, catalog projection are **downstream reactions** — choreographed via events. The seam between the two worlds must not lose messages.

## Decision

Introduce a **transactional outbox** inside the nopCommerce database.

1. Add an `OutboxMessage` table (`Id`, `MessageType`, `Payload`, `OccurredOnUtc`, `DispatchedOnUtc`, `Attempts`) via a FluentMigrator migration shipped with a new plugin `Nop.Plugin.Misc.OmnichannelOutbox`.
2. The plugin's `INopStartup` registers a **decorator over `IOrderProcessingService`** that wraps the order-creation section of `PlaceOrderAsync` in `using var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)` and writes the corresponding `OrderPlaced` outbox row inside the same scope as the `Order` insert.
3. The plugin also registers a **wrapping `IEventPublisher`** that, for a **whitelisted set of event types** (`OrderPlaced`, `OrderConfirmed`, shipment events, `StockReserved`, `ReservationRejected`, `StockLevelChanged`), writes an outbox row instead of (or in addition to) calling in-process consumers. All other events pass through unchanged — this keeps existing plugins (Brevo, Omnisend, …) untouched (see [Risk R1](../risk-plan.md)).
4. An `IScheduleTask` **outbox dispatcher** polls the table on a 5–10 s cadence, publishes undispatched rows to RabbitMQ, marks them dispatched on broker ack, and increments `Attempts` on failure. Permanent failures (`Attempts > N`) move to a dead-letter view for manual inspection. Cadence is documented under [QA-2 propagation target](../../person2/quality-attribute-scenarios.md#qa-2--consistency-stock-visibility) and [Risk R3](../risk-plan.md).
5. Consumers are **idempotent on `EventId`**. Delivery is **at-least-once** — exactly-once is not promised because we do not have a 2PC across DB and broker.

## Alternatives considered

### A. Direct RabbitMQ publish from `IConsumer<OrderPlacedEvent>` — REJECTED

The path of least implementation effort: register an `IConsumer<OrderPlacedEvent>` that opens a RabbitMQ channel and publishes the event. Person 1's analysis already identifies this as "the most viable path" *if* you assume the event publication is reliable. It is not. The exact dual-write of `EntityRepository.cs:341-350` reappears one level up: `OrderProcessingService.PlaceOrderAsync` commits the order, then publishes `OrderPlacedEvent`, then the consumer publishes to RabbitMQ. A crash, a network blip, a broker pause anywhere after the order commit loses the message permanently. The whole point of the architectural bet — *the warehouse is told, eventually* — collapses.

### B. Two-phase commit / XA across DB and broker — REJECTED

In principle, an XA transaction could span SQL Server and RabbitMQ and give us exactly-once. In practice: RabbitMQ is not an XA resource manager (its publisher confirms are a different model); LinqToDB's transaction abstraction does not coordinate across heterogeneous resources; nopCommerce supports MySQL and PostgreSQL too, neither of which the team can operate as a coordinated XA resource on a 2-day budget; and 2PC's blocking-on-coordinator-failure behaviour is famously a worse failure mode than at-least-once. We reject 2PC even before considering operational cost.

### C. CDC / Debezium-style log mining — REJECTED

Tail the SQL Server transaction log, project order inserts into messages, publish to RabbitMQ. This is a real outbox-equivalent pattern in production systems. We reject it for two scope reasons: (1) it requires SQL Server CDC enabled (an operational dependency the assignment scenario does not motivate), and (2) it ties the design to a specific DB engine, whereas nopCommerce supports SQL Server, MySQL, and PostgreSQL ([context pack §4.2](../../shared/nopcommerce-context-pack.md)). The transactional-outbox-in-the-application-DB approach is engine-agnostic.

### D. Synchronous RabbitMQ publish inside the order transaction — REJECTED

Open the broker connection inside the order's DB transaction and publish before commit. Now the broker is in the critical path of order acceptance — the very coupling we are trying to break for QA-1. A slow or down broker = a slow or down checkout. This is structurally identical to today's synchronous UPS call ([context pack §4.5](../../shared/nopcommerce-context-pack.md)) and we explicitly chose a different shape.

## Consequences

**Positive:**
- The order acceptance path is decoupled from the broker. A RabbitMQ outage accumulates outbox rows; a recovered broker drains them. QA-1's 99 %-success-during-15-min-outage target becomes a property of the system, not a hope.
- Existing nopCommerce extension points (`INopStartup`, `IConsumer<T>`, `IScheduleTask`, `IEventPublisher`) carry the entire design — no core fork.
- The outbox table itself is the operator-visible audit trail: every cross-context message has a row with timestamps and dispatch status. This satisfies a chunk of [QA-5 (operability)](../../person2/quality-attribute-scenarios.md#qa-5--operability-optional-5th-suggested-addition) at no extra cost.

**Negative / new operational concerns:**
- Delivery is **at-least-once**. Every consumer must be idempotent on `EventId`. This is a hard rule, not a guideline; consumers that violate it will double-process on redelivery.
- Propagation lag is bounded below by the dispatcher cadence. 5–10 s is comfortable for QA-2's 30 s P95 target but consumes DB read load. See [Risk R3](../risk-plan.md).
- The outbox table grows monotonically without intervention. A retention `IScheduleTask` deletes dispatched rows older than 7 days (configurable). See [Risk R5](../risk-plan.md).
- We must wrap `PlaceOrderAsync` in a `TransactionScope` ourselves — it has none today ([feasibility spike](../feasibility-spike.md), runtime confirmed). Done via the `IOrderProcessingService` decorator. **Two non-obvious details from the spike:** (1) the wrapper must use `TransactionScopeAsyncFlowOption.Enabled` or `Transaction.Current` does not flow across the first `await` and the enrolment silently fails; (2) `TransactionScope`'s default isolation is `Serializable` — for QA-3's latency target we will likely tune this down to `ReadCommitted` (`new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled)`). Tuning is Part 2; the default Serializable would otherwise be a hidden tax on order-placement throughput.
- The wrapping `IEventPublisher` is whitelist-based to avoid breaking unrelated `IConsumer<T>` plugins. Adding a new cross-context event requires editing the whitelist — a deliberate friction, not an oversight. See [Risk R1](../risk-plan.md).

**Carries forward into:**
- [ADR-2 (optimistic reservation)](0002-optimistic-reservation.md) — the `OrderPlaced` row in the outbox is what the Inventory service consumes.
- [Iteration 2 of the ADD plan](../../person3/add-framework.md#iteration-2--stock-visibility-projection) — the stock-view projection consumes `StockLevelChanged` from the same outbox.
