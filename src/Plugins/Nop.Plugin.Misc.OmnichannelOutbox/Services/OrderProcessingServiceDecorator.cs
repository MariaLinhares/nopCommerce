using System.Transactions;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Decorator over IOrderProcessingService that wraps PlaceOrderAsync in a TransactionScope
/// and activates the outbox scope so that whitelisted events are written to the outbox table
/// atomically with the order.
/// All other methods delegate directly to the inner service.
/// </summary>
public class OrderProcessingServiceDecorator : IOrderProcessingService
{
    private readonly IOrderProcessingService _inner;

    public OrderProcessingServiceDecorator(IOrderProcessingService inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Wraps the inner PlaceOrderAsync in a TransactionScope so that both the order insert
    /// and the outbox row insert are atomic. Sets the AsyncLocal outbox scope flag so that
    /// the OutboxEventPublisherDecorator routes whitelisted events to the outbox table.
    /// </summary>
    public async Task<PlaceOrderResult> PlaceOrderAsync(ProcessPaymentRequest processPaymentRequest)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        OutboxEventPublisherDecorator.OutboxScope.Value = true;
        try
        {
            var result = await _inner.PlaceOrderAsync(processPaymentRequest);

            if (result.Errors?.Any() != true)
                scope.Complete();

            return result;
        }
        finally
        {
            OutboxEventPublisherDecorator.OutboxScope.Value = false;
        }
    }

    // --- All remaining methods delegate directly to _inner ---

    public Task CheckOrderStatusAsync(Order order)
        => _inner.CheckOrderStatusAsync(order);

    public Task UpdateOrderTotalsAsync(UpdateOrderParameters updateOrderParameters)
        => _inner.UpdateOrderTotalsAsync(updateOrderParameters);

    public Task DeleteOrderAsync(Order order)
        => _inner.DeleteOrderAsync(order);

    public Task<IEnumerable<string>> ProcessNextRecurringPaymentAsync(RecurringPayment recurringPayment, ProcessPaymentResult paymentResult = null)
        => _inner.ProcessNextRecurringPaymentAsync(recurringPayment, paymentResult);

    public Task<IList<string>> CancelRecurringPaymentAsync(RecurringPayment recurringPayment)
        => _inner.CancelRecurringPaymentAsync(recurringPayment);

    public Task<bool> CanCancelRecurringPaymentAsync(Customer customerToValidate, RecurringPayment recurringPayment)
        => _inner.CanCancelRecurringPaymentAsync(customerToValidate, recurringPayment);

    public Task<bool> CanRetryLastRecurringPaymentAsync(Customer customer, RecurringPayment recurringPayment)
        => _inner.CanRetryLastRecurringPaymentAsync(customer, recurringPayment);

    public Task ShipAsync(Shipment shipment, bool notifyCustomer)
        => _inner.ShipAsync(shipment, notifyCustomer);

    public Task ReadyForPickupAsync(Shipment shipment, bool notifyCustomer)
        => _inner.ReadyForPickupAsync(shipment, notifyCustomer);

    public Task DeliverAsync(Shipment shipment, bool notifyCustomer)
        => _inner.DeliverAsync(shipment, notifyCustomer);

    public bool CanCancelOrder(Order order)
        => _inner.CanCancelOrder(order);

    public Task CancelOrderAsync(Order order, bool notifyCustomer)
        => _inner.CancelOrderAsync(order, notifyCustomer);

    public Task CompensateOrderAsync(Order order, Guid sourceEventId, string reason)
        => _inner.CompensateOrderAsync(order, sourceEventId, reason);

    public bool CanMarkOrderAsAuthorized(Order order)
        => _inner.CanMarkOrderAsAuthorized(order);

    public Task MarkAsAuthorizedAsync(Order order)
        => _inner.MarkAsAuthorizedAsync(order);

    public Task<bool> CanCaptureAsync(Order order)
        => _inner.CanCaptureAsync(order);

    public Task<IList<string>> CaptureAsync(Order order)
        => _inner.CaptureAsync(order);

    public bool CanMarkOrderAsPaid(Order order)
        => _inner.CanMarkOrderAsPaid(order);

    public Task MarkOrderAsPaidAsync(Order order)
        => _inner.MarkOrderAsPaidAsync(order);

    public Task<bool> CanRefundAsync(Order order)
        => _inner.CanRefundAsync(order);

    public Task<IList<string>> RefundAsync(Order order)
        => _inner.RefundAsync(order);

    public bool CanRefundOffline(Order order)
        => _inner.CanRefundOffline(order);

    public Task RefundOfflineAsync(Order order)
        => _inner.RefundOfflineAsync(order);

    public Task<bool> CanPartiallyRefundAsync(Order order, decimal amountToRefund)
        => _inner.CanPartiallyRefundAsync(order, amountToRefund);

    public Task<IList<string>> PartiallyRefundAsync(Order order, decimal amountToRefund)
        => _inner.PartiallyRefundAsync(order, amountToRefund);

    public bool CanPartiallyRefundOffline(Order order, decimal amountToRefund)
        => _inner.CanPartiallyRefundOffline(order, amountToRefund);

    public Task PartiallyRefundOfflineAsync(Order order, decimal amountToRefund)
        => _inner.PartiallyRefundOfflineAsync(order, amountToRefund);

    public Task<bool> CanVoidAsync(Order order)
        => _inner.CanVoidAsync(order);

    public Task<IList<string>> VoidAsync(Order order)
        => _inner.VoidAsync(order);

    public bool CanVoidOffline(Order order)
        => _inner.CanVoidOffline(order);

    public Task VoidOfflineAsync(Order order)
        => _inner.VoidOfflineAsync(order);

    public Task<IList<string>> ReOrderAsync(Order order)
        => _inner.ReOrderAsync(order);

    public Task<bool> IsReturnRequestAllowedAsync(Order order)
        => _inner.IsReturnRequestAllowedAsync(order);

    public Task<bool> ValidateMinOrderSubtotalAmountAsync(IList<ShoppingCartItem> cart)
        => _inner.ValidateMinOrderSubtotalAmountAsync(cart);

    public Task<bool> ValidateMinOrderTotalAmountAsync(IList<ShoppingCartItem> cart)
        => _inner.ValidateMinOrderTotalAmountAsync(cart);

    public Task<bool> IsPaymentWorkflowRequiredAsync(IList<ShoppingCartItem> cart, bool? useRewardPoints = null)
        => _inner.IsPaymentWorkflowRequiredAsync(cart, useRewardPoints);

    public Task<DateTime?> GetNextPaymentDateAsync(RecurringPayment recurringPayment)
        => _inner.GetNextPaymentDateAsync(recurringPayment);

    public Task<int> GetCyclesRemainingAsync(RecurringPayment recurringPayment)
        => _inner.GetCyclesRemainingAsync(recurringPayment);

    public Task<ProcessPaymentRequest> GetProcessPaymentRequestAsync()
        => _inner.GetProcessPaymentRequestAsync();

    public Task SetProcessPaymentRequestAsync(ProcessPaymentRequest processPaymentRequest, bool useNewOrderGuid = false)
        => _inner.SetProcessPaymentRequestAsync(processPaymentRequest, useNewOrderGuid);
}
