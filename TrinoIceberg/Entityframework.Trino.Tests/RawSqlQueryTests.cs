using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;
using Trino.Data.ADO.Server;
using Entityframework.Trino.Iceberg;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Tests for Raw SQL queries with parameters - Simple and safe approach
/// </summary>
public class RawSqlQueryTests : IDisposable
{
    private readonly IcebergDbContext _context;
    private readonly List<string> _testCustomerIds = new();
    private readonly List<string> _testOrderIds = new();
    private readonly List<string> _testOrderItemIds = new();

    public RawSqlQueryTests()
    {
        var connectionProperties = new TrinoConnectionProperties
        {
            Host = "localhost",
            Port = 8081,
            Catalog = "iceberg",
            Schema = "v4",
            User = "trino",
            EnableSsl = false
            // EnableSqlLogging = true // Không có trong official library
        };

        _context = new IcebergDbContext(connectionProperties);
    }

    [Fact]
    public async Task RawSql_TwoTableJoin_ShouldReturnResults()
    {
        // Arrange
        var customerId = $"CUST_RAW_{Guid.NewGuid():N}";
        var orderId = $"ORD_RAW_{Guid.NewGuid():N}";
        
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Raw SQL Customer",
            Email = "rawsql@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 250.00m,
            Status = "Completed"
        });

        // Act - Use raw SQL with parameters
        var results = await _context.ExecuteQueryAsync<CustomerOrderDTO>(
            @"SELECT c.Name AS CustomerName, o.Id AS OrderId, o.Amount, o.Status
              FROM customers c
              INNER JOIN orders o ON c.Id = o.CustomerId
              WHERE c.Name = @customerName",
            new { customerName = "Raw SQL Customer" }
        );

        // Assert
        Assert.NotEmpty(results);
        var result = results.First();
        Assert.Equal("Raw SQL Customer", result.CustomerName);
        Assert.Equal(250.00m, result.Amount);
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task RawSql_ThreeTableJoin_ShouldReturnResults()
    {
        // Arrange
        var customerId = $"CUST_RAW3_{Guid.NewGuid():N}";
        var orderId = $"ORD_RAW3_{Guid.NewGuid():N}";
        var itemId1 = $"ITEM_RAW3_1_{Guid.NewGuid():N}";
        var itemId2 = $"ITEM_RAW3_2_{Guid.NewGuid():N}";
        
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);
        _testOrderItemIds.Add(itemId1);
        _testOrderItemIds.Add(itemId2);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "3-Table Customer",
            Email = "3table@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 500.00m,
            Status = "Shipped"
        });

        await _context.OrderItems.AddAsync(new OrderItem
        {
            Id = itemId1,
            OrderId = orderId,
            ProductName = "Laptop",
            Quantity = 1,
            Price = 300.00m
        });

        await _context.OrderItems.AddAsync(new OrderItem
        {
            Id = itemId2,
            OrderId = orderId,
            ProductName = "Mouse",
            Quantity = 2,
            Price = 100.00m
        });

        // Act - 3-table JOIN with parameters
        var results = await _context.ExecuteQueryAsync<CustomerOrderItemDTO>(
            @"SELECT c.Name AS CustomerName, o.Id AS OrderId, 
                     oi.ProductName, oi.Price, oi.Quantity
              FROM customers c
              INNER JOIN orders o ON c.Id = o.CustomerId
              INNER JOIN order_items oi ON o.Id = oi.OrderId
              WHERE c.Name = @name AND o.Amount >= @minAmount
              ORDER BY oi.ProductName",
            new { name = "3-Table Customer", minAmount = 100 }
        );

        // Assert
        Assert.All(results, r => Assert.Equal("3-Table Customer", r.CustomerName));
        Assert.Contains(results, r => r.ProductName == "Laptop" && r.Price == 300.00m);
        Assert.Contains(results, r => r.ProductName == "Mouse" && r.Price == 100.00m);
    }

    [Fact]
    public async Task RawSql_WithDateParameter_ShouldWork()
    {
        // Arrange
        var customerId = $"CUST_DATE_{Guid.NewGuid():N}";
        var orderId = $"ORD_DATE_{Guid.NewGuid():N}";
        
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        var orderDate = new DateTime(2025, 6, 15, 10, 30, 0);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Date Test Customer",
            Email = "date@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = orderDate,
            Amount = 150.00m,
            Status = "Pending"
        });
        
        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = orderDate,
            Amount = 160.00m,
            Status = "Pending"
        });

        // Act - Query with date parameter
        var results = await _context.ExecuteQueryAsync<CustomerOrderDTO>(
            @"SELECT c.Name AS CustomerName, o.Id AS OrderId, o.Amount, o.Status
              FROM customers c
              INNER JOIN orders o ON c.Id = o.CustomerId
              WHERE o.OrderDate >= @startDate AND o.OrderDate <= @endDate",
            new 
            { 
                startDate = new DateTime(2025, 1, 1),
                endDate = new DateTime(2025, 12, 31)
            }
        );

        // Assert
        Assert.NotEmpty(results);
        var result = results.FirstOrDefault(r => r.OrderId == orderId);
        Assert.NotNull(result);
        Assert.Equal("Date Test Customer", result.CustomerName);
    }

    [Fact]
    public async Task RawSql_FirstOrDefault_ShouldReturnSingleResult()
    {
        // Arrange
        var customerId = $"CUST_SINGLE_{Guid.NewGuid():N}";
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Single Customer",
            Email = "single@test.com"
        });

        // Act
        var result = await _context.ExecuteQueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM customers WHERE Name = @name",
            new { name = "Single Customer" }
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Single Customer", result.Name);
    }

    [Fact]
    public async Task RawSql_SqlInjection_ShouldBeSafe()
    {
        // Arrange - Try SQL injection attack
        var maliciousInput = "'; DROP TABLE customers; --";

        // Act - This should be safe (parameters are escaped)
        var results = await _context.ExecuteQueryAsync<Customer>(
            "SELECT * FROM customers WHERE Name = @name",
            new { name = maliciousInput }
        );

        // Assert - Should return no results (not execute DROP TABLE)
        Assert.Empty(results);
        
        // Verify customers table still exists
        var count = await _context.Customers.CountAsync();
        Assert.True(count >= 0); // Table still exists
    }

    public void Dispose()
    {
        // Cleanup test data
        foreach (var orderId in _testOrderIds)
        {
            try
            {
                _context.Orders.Where(o => o.Id == orderId).DeleteAsync().GetAwaiter().GetResult();
            }
            catch { }
        }

        foreach (var customerId in _testCustomerIds)
        {
            try
            {
                _context.Customers.Where(c => c.Id == customerId).DeleteAsync().GetAwaiter().GetResult();
            }
            catch { }
        }

        foreach (var itemId in _testOrderItemIds)
        {
            try
            {
                _context.OrderItems.Where(i => i.Id == itemId).DeleteAsync().GetAwaiter().GetResult();
            }
            catch { }
        }

        _context.Dispose();
    }
}
