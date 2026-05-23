# ADR-4 — Selective extraction: Inventory + Shipping out, Order Management + Catalog stay

**Status:** Accepted · **Date:** 2026-05-03 · 
**Related:** [Bounded contexts](../02-bounded-contexts.md), [Target architecture](../06-target-architecture.md), [Assignment §3 scope clause](../../Group%20Assignment%20-%20Final%20Assignment.pdf), [ADR-1](0001-transactional-outbox.md), [ADR-2](0002-optimistic-reservation.md)

---

## Context

The assignment is explicit: *"This is not a 'rewrite nopCommerce into microservices' assignment. A smaller, defensible architecture is better than an inflated design that is only convincing in diagrams."* The team must therefore justify, with rejected alternatives, the specific cut between **what stays in the monolith** and **what becomes an independently deployable service**.

The natural pull is in two directions. From one side, scenario pressure (warehouse and carrier degradation) demands that those subsystems have **independent failure domains** — they must be able to fail without taking down order acceptance. From the other side, [`OrderProcessingService`](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs) is the god-service of nopCommerce: it injects 30+ dependencies ([context pack §2.4](../../shared/nopcommerce-context-pack.md)) and its state machine is implicit and scattered across [`CheckAndSaveOrderStatusAsync`](../../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1484). Extracting it would mean replicating half of `Nop.Services`.

The lecturer's framing applies twice. From the DDD deck (p.~41): *"aggregate boundaries are scaling decisions, not only modeling decisions"* — extract only when concurrency or failure-domain pressure demands it. From the principles deck (p.~36): *"a good boundary aligns owner, language, and reason to change"* — when one team would still own both halves of an extraction, the cut is premature.

## Decision

Extract exactly two services, leave the rest inside the monolith:

| Subsystem | Decision | Reason |
|-----------|----------|--------|
| **Inventory / Warehouse Sync** | Extract — own process, own DB | Independent failure domain (QA-1); ACL in front of OpenBoxes; assignment requires ≥1 independently deployable subsystem |
| **Shipping / Fulfillment Integration** | Extract — own process, own DB | Independent failure domain for carrier degradation; ACL in front of WireMock ([ADR-3](0003-wiremock-carrier.md)) |
| **Order Management** | Stay — evolve in place | 30+ dependencies; implicit state machine; the `Compensated` state in [ADR-2](0002-optimistic-reservation.md) is an *evolution*, not an extraction |
| **Catalog (incl. stock-view projection)** | Stay — local read model | Read-heavy, low scenario pressure; consuming `StockLevelChanged` from RabbitMQ inside the monolith avoids an extra network hop on every product page |
| **Identity / Customer** | Stay — as-is | No QA scenario drives change; honest scope cut on federated SSO |

The two extractions get **own databases** — the assignment-hard "no shared DB across extracted boundaries" rule. nopCommerce keeps its existing SQL Server (with the new outbox table); Inventory Sync owns its own (e.g. SQLite or Postgres for the demo); Shipping Integration owns its own.

The decision is *selective* on purpose: each extraction is justified by a specific QA scenario whose failure mode the monolith cannot absorb. Each non-extraction is justified by absence of such pressure.

## Alternatives considered

### A. Full microservices decomposition (Order, Catalog, Customer, Cart, Payment all out) — REJECTED

The maximalist read of "modernize nopCommerce". Pros: clean bounded contexts, independent scaling, fashionable. Cons: (1) **violates the assignment's explicit scope clause** ("not a microservices rewrite"); (2) no QA scenario in [QA-1..QA-5](../04-quality-attribute-scenarios.md) demands extracting Order Management, Catalog, Customer, Cart, or Payment — extracting them would be ideology, not architecture; (3) the team has 2 days until checkpoint and ~4 weeks to final delivery — full decomposition is undeliverable; (4) the lecturer's deck (slides 02.02 p.46) explicitly warns that choreography across many services *"distributes accountability — that's the cost"*. Rejected as scope inflation that the assignment grader penalises directly.

