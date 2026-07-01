namespace FlowLine.Application.Orders;

/// <summary>Thrown for order-management operations that are invalid given current state.</summary>
public class OrderServiceException(string message) : Exception(message);
