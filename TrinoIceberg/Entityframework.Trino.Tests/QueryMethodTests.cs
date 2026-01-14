using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Tests cho Query() method - Type được infer từ Select()
/// </summary>
public class QueryMethodTests : IDisposable
{
    private readonly IcebergDbContext _context;
    private readonly List<string> _testCustomerIds = new();
    private readonly List<string> _testOrderIds = new();

    public QueryMethodTests()
    {
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
    }

    [Fact]
    public async Task Query_WithTypeInference_FullObjectMapping()
    {
        // Arrange
        var customerId = "CUST_QUERY_" + Guid.NewGuid().ToString("N");
        var orderId = "ORD_QUERY_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Query Test Customer",
            Email = "query@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 999.00m,
            Status = "Processing"
        });

        // Act - Query() với type inference từ Select()
        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c1, Order o1) => new CustomerWithNavigationProperty
            {
                Customer = c1,
                Order = o1
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.NotNull(result.Customer);
        Assert.NotNull(result.Order);
        Assert.Equal(customerId, result.Customer.Id);
        Assert.Equal("Query Test Customer", result.Customer.Name);
        Assert.Equal(orderId, result.Order.Id);
    }

    [Fact]
    public async Task Query_WithTypeInference_PropertyProjection()
    {
        // Arrange
        var customerId = "CUST_PROJ2_" + Guid.NewGuid().ToString("N");
        var orderId = "ORD_PROJ2_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Projection Test",
            Email = "proj@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 500.00m,
            Status = "Shipped"
        });

        // Act - Query() với property projection
        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c, Order o) => new BasicCustomer
            {
                Name = c.Name,
                Email = c.Email,
                TotalAmount = o.Amount,
                Status = o.Status
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.Equal("Projection Test", result.Name);
        Assert.Equal("proj@test.com", result.Email);
        Assert.Equal(500.00m, result.TotalAmount);
        Assert.Equal("Shipped", result.Status);
    }

    /// <summary>
    /// TEST: SELECT * cho 1 table
    /// Cơ chế: ParameterExpression (Customer c) → SELECT c.*
    /// SQL: SELECT * FROM customers WHERE ...
    /// </summary>
    [Fact]
    public async Task Query_SelectAllColumns_SingleTable()
    {
        // Arrange
        var customerId = "CUST_SELECTALL_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Select All Test",
            Email = "selectall@test.com"
        });

        // Act - ParameterExpression: new Result { Customer = c }
        // Code nhận diện c là ParameterExpression → gọi AddAllColumnsForType → SELECT c.*
        var results = await _context.Query("FROM customers c WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c) => new CustomerWrapper { Customer = c })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.NotNull(result.Customer);
        Assert.Equal(customerId, result.Customer.Id);
        Assert.Equal("Select All Test", result.Customer.Name);
        Assert.Equal("selectall@test.com", result.Customer.Email);
    }

    /// <summary>
    /// TEST: SELECT specific columns từ 1 table
    /// Cơ chế: MemberExpression (c.Name, c.Email) → SELECT c.Name, c.Email
    /// SQL: SELECT c.Name, c.Email FROM customers WHERE ...
    /// </summary>
    [Fact]
    public async Task Query_SelectSpecificColumns_SingleTable()
    {
        // Arrange
        var customerId = "CUST_SPECIFIC_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Specific Columns Test",
            Email = "specific@test.com"
        });

        // Act - MemberInitExpression: new { Name = c.Name, Email = c.Email }
        // Code parse từng binding → extract c.Name, c.Email → SELECT c.Name, c.Email
        var results = await _context.Query("FROM customers c WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c) => new CustomerNameEmail
            {
                CustomerName = c.Name,
                CustomerEmail = c.Email
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.Equal("Specific Columns Test", result.CustomerName);
        Assert.Equal("specific@test.com", result.CustomerEmail);
        // Note: Id KHÔNG được select vì không có trong projection
    }

    /// <summary>
    /// TEST: SELECT * cho 2 tables
    /// Cơ chế: 2 ParameterExpression (Customer c, Order o) → SELECT c.*, o.*
    /// SQL: SELECT * FROM customers c JOIN orders o ...
    /// </summary>
    [Fact]
    public async Task Query_SelectAllColumns_TwoTables()
    {
        // Arrange
        var customerId = "CUST_2TABLES_" + Guid.NewGuid().ToString("N");
        var orderId = "ORD_2TABLES_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Two Tables Test",
            Email = "twotables@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 750.00m,
            Status = "Pending"
        });

        // Act - 2 ParameterExpression: Customer = c, Order = o
        // Code detect 2 parameters → SELECT c.*, o.* (tất cả columns từ cả 2 tables)
        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c, Order o) => new CustomerWithNavigationProperty
            {
                Customer = c,
                Order = o
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.NotNull(result.Customer);
        Assert.NotNull(result.Order);
        Assert.Equal("Two Tables Test", result.Customer.Name);
        Assert.Equal(750.00m, result.Order.Amount);
    }

    /// <summary>
    /// TEST: SELECT mixed - 1 column từ table 1 + all columns từ table 2
    /// Cơ chế: MemberExpression (c.Name) + ParameterExpression (o) → SELECT c.Name, o.*
    /// SQL: SELECT c.Name, * FROM customers c JOIN orders o ...
    /// </summary>
    [Fact]
    public async Task Query_SelectMixed_SpecificAndAll()
    {
        // Arrange
        var customerId = "CUST_MIXED_" + Guid.NewGuid().ToString("N");
        var orderId = "ORD_MIXED_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Mixed Select Test",
            Email = "mixed@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 888.00m,
            Status = "Completed"
        });

        // Act - Mixed: c.Name (MemberExpression) + o (ParameterExpression)
        // Code sẽ generate: SELECT c.Name, o.*
        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c, Order o) => new CustomerNameAndOrder
            {
                CustomerName = c.Name,  // Chỉ select 1 column
                Order = o               // Select tất cả columns
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.Equal("Mixed Select Test", result.CustomerName);
        Assert.NotNull(result.Order);
        Assert.Equal(888.00m, result.Order.Amount);
        Assert.Equal("Completed", result.Order.Status);
    }

    /// <summary>
    /// TEST: SELECT với property aliasing (TotalAmount = o.Amount)
    /// Cơ chế: Target property name != Source property name → SELECT ... AS ...
    /// SQL: SELECT c.Name, o.Amount AS TotalAmount FROM ...
    /// </summary>
    [Fact]
    public async Task Query_SelectWithAliasing()
    {
        // Arrange
        var customerId = "CUST_ALIAS_" + Guid.NewGuid().ToString("N");
        var orderId = "ORD_ALIAS_" + Guid.NewGuid().ToString("N");
        _testCustomerIds.Add(customerId);
        _testOrderIds.Add(orderId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Alias Test",
            Email = "alias@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.Now,
            Amount = 555.00m,
            Status = "Shipped"
        });

        // Act - Property name khác source property: TotalAmount = o.Amount
        // Code detect mismatch → generate: SELECT o.Amount AS TotalAmount
        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId WHERE c.Id = @customerId")
            .WithParams(new { customerId = customerId })
            .Select((Customer c, Order o) => new BasicCustomer
            {
                Name = c.Name,          // Name = c.Name (no alias)
                Email = c.Email,        // Email = c.Email (no alias)
                TotalAmount = o.Amount, // TotalAmount != Amount → AS TotalAmount
                Status = o.Status       // Status = o.Status (no alias)
            })
            .ExecuteAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results[0];
        Assert.Equal("Alias Test", result.Name);
        Assert.Equal(555.00m, result.TotalAmount); // Mapped từ o.Amount
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

        _context.Dispose();
    }

    /// <summary>
    /// TEST: Sử dụng WhereIf() trong FluentQueryBuilder pattern
    /// </summary>
    [Fact]
    public async Task Query_WithWhereIf_ShouldFilterDynamically()
    {
        // Arrange
        var customerId1 = "CUST_WHEREIF1_" + Guid.NewGuid().ToString("N");
        var customerId2 = "CUST_WHEREIF2_" + Guid.NewGuid().ToString("N");
        var orderId1 = "ORD_WHEREIF1_" + Guid.NewGuid().ToString("N");
        var orderId2 = "ORD_WHEREIF2_" + Guid.NewGuid().ToString("N");
        
        _testCustomerIds.Add(customerId1);
        _testCustomerIds.Add(customerId2);
        _testOrderIds.Add(orderId1);
        _testOrderIds.Add(orderId2);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId1,
            Name = "Active Customer",
            Email = "active@test.com"
        });

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId2,
            Name = "Inactive Customer",
            Email = "inactive@test.com"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId1,
            CustomerId = customerId1,
            OrderDate = DateTime.Now,
            Amount = 150.00m,
            Status = "Completed"
        });

        await _context.Orders.AddAsync(new Order
        {
            Id = orderId2,
            CustomerId = customerId2,
            OrderDate = DateTime.Now,
            Amount = 50.00m,
            Status = "Pending"
        });

        // Act - Dynamic filtering với WhereIf()
        string? nameFilter = "Active";
        decimal? minAmount = 100m;
        bool applyNameFilter = true;
        bool applyAmountFilter = true;

        var results = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId")
            .WhereIf(applyNameFilter && !string.IsNullOrEmpty(nameFilter), "c.Name LIKE @namePattern")
            //.WhereIf(applyAmountFilter && minAmount.HasValue, "o.Amount >= @minAmount")
            .WithParams(new { namePattern = $"%{nameFilter}%", minAmount })
            .Select((Customer c, Order o) => new CustomerNameAndOrder
            {
                CustomerName = c.Name,
                Order = o
            })
            .ExecuteAsync();

        // Assert - Chỉ trả về Active customer với order >= 100
        Assert.Single(results);
        Assert.Equal("Active Customer", results[0].CustomerName);
        Assert.Equal(150.00m, results[0].Order.Amount);
    }
}

