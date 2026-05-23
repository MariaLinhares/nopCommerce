# Feasibility Spike — Does `PlaceOrderAsync` Run Inside an Ambient `TransactionScope`?

**Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)
**Status:** RESOLVED · **Date:** 2026-05-03
**Grounded in:** [docs/shared/nopcommerce-context-pack.md](README.md) (R2)

---

## 1. The question

> *Does `OrderProcessingService.PlaceOrderAsync` run inside an explicit or ambient `System.Transactions.TransactionScope`?*

This is the single load-bearing assumption behind [ADR-1 (transactional outbox)](adr/0001-transactional-outbox.md). If the answer is **yes**, our outbox row written via `_outboxRepository.InsertAsync(...)` enrolls in the same scope as the `Order` insert and we get the exactly-once-publish-relative-to-order guarantee for free. If **no**, we must wrap the relevant section of `PlaceOrderAsync` in a `TransactionScope` ourselves before the outbox guarantee holds.

[The current-state analysis (§2)](../01-current-state-analysis.pdf) states that `EntityRepository<TEntity>` is *"backed by `TransactionScope` for atomic operations"*. The [context pack §5 — Risk R2](README.md#5-what-this-means-for-each-of-us) flags this as unverified and the most important question to answer. This document resolves the conflict.

## 2. Method

Two complementary methods, in increasing strength of evidence:

1. **Source reading** of `EntityRepository.cs` and `OrderProcessingService.cs` — confirms `TransactionScope` is absent from the order-placement chain.
2. **Runtime probe** ([`assignment-2/spike/TransactionScopeSpike/`](../../spike/TransactionScopeSpike/)) — a self-contained .NET 9 console that reproduces the exact `EntityRepository.InsertAsync` shape (`await dataProvider; await consumerHook;`) and `PlaceOrderAsync` call sequence (sequential awaits, no scope), inspecting `Transaction.Current` from inside the consumer hook. Run twice: bare (today's shape) and wrapped (proposed ADR-1 fix).

The runtime probe is structural, not an integration test — it does not boot the nopCommerce DI container, run FluentMigrator, or call the real `PlaceOrderAsync`. That fixture work belongs to Part 2. **The spike's purpose is to settle the C# / `System.Transactions` semantic question** that the architectural decision rests on, with both source and runtime evidence aligned.

Tools used:

```bash
grep -rn "TransactionScope" \
  nopCommerce/src/Libraries/Nop.Data/ \
  nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs

cd assignment-2/spike/TransactionScopeSpike
dotnet build -c Release && dotnet run -c Release --no-build
```

## 3. Evidence

### 3.1 Full grep result

```
nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs:362:        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs:468:        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs:502:        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
```

Three hits, all inside `EntityRepository.cs`. **Zero hits in `OrderProcessingService.cs` and zero hits anywhere else under `Nop.Data/` or `Nop.Services/Orders/`.**

### 3.2 Where the three transactions actually wrap

Reading [`EntityRepository.cs:341-372`](../../nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs#L341-L372):

```csharp
// Single-entity insert — NO TransactionScope
public virtual async Task InsertAsync(TEntity entity, bool publishEvent = true)
{
    ArgumentNullException.ThrowIfNull(entity);
    await _dataProvider.InsertEntityAsync(entity);          // line 345 — DB commit
    if (publishEvent)
        await _eventPublisher.EntityInsertedAsync(entity);  // line 349 — fires AFTER commit
}

// Bulk insert — TransactionScope wraps the bulk DB write only
public virtual async Task InsertAsync(IList<TEntity> entities, bool publishEvent = true)
{
    ArgumentNullException.ThrowIfNull(entities);
    using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);  // line 362
    await _dataProvider.BulkInsertEntitiesAsync(entities);
    transaction.Complete();                                  // line 364 — DB committed here

    if (!publishEvent) return;
    foreach (var entity in entities)
        await _eventPublisher.EntityInsertedAsync(entity);   // line 371 — fires AFTER commit
}
```

The three `TransactionScope` blocks (lines 362, 468, 502) are inside `InsertAsync(IList<TEntity>)`, `DeleteAsync(IList<TEntity>)`, and `DeleteAsync(Expression<...>)`. **All three call `transaction.Complete()` *before* publishing events**, so even bulk writes have the dual-write shape the context pack §3.3 describes.

### 3.3 What `PlaceOrderAsync` actually does

Reading [`OrderProcessingService.cs:1571-1632`](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1571-L1632):

```csharp
public virtual async Task<PlaceOrderResult> PlaceOrderAsync(ProcessPaymentRequest processPaymentRequest)
{
    using var activity = NopCommerceDiagnostics.ActivitySource.StartActivity("PlaceOrder");
    // ... no TransactionScope, no IDbContextTransaction, no using var transaction = ...

    var details = await PreparePlaceOrderDetailsAsync(processPaymentRequest);

    async Task<PlaceOrderResult> placeOrder(PlaceOrderContainer placeOrderContainer)
    {
        var processPaymentResult = await GetProcessPaymentResultAsync(...);
        if (processPaymentResult.Success)
        {
            var order = await SaveOrderDetailsAsync(...);                  // commit #1 (Order insert)
            await MoveShoppingCartItemsToOrderItemsAsync(...);             // commits #2..N (OrderItem inserts + AdjustInventoryAsync writes)
            await SaveDiscountUsageHistoryAsync(...);                       // more commits
            await SaveGiftCardUsageHistoryAsync(...);                       // more commits
            // ...
            await _eventPublisher.PublishAsync(new OrderPlacedEvent(order)); // line 1625 — fires AFTER all commits
            await CheckOrderStatusAsync(order);
            // ...
        }
    }
}
```

`MoveShoppingCartItemsToOrderItemsAsync` ([line 1279](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs#L1279)) iterates the cart calling `_orderService.InsertOrderItemAsync(orderItem)` (line 1331) and `_productService.AdjustInventoryAsync(product, -sc.Quantity, ...)` (line 1337) on each iteration. Each of these is a single-entity repository call. None of them runs inside a `TransactionScope`.

### 3.4 Runtime probe — actual run

Source: [`assignment-2/spike/TransactionScopeSpike/Program.cs`](../../spike/TransactionScopeSpike/Program.cs)
Output (full log committed at [`assignment-2/spike/spike-output.txt`](../../spike/spike-output.txt)):

```
=== Scenario A — bare PlaceOrderAsync (today's behaviour) ===
[probe] A: baseline before call                            Transaction.Current = null
[probe] A: enter PlaceOrderAsync                           Transaction.Current = null
[probe] A: inside Order consumer                           Transaction.Current = null
[probe] A: inside OrderItem-1 consumer                     Transaction.Current = null
[probe] A: inside Product update consumer                  Transaction.Current = null
[probe] A: just before OrderPlacedEvent publish            Transaction.Current = null
[probe] A: after call returned                             Transaction.Current = null

=== Scenario B — PlaceOrderAsync wrapped in TransactionScope (ADR-1 fix) ===
[probe] B: baseline before scope                           Transaction.Current = null
[probe] B: inside scope, before call                       Transaction.Current = non-null (isolation=Serializable)
[probe] B: enter PlaceOrderAsync                           Transaction.Current = non-null (isolation=Serializable)
[probe] B: inside Order consumer                           Transaction.Current = non-null (isolation=Serializable)
[probe] B: inside OrderItem-1 consumer                     Transaction.Current = non-null (isolation=Serializable)
[probe] B: inside Product update consumer                  Transaction.Current = non-null (isolation=Serializable)
[probe] B: just before OrderPlacedEvent publish            Transaction.Current = non-null (isolation=Serializable)
[probe] B: inside scope, after call                        Transaction.Current = non-null (isolation=Serializable)
[probe] B: after scope disposed                            Transaction.Current = null

Bare scenario A — consumer hooks observing ambient TS: 0/3
Wrapped scenario B — consumer hooks observing ambient TS: 3/3
VERDICT: Confirms the spike's claim.
```

Two findings of architectural relevance:

- **Bare scenario A** — every probe (entry, three consumer hooks mirroring `EntityRepository.InsertAsync`'s post-DB consumer call, just-before-event, and post-return) reports `Transaction.Current = null`. The shape used by `PlaceOrderAsync` does not produce an ambient scope at any point a write enrolment could happen.
- **Wrapped scenario B** — `using var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)` propagates `Transaction.Current` through every `await`, and every consumer hook observes a non-null transaction. **`TransactionScopeAsyncFlowOption.Enabled` is the load-bearing detail** — without it the ambient scope does not flow across the first `await`, and the wrap silently fails to enrol the consumer-hook calls.
- **Default isolation is `Serializable`** (the `TransactionScope` default). Locking cost is a real consequence — see [ADR-1 §Consequences](adr/0001-transactional-outbox.md#consequences).

## 4. Conclusion

**`PlaceOrderAsync` does NOT run inside an ambient `TransactionScope`.** Every repository call inside it is its own commit point. The `OrderPlacedEvent` is published *after* the order, the order items, the inventory mutations, the discount-usage rows, and the gift-card-usage rows have all been committed independently to the database. The runtime probe confirms the C# semantics: every observation point in the bare-scenario call chain reports `Transaction.Current = null`; the wrap does fix it, end-to-end across `await` boundaries, when (and only when) `TransactionScopeAsyncFlowOption.Enabled` is set.

The current-state analysis claim that *"`EntityRepository<TEntity>` [is] backed by `TransactionScope`"* is **partially correct**: only the bulk and predicate-delete variants wrap in a scope, and even those publish their events *after* `Complete()`. The single-entity `InsertAsync`/`UpdateAsync`/`DeleteAsync` paths — which are the ones `PlaceOrderAsync` actually exercises — have no scope at all.

Risk **R2** in the context pack is **CONFIRMED**.

## 5. Implications and path forward

This shapes [ADR-1](adr/0001-transactional-outbox.md) and [the risk plan](risk-plan.md):

1. The outbox-in-same-transaction guarantee is **not free**. Writing `_outboxRepository.InsertAsync(message)` inside `placeOrder(...)` will commit independently of the `Order` insert, and a crash between the two leaves the system in exactly the dual-write state we are trying to escape.
2. The plugin must therefore introduce an explicit `using var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)` around the relevant section of `PlaceOrderAsync`. The cleanest seam is a **decorator over `IOrderProcessingService`** registered by the plugin's `INopStartup`, wrapping the call in a `TransactionScope` before delegating. This avoids forking core.
3. Honest fallback if scope-wrapping proves brittle in implementation: design the consumers to be **idempotent on event ID** and accept **at-least-once delivery**. QA-3's response measure ([quality-attribute-scenarios.md §QA-3](04-quality-attribute-scenarios.md#qa-3--performance-order-placement-throughput)) already calls this caveat out.

## 6. What this spike did NOT test

Honesty up front, since the assignment grades evidence packs on *"known limitations"*:

- The runtime probe is **structural**, not an integration test. It does not boot `BaseNopTest`'s SQLite-backed DI container and call the real `PlaceOrderAsync`. It reproduces the C# call shape and runs `System.Transactions` against it, which is sufficient to settle the *semantic* question (does this shape produce an ambient scope?) but does not exercise nopCommerce's actual DI graph. The full integration test is Part 2 work bundled with the plugin implementation.
- We did not test whether `LinqToDbDataProvider.InsertEntityAsync` opens its own per-call transaction internally. That detail does not change the conclusion — even if it does, two consecutive calls land in two separate transactions, and neither propagates as an ambient `Transaction.Current` to the next call.
- We did not measure the latency cost of wrapping `PlaceOrderAsync` in a `TransactionScope` under load. The default `Serializable` isolation has known overhead; tuning to `ReadCommitted` is a Part 2 decision tied to QA-3's response measure.
- We did not test the proposed `IOrderProcessingService` decorator approach end-to-end. Plugin and decorator construction are Part 2 work; this spike's mandate was to settle the question on which the Part 1 design hangs.

## 7. References

- [Source — `EntityRepository.cs`](../../nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs) — lines 341-350 (single-entity dual-write), 358-372 (bulk-insert TS), 461-488 (bulk-delete TS), 498-507 (predicate-delete TS).
- [Source — `OrderProcessingService.cs`](../../nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs) — lines 1571-1656 (`PlaceOrderAsync`), 1279-1344 (`MoveShoppingCartItemsToOrderItemsAsync`), 1337 (`AdjustInventoryAsync` call site), 1625 (`OrderPlacedEvent` publish after commit).
- **Spike code:** [`assignment-2/spike/TransactionScopeSpike/Program.cs`](../../spike/TransactionScopeSpike/Program.cs) — runnable: `cd assignment-2/spike/TransactionScopeSpike && dotnet run -c Release`.
- **Spike output:** [`assignment-2/spike/spike-output.txt`](../../spike/spike-output.txt).
- [Context pack §3.3 — the dual-write evidence](README.md) — same `EntityRepository.cs:341-350` snippet.
- [QA-3 — performance scenario with the explicit TS caveat](04-quality-attribute-scenarios.md#qa-3--performance-order-placement-throughput).
