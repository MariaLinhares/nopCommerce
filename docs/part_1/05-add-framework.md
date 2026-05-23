# Chosen Framework: ADD (Attribute-Driven Design)

**Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)
**Status:** DRAFT v1
**Grounded in:** [current-state analysis](../../analysis.pdf), [QA scenarios](04-quality-attribute-scenarios.md), [bounded contexts](02-bounded-contexts.md)

---

## Why ADD

We chose Attribute-Driven Design because it starts from exactly what we already have: a set of quality attribute scenarios, a known system with clear constraints, and a need to evolve incrementally rather than redesign from scratch. ADD takes these inputs and produces architectural structures through iterative decomposition — each iteration driven by a subset of QA scenarios, each producing traceable decisions.

We considered ACDM, but its early stages (discover drivers, establish baseline) duplicate work that the team has already completed. Retrofitting their deliverables into ACDM's prescribed phases would be artificial. We also considered ADM/TOGAF, but the governance overhead is disproportionate — we are evolving one system with two extracted services, not running an enterprise-wide transformation.

ADD does not cover transition planning or experimentation. We address those gaps through the evolution roadmap and the feasibility spike, respectively.

---

## Inputs

- **Business drivers:** VerdeMart needs nopCommerce to act as a commerce core, not an isolated storefront. Cross-channel visibility and resilience under surrounding-system degradation are the primary pressures.
- **Quality attribute scenarios:** QA-1 (availability), QA-2 (consistency), QA-3 (performance), QA-4 (recoverability), QA-5 (operability) — defined in [quality-attribute-scenarios.md](04-quality-attribute-scenarios.md).
- **Constraints:** no shared database across extracted boundaries, at least one async workflow, at least one explicit reliability mechanism, extension via nopCommerce's plugin system.
- **Existing architecture:** modular monolith with synchronous stock decrement at order placement, in-process event system with no durability, no message broker — see [current-state analysis](../../analysis.pdf), pressure points P1–P5.

---

## Iteration 1 — Decouple Order Placement from Inventory

**Drives:** QA-1 (availability), QA-3 (performance)

Today, `OrderProcessingService` synchronously decrements stock during order placement. If the warehouse system is slow or down, the order fails. The event system publishes after the DB commit with no delivery guarantee — a classic dual-write.

**Decisions:**

- Introduce a transactional outbox table inside the nopCommerce database. When an order is placed, the outbox row is written in the same transaction as the order — no event can be lost.
- An `IScheduleTask`-based dispatcher drains the outbox to RabbitMQ. This leverages nopCommerce's existing background task infrastructure, so no new runtime is needed inside the monolith.
- Extract an **Inventory Sync Service** as an independently deployable process with its own database. It consumes `OrderPlaced` messages from RabbitMQ and coordinates with OpenBoxes through an anti-corruption layer.
- Order Management stays inside the monolith. The state machine is deeply coupled to existing services and extracting it would be riskier than evolving it in place. It now accepts orders optimistically and waits for async confirmation from Inventory.

**Satisfies:** QA-1 — orders accepted during warehouse outage (target: 99% success for up to 15 min). QA-3 — synchronous stock call removed from order placement path (target: P95 latency ≤ 800ms).

---

## Iteration 2 — Stock Visibility Projection

**Drives:** QA-2 (consistency)

After Iteration 1, the Inventory service owns stock truth, but the storefront still needs fast local reads for product pages. Today, `ProductService.GetTotalStockQuantityAsync` computes availability from local tables — that computation shape is fine, but the data source must change.

**Decisions:**

- The Inventory service publishes `StockLevelChanged` events to RabbitMQ whenever stock moves (reservation, replenishment, manual adjustment).
- Inside the monolith, an `IConsumer<StockLevelChanged>` updates a local read-model table. The storefront queries this projection rather than the old stock fields.
- The read model stays inside nopCommerce — a separate projection service would add scope and latency without improving the architectural story.
- When propagation lag exceeds 60 seconds, the UI surfaces a staleness indicator so customers see honest information.

**Satisfies:** QA-2 — P95 propagation from warehouse change to storefront visibility ≤ 30 seconds under normal load.

---

## Iteration 3 — Shipping Integration and Compensation

**Drives:** QA-4 (recoverability)

There is currently no automated carrier dispatch and no compensation path when the warehouse rejects an order. The order state machine is implicit, with transitions scattered across `CheckAndSaveOrderStatusAsync`.

**Decisions:**

- Extract a **Shipping Integration Service** as an independently deployable process with its own database. It consumes `OrderConfirmed` messages and talks to the carrier (WireMock-simulated) through an anti-corruption layer.
- Add an explicit `Compensated` state to the order lifecycle. When Inventory publishes `ReservationRejected`, Order Management transitions the order to `Compensated`, releases the payment authorization, and notifies the customer.
- Compensation is idempotent — a deduplication key on the event ID prevents double-processing if the same rejection is delivered twice.
- The order state machine stays in the monolith. Making it explicit (with named states and clear transitions) is the evolution — extracting it is not justified at this stage.

**Satisfies:** QA-4 — rejected orders reach `Compensated` within 2 minutes, with payment released and customer notified.

---

## Traceability Summary

| Iteration | QA Scenarios | Key Decision | Components |
|---|---|---|---|
| 1 | QA-1, QA-3 | Transactional outbox + Inventory Sync Service | nopCommerce (outbox plugin), Inventory Service, RabbitMQ |
| 2 | QA-2 | Event-driven stock projection | nopCommerce (catalog read model), Inventory Service |
| 3 | QA-4 | Shipping service + compensation path | Shipping Service, nopCommerce (order state machine) |
| Cross-cutting | QA-5 | Correlation IDs + outbox audit trail | All components |
