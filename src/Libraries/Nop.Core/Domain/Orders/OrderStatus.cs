namespace Nop.Core.Domain.Orders;

/// <summary>
/// Represents an order status enumeration
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Pending
    /// </summary>
    Pending = 10,

    /// <summary>
    /// Processing
    /// </summary>
    Processing = 20,

    /// <summary>
    /// Complete
    /// </summary>
    Complete = 30,

    /// <summary>
    /// Cancelled
    /// </summary>
    Cancelled = 40,

    /// <summary>
    /// Compensated — order was accepted optimistically but the warehouse rejected the
    /// reservation (or another downstream dependency failed); payment has been released
    /// and the customer has been notified. Terminal state, distinct from Cancelled which
    /// is operator-initiated.
    /// </summary>
    Compensated = 50
}