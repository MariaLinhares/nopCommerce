# ADR-3 — Carrier surrogate via WireMock

**Status:** Accepted · **Date:** 2026-05-03 · 
**Related:** [QA-1 pressure point precedent](../04-quality-attribute-scenarios.md#qa-1--availability-mandatory-pressure-point), [Iteration 3](../05-add-framework.md#iteration-3--shipping-integration-and-compensation), [Bounded contexts §2.1 — Shipping](../02-bounded-contexts.md)

---

## Context

The [Shipping / Fulfillment context](../02-bounded-contexts.md) is one of two extracted services in the target architecture. It must be exercisable under **carrier degradation** — slow responses, 5xx, contradictory states — because that is the runtime challenge we promise to demonstrate. The assignment scenario brief explicitly allows *"a surrogate simulator, stub, or mock service if that choice preserves the pressure, behavior, and architectural consequences of the scenario."*

The current carrier integration in nopCommerce sets the precedent we are evolving away from. [`Nop.Plugin.Shipping.UPS/Services/UPSService.cs`](../../../nopCommerce/src/Plugins/Nop.Plugin.Shipping.UPS/Services/UPSService.cs) makes synchronous in-request HTTP calls to UPS via `IHttpClientFactory`, with no retry, no circuit breaker, and no background queue ([context pack §4.5](../../shared/nopcommerce-context-pack.md)). A slow UPS = a slow checkout. The architectural pressure of this design is **identical in shape** to the warehouse pressure of QA-1: a synchronous in-request external call that blocks the customer-visible path.

We need a stand-in that **preserves that pressure shape** without requiring a real carrier account or network access during evaluation, and that lets us inject specific failure modes on demand. The lecturer's stance from the DDD deck (p.~55) is the framing for the integration: *"use an Anti-Corruption Layer unless you have a good reason not to."* The ACL contract becomes the boundary the Shipping service exposes regardless of which surrogate sits behind it.

## Decision

Use **WireMock** (containerised, `docker-compose.demo.yml`) as the carrier stand-in for the demo and the Part 2 evidence pack.

1. WireMock is configured via stub mappings (JSON files in `assignment-2/wiremock/mappings/`) that simulate a generic-carrier rate-quote / dispatch / tracking API. The exact shape mirrors a small subset of UPS/FedEx semantics — enough to make the architectural shape recognisable to a reviewer, no more.
2. The Shipping Integration Service talks to WireMock through an **Anti-Corruption Layer** — an internal `ICarrierGateway` interface that translates WireMock's rate/dispatch responses into the Shipping context's published language (`ShipmentDispatched`, `ShipmentDelivered`, `ShipmentFailed`).
3. **Failure injection** is the entire point: we add stub mappings that simulate (a) latency (`fixedDelayMilliseconds`), (b) 5xx responses on a percentage of requests, and (c) **contradictory states** (e.g. dispatch succeeded but tracking returns "not found"). These are toggled via WireMock admin API or by swapping mapping files for the demo.
4. The ACL is the integration-test surface. Tests assert on the ACL's translated outputs, not on raw WireMock responses, so swapping WireMock for a real carrier later (future work) requires no test changes.

## Alternatives considered

### A. Real carrier sandbox API (UPS / FedEx test credentials) — REJECTED

The most "realistic" choice. Pros: actually exercises a real-world API; arguably the strongest demo. Cons: (1) requires test-account credentials, which neither the team nor the evaluator have on hand; (2) is rate-limited and network-dependent — the demo cannot run reliably offline or under exam-room conditions; (3) **cannot inject contradictory or 5xx states on demand** — the carrier returns whatever its sandbox returns, so the runtime challenge becomes "wait for the sandbox to be slow", which is not a reproducible experiment; (4) operational cost (key rotation, account creation) is disproportionate. Rejected primarily on (3): the assignment grades "claims of resilience without evidence" as a weak submission, and we cannot generate the evidence reliably with a real sandbox.

### B. Custom in-process stub (a hand-written `FakeCarrier`) — REJECTED

Write a small ASP.NET Core minimal-API project that returns canned carrier responses. Pros: full control, zero new dependencies. Cons: (1) re-invents WireMock — there is no architectural insight in writing yet another stub; (2) the reviewer does not recognise the *shape* of the pressure (custom code looks like business logic, not a surrogate); (3) we lose WireMock's standard failure-injection knobs (delays, percentage failures, request-matching DSL); (4) maintenance debt is ours. Rejected as scope inflation that buys no architectural credibility.

### C. Shared mock with the warehouse (one container handles both surrogates) — REJECTED

Combine WireMock instances or use a single multi-purpose mock. Pros: marginally simpler `docker-compose`. Cons: collapses two distinct contexts (warehouse, carrier) into one operational boundary, which the assignment explicitly forbids: *"do not use a shared database as a shortcut across extracted service boundaries"* — and the spirit of that constraint extends to shared mocks. The Shipping ACL must be testable independently of the Inventory ACL. Rejected as boundary-blurring.

### D. No carrier surrogate at all — only outbox-message-level demonstration — REJECTED

Stop the demo at "Shipping service consumes `OrderConfirmed` and writes a row". Pros: minimal scope. Cons: the assignment's mandatory pressure point requires *"a surrounding operational system becoming delayed, stale, unavailable, or contradictory while the commerce core must remain useful"* — without a carrier behind the Shipping service, there is nothing to make stale. Rejected because it makes Iteration 3 vacuous.

## Consequences

**Positive:**
- The carrier-degradation pressure point is **reproducibly demonstrable**. The same demo runs identically on every machine that has Docker.
- WireMock is a recognised standard tool in the integration-testing world — the reviewer sees a familiar shape and does not need to evaluate our stub's faithfulness.
- The ACL boundary stays the same whether WireMock or a real carrier sits behind it, so future work (swap to a real sandbox) is mechanical.
- Failure-injection knobs (delay, 5xx %, contradictory responses) are configurable without code changes — the Part 2 evidence pack can include screenshots of mappings being toggled live.

**Negative / known limitations** (called out explicitly per assignment §4.5 evidence-pack requirement):
- WireMock cannot reveal real-carrier quirks (idiosyncratic field names, undocumented retry semantics, regional API differences). The architecture report must state plainly: *the architectural pressure is preserved; the carrier-specific edge cases are not.*
- The Shipping ACL is scoped to what WireMock simulates. Adding a real carrier later may require extending the ACL with translation logic we did not anticipate — known unknown.
- WireMock is one more container in the demo `docker-compose`, increasing the surface for "demo doesn't start" failures. Mitigated by pinning the WireMock image version and committing the mapping files.
- We cannot demo "real-world rate-quote latency variance" — only the latency we configure WireMock to inject. The architecture report should be honest that the demo's latency numbers are illustrative of *the architecture's response*, not of *real-carrier behaviour*.
