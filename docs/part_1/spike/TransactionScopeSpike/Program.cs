// Feasibility spike — does PlaceOrderAsync run inside an ambient TransactionScope?
//
// Strategy: reproduce the exact code shape used by nopCommerce's
// EntityRepository.InsertAsync (single-entity variant) and the call sequence
// used by OrderProcessingService.PlaceOrderAsync. Probe Transaction.Current
// from inside the consumer hook (the same place EntityInsertedAsync runs in
// the real codebase).
//
// Scenario A: replicate today's PlaceOrderAsync — no TransactionScope wrapper.
// Scenario B: wrap PlaceOrderAsync in TransactionScope (the proposed fix from
//             ADR-1 §Decision step 2). Confirms the wrapper actually flows.
//
// References (paths relative to repo root):
//   nopCommerce/src/Libraries/Nop.Data/EntityRepository.cs:341-350  — InsertAsync(single)
//   nopCommerce/src/Libraries/Nop.Services/Orders/OrderProcessingService.cs:1571-1632 — PlaceOrderAsync

using System.Transactions;

var observations = new List<(string label, bool ambientPresent, string? isolation)>();

// Mimics EntityRepository<T>.InsertAsync(entity, publishEvent:true) — one
// data-provider await, then a consumer-publish await. THIS IS THE EXACT SHAPE
// at EntityRepository.cs:341-350.
async Task InsertAsync(string entityName, Action<string> consumerHook)
{
    // _dataProvider.InsertEntityAsync(entity)
    await Task.Yield();

    // _eventPublisher.EntityInsertedAsync(entity)
    await Task.Yield();
    consumerHook(entityName);
}

void Probe(string label)
{
    var tx = Transaction.Current;
    observations.Add((label, tx is not null, tx?.IsolationLevel.ToString()));
    Console.WriteLine($"[probe] {label,-50} Transaction.Current = {(tx is null ? "null" : $"non-null (isolation={tx.IsolationLevel})")}");
}

// Mimics OrderProcessingService.PlaceOrderAsync — sequential awaits over
// repository calls. NO TransactionScope wrapper, matching the real source.
async Task PlaceOrderAsync(string scenario)
{
    Probe($"{scenario}: enter PlaceOrderAsync");

    // SaveOrderDetailsAsync -> _orderService.InsertOrderAsync(order)
    await InsertAsync("Order", _ => Probe($"{scenario}: inside Order consumer"));

    // MoveShoppingCartItemsToOrderItemsAsync — first OrderItem
    await InsertAsync("OrderItem-1", _ => Probe($"{scenario}: inside OrderItem-1 consumer"));

    // _productService.AdjustInventoryAsync — single-warehouse path
    // updates Product.StockQuantity then UpdateProductAsync (single-entity).
    await InsertAsync("Product (stock adjustment)", _ => Probe($"{scenario}: inside Product update consumer"));

    // _eventPublisher.PublishAsync(new OrderPlacedEvent(order))
    Probe($"{scenario}: just before OrderPlacedEvent publish");
}

Console.WriteLine("=== Scenario A — bare PlaceOrderAsync (today's behaviour) ===");
Probe("A: baseline before call");
await PlaceOrderAsync("A");
Probe("A: after call returned");

Console.WriteLine();
Console.WriteLine("=== Scenario B — PlaceOrderAsync wrapped in TransactionScope (ADR-1 fix) ===");
Probe("B: baseline before scope");
using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    Probe("B: inside scope, before call");
    await PlaceOrderAsync("B");
    Probe("B: inside scope, after call");
    ts.Complete();
}
Probe("B: after scope disposed");

// Summary table
Console.WriteLine();
Console.WriteLine("=== Summary ===");
Console.WriteLine($"{"Probe",-55} {"Ambient TS",-12} Isolation");
Console.WriteLine(new string('-', 90));
foreach (var (label, present, iso) in observations)
    Console.WriteLine($"{label,-55} {(present ? "PRESENT" : "absent"),-12} {iso ?? "-"}");

Console.WriteLine();

// Verdict — derived from the observations, not asserted.
var bareInsideCount = observations.Count(o => o.label.StartsWith("A:") && o.label.Contains("consumer") && o.ambientPresent);
var wrappedInsideCount = observations.Count(o => o.label.StartsWith("B:") && o.label.Contains("consumer") && o.ambientPresent);

Console.WriteLine($"Bare scenario A — consumer hooks observing ambient TS: {bareInsideCount}/3");
Console.WriteLine($"Wrapped scenario B — consumer hooks observing ambient TS: {wrappedInsideCount}/3");

if (bareInsideCount == 0 && wrappedInsideCount == 3)
{
    Console.WriteLine();
    Console.WriteLine("VERDICT: Confirms the spike's claim.");
    Console.WriteLine("  (a) PlaceOrderAsync's call shape produces no ambient TransactionScope.");
    Console.WriteLine("  (b) Wrapping PlaceOrderAsync in `using var ts = new TransactionScope(");
    Console.WriteLine("      TransactionScopeAsyncFlowOption.Enabled)` makes Transaction.Current");
    Console.WriteLine("      flow through the awaits and is observable from the consumer hooks");
    Console.WriteLine("      where the outbox row would be enrolled.");
    return 0;
}

Console.WriteLine();
Console.WriteLine("VERDICT: Unexpected. Review the observations table.");
return 1;
