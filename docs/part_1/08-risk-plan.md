# Risk Plan

**Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)
**Status:** v1 (post-spike) · **Date:** 2026-05-03
**Grounded in:** [docs/shared/nopcommerce-context-pack.md §5](README.md) — formalises R1–R4; adds R5.

Likelihood / Impact use a deliberately coarse **H / M / L** scale. Coarseness is honest: we do not have production telemetry from VerdeMart and inventing percentages would be theatre. The point of the table is to make our top risks explicit and to tie each one to a validation lever we control.

---

| # | Risk | Likelihood | Impact | Mitigation | Validation lever |
|---|------|:----------:|:------:|------------|------------------|
| **R1** | Wrapping `IEventPublisher` globally collides with unrelated `IConsumer<T>` plugins (Brevo, Omnisend, etc.) and breaks behaviour they depend on | M | M | Whitelist event types instead of full replacement: the wrapping publisher writes to the outbox **only** for an enumerated set of cross-context events (`OrderPlaced`, `OrderConfirmed`, `StockReserved`, `ReservationRejected`, `StockLevelChanged`, shipment events). All other events flow through the existing in-process `EventPublisher` untouched. | Re-run the existing nopCommerce test suite after registering the plugin; assert no consumer behaviour changes for the non-whitelisted events. |
| **R2** | `PlaceOrderAsync` does not run in an ambient `TransactionScope`, so the outbox row commits independently of the `Order` insert and the dual-write reappears | **H (confirmed)** | H | Plugin registers a **decorator over `IOrderProcessingService`** that opens `using var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)` around the order-creation section, then enrolls the outbox row inside it. Honest fallback if the wrap proves brittle: at-least-once delivery + idempotent consumers keyed on `EventId` (already required for QA-4). | **CONFIRMED by [feasibility spike](feasibility-spike.md).** Validation in Part 2: an integration test that crashes the process between `Order` insert and outbox insert and asserts no `Order` row exists in the DB. |
| **R3** | `IScheduleTask` polling cadence cannot meet the QA-2 30 s P95 propagation target | L–M | M | Set the dispatcher cadence to 5–10 s for the demo (existing infrastructure already polls at sub-minute cadence — context pack §3.4). Measure tick-to-broker latency under k6 load. | Part-2 k6 run, not checkpoint-1. |
| **R4** | Default `MemoryDistributedCacheManager` blocks horizontal scale of nopCommerce, so any "throughput" claim that assumes >1 instance is unsupported | H | L (single-instance demo) | **Honest scope cut**: the demo runs a single nopCommerce instance, called out in the architecture report and presentation. Redis is listed as future work. We do not claim multi-instance throughput. | None needed at checkpoint — the cut is the mitigation. |
| **R5** | Outbox table grows without bound and degrades DB performance | M | L | Second `IScheduleTask` runs daily and deletes dispatched messages older than N days (configurable, default 7). Documented in [ADR-1 consequences](adr/0001-transactional-outbox.md). | Part-2 measurement: track outbox table size over a sustained k6 run. |

## How risks tie to the QA scenarios

- **R1, R2** are reliability risks that, if unmitigated, void [QA-1 (availability)](04-quality-attribute-scenarios.md#qa-1--availability-mandatory-pressure-point) — a lost `OrderPlaced` event means the warehouse is never told about an accepted order.
- **R2** also voids [QA-3 (performance)](04-quality-attribute-scenarios.md#qa-3--performance-order-placement-throughput)'s exactly-once language; the spike has already forced us to soften that to "at-least-once with idempotent consumers".
- **R3** is the direct threat to [QA-2 (consistency)](04-quality-attribute-scenarios.md#qa-2--consistency-stock-visibility) — propagation lag exceeding 30 s P95 is exactly what the polling cadence controls.
- **R4** is a scope-cut risk, not a failure-mode risk. Calling it out keeps the architecture report defensible against the "claims of scalability without evidence" failure mode the assignment §5 explicitly penalises.
- **R5** is operability — pairs with [QA-5 (operability)](04-quality-attribute-scenarios.md#qa-5--operability-optional-5th-suggested-addition).

## What is NOT in this table (and why)

We considered and excluded:

- **"RabbitMQ outage"** — this is exactly the pressure condition the architecture is designed to absorb (outbox accumulates, dispatcher retries on recovery). It's a *demo scenario*, not a risk to mitigate. It belongs in the evidence pack, not here.
- **"OpenBoxes outage"** — same reasoning; QA-1's stimulus.
- **"WireMock outage"** — same; degradation injection is the entire point of the WireMock choice ([ADR-3](adr/0003-wiremock-carrier.md)).
- **"Plugin discovery fails at boot"** — generic plugin-system risk; not specific to this scenario.

A risk plan that lists every conceivable failure is theatre. The five rows above are the ones whose mitigation actually shapes the architecture or the demo's scope.
