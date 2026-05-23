﻿Group:
- Maria Linhares
- Rui Machado
- Guilherme Rosa
- Gabriel Silva

---

## docs/

All written deliverables are under `docs/part_1/`. Here is what each file contains:

| File | What it is |
|---|---|
| `01-current-state-analysis.pdf` | Analysis of the existing nopCommerce architecture — what exists today and where the problems are |
| `02-bounded-contexts.md` | Definition of the bounded contexts (Order Management, Inventory, Shipping, Catalog, Identity/Payment) |
| `03-context-map.md` | How those contexts relate to each other and to external systems (OpenBoxes WMS, WireMock carrier) |
| `04-quality-attribute-scenarios.md` | Concrete quality attribute scenarios (availability, consistency, latency) used to drive architectural decisions |
| `05-add-framework.md` | The ADD (Attribute-Driven Design) process applied step by step to this scenario |
| `06-target-architecture.md` | The target architecture: a transactional outbox plugin that decouples order placement from warehouse availability |
| `07-feasibility-spike.md` | Results of a technical spike confirming that `PlaceOrderAsync` does not run inside a `TransactionScope` — the key risk that shapes the design |
| `08-risk-plan.md` | Risk register with mitigations for the top 5 risks identified |

### ADRs (`adr/`)

Architecture Decision Records explaining the four main decisions made:

| ADR | Decision |
|---|---|
| `0001-transactional-outbox.md` | Use a transactional outbox to avoid dual-write between the order DB and the message broker |
| `0002-optimistic-reservation.md` | Use optimistic stock reservation at order time instead of a synchronous warehouse call |
| `0003-wiremock-carrier.md` | Use WireMock to simulate the carrier API during development and demo |
| `0004-selective-extraction.md` | Extract only Inventory and Shipping as separate services; keep everything else in the monolith |

### Diagrams (`diagrams/`)

Mermaid diagrams (`.mmd`) with rendered PNG/SVG exports illustrating: the current dual-write problem, the outbox seam, rejected alternatives, the order lifecycle state machine, the two feasibility spike scenarios, and the risk/QA traceability matrix.

### Spike (`spike/`)

A small standalone C# console app (`TransactionScopeSpike`) that was run against nopCommerce to confirm whether order placement uses an ambient `TransactionScope`. The output is saved in `spike-output.txt`. This spike directly informed ADR-1.

