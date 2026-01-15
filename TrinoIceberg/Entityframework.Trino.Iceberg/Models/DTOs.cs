namespace Entityframework.Trino.Iceberg;

/// <summary>
/// DTO for Customer + Order JOIN queries
/// </summary>
public class CustomerOrderDTO
{
    public string CustomerName { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// DTO for 3-table JOIN: Customer + Order + OrderItem
/// </summary>
public class CustomerOrderItemDTO
{
    public string CustomerName { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// DTO for aggregate queries
/// </summary>
public class OrderSummaryDTO
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalAmount { get; set; }
    public int OrderCount { get; set; }
}
