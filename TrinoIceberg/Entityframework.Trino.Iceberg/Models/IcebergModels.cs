using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFramework.Trino.Iceberg.Models;


/// <summary>
/// Customer entity - mapped to 'customers' table in Iceberg
/// </summary>
public class CreateUpdateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}



/// <summary>
/// Customer entity - mapped to 'customers' table in Iceberg
/// </summary>
public class Customer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// EmailDomain entity - for aggregate queries
/// </summary>
public class EmailDomain
{
    [Column("domain")]
    public string Domain { get; set; } = string.Empty;

    [Column("customer_count")]
    public long CustomerCount { get; set; }
}

/// <summary>
/// Order entity - for testing JOIN operations
/// </summary>
public class Order
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// DTO for Customer + Order JOIN queries
/// </summary>
public class CustomerOrderDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// DTO for 3-table JOIN: Customer + Order + OrderItem
/// </summary>
public class CustomerOrderItemDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// OrderItem entity - for testing 3-table JOIN operations
/// </summary>
public class OrderItem
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class CustomerWithNavigationProperty
{
    public Customer Customer { get; set; }
    public Order Order { get; set; }
}

public class CustomerNameAndOrder
{
    public string CustomerName { get; set; }
    public Order Order { get; set; }
}

public class CustomerWrapper
{
    public Customer Customer { get; set; }
}

public class CustomerNameEmail
{
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
}

public class BasicCustomer
{
    public string Name { get; set; }
    public string Email { get; set; }
    public Decimal TotalAmount { get; set; }
    public string Status{get;set;}
}