### B. Keep everything in the monolith, add only the outbox — REJECTED

Add the transactional outbox ([ADR-1](0001-transactional-outbox.md)) but keep stock truth and shipping logic inside `Nop.Services`. Pros: simplest implementation; minimal change. Cons: the warehouse's failure domain is still the monolith's failure domain. If `nopcommerce` itself is overloaded, **so is the inventory subsystem** — there is no isolation. QA-1 cannot be honestly demonstrated because the "warehouse outage" is not a real outage, just a faked one inside a process that is fine. Rejected because it makes the mandatory pressure-point demonstration vacuous.

### C. Extract Order Management instead of Inventory — REJECTED

A defensible alternative: many architectures extract the order/payment core first because it is the highest-value transactional engine. Pros: aligns with "money-has-moved" boundary. Cons: (1) `OrderProcessingService` injects 30+ dependencies — the extraction surface is enormous; (2) the order state machine is implicit and tangled; refactoring it to be explicit *while also* extracting it doubles the risk; (3) the QA scenarios that drive scenario C are about **surrounding-system degradation**, not about scaling order placement itself. Rejected because the cost is high and the QA driver is missing.

### D. Single mega-service for "Operations" (Inventory + Shipping merged) — REJECTED

Combine Inventory and Shipping into one extracted service, since both are "non-storefront operations". Pros: one fewer process. Cons: the two have **different failure domains** (warehouse vs carrier) and **different ACLs** (OpenBoxes vs WireMock). Merging them means a carrier outage and a warehouse outage share blast radius — which is exactly the kind of hidden coupling the assignment §5 calls out as a weak-submission tell. Rejected because the boundaries are real even if the deployment count would be smaller.

### E. Extract Catalog as a read-only stock-view service — REJECTED

The stock-view projection (Iteration 2 of [the ADD plan](../05-add-framework.md#iteration-2--stock-visibility-projection)) is a genuinely separate concern. Pros: clean projection-as-a-service shape; recognised pattern. Cons: the storefront reads stock visibility on **every product page**. An extra network hop on every product impression is a real performance cost for a marginal architectural win. The target architecture explicitly keeps the projection in-process for this reason. Rejected on performance grounds.

## Consequences

**Positive:**
- Two extractions are the **minimum** that satisfies the assignment's "≥1 independently deployable subsystem" constraint *and* gives both pressure points (warehouse, carrier) their own failure domains.
- The cut maps cleanly to the QA scenarios: each extraction has a named QA scenario it serves; each non-extraction has no such scenario.
- The outbox + RabbitMQ from [ADR-1](0001-transactional-outbox.md) is the **only seam** between monolith and extracted services. One seam is easier to defend, monitor, and operate than many.

**Negative / honest scope cuts:**
- **Single nopCommerce instance** for the demo. The default in-memory cache ([context pack §4.3](../../shared/nopcommerce-context-pack.md)) does not support horizontal scale; Redis is future work. Documented in [Risk R4](../risk-plan.md).
- **No federated identity** across channels. The Identity context stays as-is; SSO across POS/web/store-app is a real omnichannel concern but does not drive any QA scenario in this assignment. Honest cut.
- **Shipping = Fulfillment** — merged into one extracted service. nopCommerce already models them as one (`Shipment` is created from `OrderProcessingService.ShipAsync`); separating them here would be inventing scope.
- **Order Management's state machine is implicit today**. [ADR-2](0002-optimistic-reservation.md) adds the `Compensated` state cleanly, but a full FSM extraction (e.g. `IOrderWorkflow`) is *not* part of this evolution. We accept that the state machine will remain partly scattered through `CheckAndSaveOrderStatusAsync` after this evolution.
- **Two new deployables, two new databases** — operational cost increases. The `docker-compose.demo.yml` carries five containers (nopCommerce, RabbitMQ, Inventory service, Shipping service, WireMock) plus the existing SQL Server; this is the minimum to make the architectural story runnable, and it is also the maximum the demo budget supports.
