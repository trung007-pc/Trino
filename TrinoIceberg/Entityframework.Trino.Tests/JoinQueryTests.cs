using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;
using EntityFramework.Trino.Dapper;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Test cases cho JOIN queries (2 tables và 3 tables) với WhereIf
/// TẤT CẢ đều dùng TrinoSqlBuilder
/// </summary>
public class JoinQueryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;

    public JoinQueryTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
        TrinoTypeHandlers.Initialize();
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    [Fact]
    public async Task Join_TwoTables_Simple()
    {
        // Arrange - JOIN 2 tables không có filter, dùng DTO cụ thể
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                o.id as OrderId,
                o.amount as Amount,
                o.status as Status
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            LIMIT 10")
            .Build();

        // Act
        var results = await _context.QueryAsync<CustomerOrderDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify data integrity
        Assert.All(list, r =>
        {
            Assert.NotEmpty(r.CustomerName);
            Assert.NotEmpty(r.OrderId);
            Assert.True(r.Amount > 0, "Amount should be positive");
            Assert.NotEmpty(r.Status);
        });
        
        _output.WriteLine($"Found {list.Count} joined records");
        var first = list.First();
        _output.WriteLine($"  Customer: {first.CustomerName}, Order: {first.OrderId}, Amount: {first.Amount}, Status: {first.Status}");
    }

    [Fact]
    public async Task Join_TwoTables_WithWhereIf()
    {
        // Arrange - JOIN 2 tables với WhereIf filter
        var hasAmountFilter = true;
        var hasStatusFilter = false;

        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                o.id as OrderId,
                o.amount as Amount,
                o.status as Status
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            LIMIT 10")
            .WhereIf(hasAmountFilter, "o.amount >= @minAmount")
            .WhereIf(hasStatusFilter, "o.status = @status")
            .WithParams(new { minAmount = 50, status = "completed" })
            .Build();

        // Act
        var results = await _context.QueryAsync<CustomerOrderDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        
        // Verify filter condition is applied correctly
        Assert.All(list, r =>
        {
            Assert.True(r.Amount >= 50, $"Expected amount >= 50, got {r.Amount}");
            Assert.NotEmpty(r.CustomerName);
            Assert.NotEmpty(r.OrderId);
            Assert.NotEmpty(r.Status);
        });
        
        // Verify SQL contains the filter
        Assert.Contains("o.amount >= 50", sql);
        
        _output.WriteLine($"Found {list.Count} orders with amount >= 50");
        if (list.Any())
        {
            _output.WriteLine($"  Min amount: {list.Min(x => x.Amount)}, Max amount: {list.Max(x => x.Amount)}");
        }
    }

    [Fact]
    public async Task Join_ThreeTables_CustomerOrderItem()
    {
        // Arrange - JOIN 3 tables: customers + orders + order_items với DTO cụ thể
        var hasCustomerFilter = true;
        var hasAmountFilter = true;

        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                o.id as OrderId,
                oi.productname as ProductName,
                oi.quantity as Quantity,
                oi.price as Price
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            INNER JOIN order_items oi ON o.id = oi.orderid
            ORDER BY o.orderdate DESC
            LIMIT 20")
            .WhereIf(hasCustomerFilter, "c.name LIKE @customerPattern")
            .WhereIf(hasAmountFilter, "o.amount >= @minAmount")
            .WithParams(new
            {
                customerPattern = "%a%",
                minAmount = 100
            })
            .Build();

        // Act
        var results = await _context.QueryAsync<CustomerOrderItemDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        
        // Verify all fields have valid data
        Assert.All(list, r =>
        {
            Assert.NotEmpty(r.CustomerName);
            Assert.Contains("a", r.CustomerName.ToLower()); // hasCustomerFilter with pattern %a%
            Assert.NotEmpty(r.OrderId);
            Assert.NotEmpty(r.ProductName);
            Assert.True(r.Quantity > 0, "Quantity must be positive");
            Assert.True(r.Price > 0, "Price must be positive");
        });
        
        _output.WriteLine($"Found {list.Count} order items from 3-table JOIN");
        if (list.Any())
        {
            var first = list.First();
            _output.WriteLine($"  Customer: {first.CustomerName}, Product: {first.ProductName}, Qty: {first.Quantity}, Price: {first.Price}");
            _output.WriteLine($"  Total items value: {list.Sum(x => x.Price * x.Quantity):F2}");
        }
    }

    [Fact]
    public async Task Join_WithNavigationProperties()
    {
        // Arrange - JOIN trả về nested objects (Customer và Order riêng biệt)
        // Dùng multi-mapping của Dapper để map 2 entities
        // NOTE: Quan trọng - phải đặt columns theo đúng thứ tự và dùng splitOn chính xác
        // Customer fields trước, Order fields sau, và splitOn phải là column đầu tiên của Order
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.id,
                c.name,
                c.email,
                o.id,
                o.customerid,
                o.orderdate,
                o.amount,
                o.status
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            LIMIT 10")
            .Build();

        // Act - Dùng QueryMultiMapAsync để map Customer + Order
        // splitOn: "id" - GetSplitIndex sẽ tìm column "id" THỨ HAI trong result set (o.id)
        // để bắt đầu map entity thứ 2 (Order)
        // Columns [0-2] map vào Customer (id, name, email)
        // Columns [3-7] map vào Order (id, customerid, orderdate, amount, status)
        var results = await _context.QueryMultiMapAsync<Customer, Order, CustomerWithNavigationProperty>(
            sql,
            (customer, order) => new CustomerWithNavigationProperty
            {
                Customer = customer,
                Order = order
            },
            splitOn: "id" // Tìm occurrence thứ 2 của "id" (o.id tại index 3)
        );

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify nested objects structure and data integrity
        Assert.All(list, r =>
        {
            Assert.NotNull(r.Customer);
            Assert.NotNull(r.Order);
            Assert.NotEmpty(r.Customer.Id);
            Assert.NotEmpty(r.Customer.Name);
            Assert.NotEmpty(r.Customer.Email);
            Assert.NotEmpty(r.Order.Id);
            Assert.NotEmpty(r.Order.CustomerId);
            Assert.True(r.Order.Amount > 0);
            Assert.NotEmpty(r.Order.Status);
            
            // Verify JOIN integrity: Customer.Id should match Order.CustomerId
            Assert.Equal(r.Customer.Id, r.Order.CustomerId);
        });
        
        _output.WriteLine($"Found {list.Count} customers with navigation properties (multi-mapping)");
        var first = list.First();
        _output.WriteLine($"  Customer: {first.Customer.Name} ({first.Customer.Email})");
        _output.WriteLine($"  Order: {first.Order.Id}, Amount: {first.Order.Amount}, Status: {first.Order.Status}");
        _output.WriteLine($"  JOIN verified: Customer.Id == Order.CustomerId");
    }
    
     [Fact]
    public async Task Join_WithNavigationProperties2()
    {
        // Arrange - JOIN trả về nested objects với wrapper class cho customer name
        // NOTE: Dapper multi-mapping KHÔNG hỗ trợ primitive types như string, int, decimal
        // Phải dùng class wrapper cho tất cả các types trong multi-mapping
        var sql = new TrinoSqlBuilder(@"
            SELECT        
                c.name as Name,             
                o.id,
                o.customerid,
                o.orderdate,
                o.amount,
                o.status
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            LIMIT 10")
            .Build();

        // Act - Dùng QueryMultiMapAsync với wrapper class thay vì primitive string
        // CustomerNameWrapper chứa property Name để Dapper map được
        // splitOn: "id" - split tại o.id để bắt đầu map Order
        var results = await _context.QueryMultiMapAsync<CustomerNameWrapper, Order, CustomerNameAndOrder>(
            sql,
            (customerWrapper, order) => new CustomerNameAndOrder
            {
                CustomerName = customerWrapper.Name,
                Order = order
            },
            splitOn: "id"
        );

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify data integrity
        Assert.All(list, r =>
        {
            Assert.NotEmpty(r.CustomerName);
            Assert.NotNull(r.Order);
            Assert.NotEmpty(r.Order.Id);
            Assert.True(r.Order.Amount > 0);
        });
        
        _output.WriteLine($"Found {list.Count} customer-order records");
        var first = list.First();
        _output.WriteLine($"  Customer: {first.CustomerName}, Order: {first.Order.Id}, Amount: {first.Order.Amount}");
    }

    [Fact]
    public async Task Join_OrderItems_Only()
    {
        // Arrange - Query OrderItem entity trực tiếp (có thể có JOIN hoặc không)
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                oi.id as Id,
                oi.orderid as OrderId,
                oi.productname as ProductName,
                oi.quantity as Quantity,
                oi.price as Price
            FROM order_items oi
            INNER JOIN orders o ON oi.orderid = o.id
            LIMIT 10")
            .WhereIf(true, "oi.quantity > @minQty")
            .WithParams(new { minQty = 1 })
            .Build();

        // Act
        var results = await _context.QueryAsync<OrderItem>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify all fields and filter condition
        Assert.All(list, r =>
        {
            Assert.NotEmpty(r.Id);
            Assert.NotEmpty(r.OrderId);
            Assert.NotEmpty(r.ProductName);
            Assert.True(r.Quantity > 1, $"Expected quantity > 1, got {r.Quantity}");
            Assert.True(r.Price > 0, "Price must be positive");
        });
        
        _output.WriteLine($"Found {list.Count} order items with quantity > 1");
        var first = list.First();
        _output.WriteLine($"  Product: {first.ProductName}, Qty: {first.Quantity}, Price: {first.Price}");
        _output.WriteLine($"  Min qty: {list.Min(x => x.Quantity)}, Max qty: {list.Max(x => x.Quantity)}");
        _output.WriteLine($"  Total items: {list.Sum(x => x.Quantity)}");
    }

    [Fact]
    public async Task Join_ThreeTables_WithNavigationProperties()
    {
        // Arrange - JOIN 3 tables với multi-mapping
        // Customer -> Order -> OrderItem
        // Test case này verify GetSplitIndexes hoạt động đúng cho 3+ tables
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.id,
                c.name,
                c.email,
                o.id,
                o.customerid,
                o.orderdate,
                o.amount,
                o.status,
                oi.id,
                oi.orderid,
                oi.productname,
                oi.quantity,
                oi.price
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            INNER JOIN order_items oi ON o.id = oi.orderid
            LIMIT 5")
            .Build();

        // Act - Dùng QueryMultiMapAsync<T1,T2,T3> để map 3 entities
        // splitOn: "id" - GetSplitIndexes sẽ tìm TẤT CẢ "id" columns và skip occurrence đầu tiên
        // Columns [0-2] map vào Customer (id, name, email)
        // Columns [3-7] map vào Order (id, customerid, orderdate, amount, status)
        // Columns [8-12] map vào OrderItem (id, orderid, productname, quantity, price)
        var results = await _context.QueryMultiMapAsync<Customer, Order, OrderItem, CustomerOrderItemWrapper>(
            sql,
            (customer, order, orderItem) => new CustomerOrderItemWrapper
            {
                Customer = customer,
                Order = order,
                OrderItem = orderItem
            },
            splitOn: "id" // Sẽ tìm occurrence thứ 2 và 3 của "id" (indexes 3 và 8)
        );

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify 3-level nested objects structure
        Assert.All(list, r =>
        {
            // Customer validation
            Assert.NotNull(r.Customer);
            Assert.NotEmpty(r.Customer.Id);
            Assert.NotEmpty(r.Customer.Name);
            Assert.NotEmpty(r.Customer.Email);
            
            // Order validation
            Assert.NotNull(r.Order);
            Assert.NotEmpty(r.Order.Id);
            Assert.NotEmpty(r.Order.CustomerId);
            Assert.True(r.Order.Amount > 0, "Order amount should be positive");
            Assert.NotEmpty(r.Order.Status);
            
            // OrderItem validation
            Assert.NotNull(r.OrderItem);
            Assert.NotEmpty(r.OrderItem.Id);
            Assert.NotEmpty(r.OrderItem.OrderId);
            Assert.NotEmpty(r.OrderItem.ProductName);
            Assert.True(r.OrderItem.Quantity > 0, "Quantity should be positive");
            Assert.True(r.OrderItem.Price > 0, "Price should be positive");
            
            // Relationship validation
            Assert.Equal(r.Customer.Id, r.Order.CustomerId);
            Assert.Equal(r.Order.Id, r.OrderItem.OrderId);
        });
        
        _output.WriteLine($"Found {list.Count} customer->order->item records from 3-table JOIN");
        var first = list.First();
        _output.WriteLine($"  Customer: {first.Customer.Name} ({first.Customer.Email})");
        _output.WriteLine($"  Order: {first.Order.Id}, Amount: {first.Order.Amount}, Status: {first.Order.Status}");
        _output.WriteLine($"  Item: {first.OrderItem.ProductName}, Qty: {first.OrderItem.Quantity}, Price: {first.OrderItem.Price}");
    }

    [Fact]
    public async Task Join_WithInclude_CustomerWithOrdersList()
    {
        // Arrange - Pattern giống EF Core: context.Customers.Include(c => c.Orders)
        // Query JOIN trả về flat data, sau đó group để 1 customer có List<Order>
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.id,
                c.name,
                c.email,
                o.id,
                o.customerid,
                o.orderdate,
                o.amount,
                o.status
            FROM customers c
            LEFT JOIN orders o ON c.id = o.customerid
            ORDER BY c.id, o.orderdate")
            .Build();
        
        // Act - Query với multi-mapping rồi group results
        var flatResults = await _context.QueryMultiMapAsync<Customer, Order, CustomerWithNavigationProperty>(
            sql,
            (customer, order) => new CustomerWithNavigationProperty
            {
                Customer = customer,
                Order = order
            },
            splitOn: "id"
        );

        // Group by customer to create Include-like structure
        var customersWithOrders = flatResults
            .GroupBy(r => r.Customer.Id)
            .Select(g => new CustomerWithOrders
            {
                Customer = g.First().Customer,
                Orders = g.Where(r => r.Order != null && !string.IsNullOrEmpty(r.Order.Id))
                         .Select(r => r.Order)
                         .ToList()
            })
            .ToList();

        // Assert
        Assert.NotNull(customersWithOrders);
        Assert.NotEmpty(customersWithOrders);
        
        // Verify Include pattern: Each customer should have multiple orders
        Assert.All(customersWithOrders, c =>
        {
            Assert.NotNull(c.Customer);
            Assert.NotEmpty(c.Customer.Id);
            Assert.NotEmpty(c.Customer.Name);
            Assert.NotEmpty(c.Customer.Email);
            Assert.NotNull(c.Orders);

            if (c.Orders.Any())
            {
                // Verify all orders belong to this customer
                Assert.All(c.Orders, order =>
                {
                    Assert.Equal(c.Customer.Id, order.CustomerId);
                    Assert.NotEmpty(order.Id);
                    Assert.NotEmpty(order.Status);
                });
            }
        });
        
        _output.WriteLine($"Found {customersWithOrders.Count} customers with Include(c => c.Orders) pattern");
        foreach (var item in customersWithOrders)
        {
            _output.WriteLine($"  Customer: {item.Customer.Name} ({item.Customer.Email})");
            _output.WriteLine($"    Orders: {item.Orders.Count}");
            foreach (var order in item.Orders.Take(3))
            {
                _output.WriteLine($"      - Order {order.Id}: ${order.Amount}, Status: {order.Status}");
            }
            _output.WriteLine($"    Total Amount: ${item.Orders.Sum(o => o.Amount):F2}");
        }
    }

    [Fact]
    public async Task Join_WithDynamic_ComplexQuery()
    {
        // Arrange - Query với aggregation, dùng strongly-typed DTO để type handlers hoạt động
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                COUNT(o.id) as TotalOrders,
                SUM(o.amount) as TotalAmount,
                AVG(o.amount) as AvgAmount
            FROM customers c
            LEFT JOIN orders o ON c.id = o.customerid
            GROUP BY c.name
            HAVING COUNT(o.id) > @minOrders
            ORDER BY TotalAmount DESC
            LIMIT 10")
            .WithParams(new { minOrders = 1 })
            .Build();

        // Act - Dùng CustomerAggregateDto thay vì dynamic để type handlers tự động convert
        var results = await _context.QueryAsync<CustomerAggregateDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);
        
        // Verify aggregated data - type handlers đã convert TrinoBigDecimal sang decimal
        Assert.All(list, r =>
        {
            Assert.NotEmpty(r.CustomerName);
            Assert.True(r.TotalOrders > 1, "Should have more than 1 order due to HAVING clause");
            Assert.True(r.TotalAmount > 0, "Total amount must be positive");
            Assert.True(r.AvgAmount > 0, "Average amount must be positive");
            
            // Verify calculation: TotalAmount should equal TotalOrders * AvgAmount (roughly)
            decimal expectedTotal = r.TotalOrders * r.AvgAmount;
            Assert.True(Math.Abs(r.TotalAmount - expectedTotal) < 1, 
                $"TotalAmount ({r.TotalAmount}) should equal TotalOrders * AvgAmount ({expectedTotal})");
        });
        
        _output.WriteLine($"Found {list.Count} customers with aggregated data");
        var first = list.First();
        _output.WriteLine($"  Top Customer: {first.CustomerName}");
        _output.WriteLine($"    Orders: {first.TotalOrders}, Total: {first.TotalAmount:F2}, Avg: {first.AvgAmount:F2}");
        _output.WriteLine($"  Total revenue from top {list.Count} customers: {list.Sum(x => x.TotalAmount):F2}");
    }

    [Fact]
    public async Task Join_WithNavigationProperty()
    {
        // Arrange - JOIN trả về nested objects (Customer + Order)
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.id,
                c.name,
                c.email,
                o.id,
                o.customerid,
                o.orderdate,
                o.amount,
                o.status
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            LIMIT 5")
            .Build();

        // Act
        var results = await _context.QueryMultiMapAsync<Customer, Order, CustomerWithNavigationProperty>(
            sql,
            (customer, order) => new CustomerWithNavigationProperty
            {
                Customer = customer,
                Order = order
            });
        
        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.All(list, item =>
        {
            Assert.NotNull(item.Customer);
            Assert.NotNull(item.Order);
            Assert.NotEmpty(item.Customer.Name);
            Assert.NotEmpty(item.Order.Id);
        });
        
        _output.WriteLine($"Found {list.Count} CustomerWithNavigationProperty records");
        if (list.Any())
        {
            var first = list.First();
            _output.WriteLine($"  Customer: {first.Customer.Name}, Order: {first.Order.Id}, Amount: {first.Order.Amount}");
        }
    }

    [Fact]
    public async Task Join_ThreeTables_ReturnCustomerOrderItemDto()
    {
        // Arrange - JOIN 3 tables với DTO cụ thể
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                o.id as OrderId,
                oi.productname as ProductName,
                oi.price as Price,
                oi.quantity as Quantity
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            INNER JOIN order_items oi ON o.id = oi.orderid
            LIMIT 10")
            .Build();

        // Act
        var results = await _context.QueryAsync<CustomerOrderItemDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.All(list, item =>
        {
            Assert.NotEmpty(item.CustomerName);
            Assert.NotEmpty(item.OrderId);
            Assert.NotEmpty(item.ProductName);
        });
        
        _output.WriteLine($"Found {list.Count} CustomerOrderItemDto records");
    }
    

    [Fact]
    public async Task Join_TwoTables_ReturnOrderItemsOnly()
    {
        // Arrange - Query chỉ lấy order items với filter
        var hasQuantityFilter = true;
        
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                oi.id as Id,
                oi.orderid as OrderId,
                oi.productname as ProductName,
                oi.quantity as Quantity,
                oi.price as Price
            FROM order_items oi
            INNER JOIN orders o ON oi.orderid = o.id
            ORDER BY oi.quantity DESC
            LIMIT 15")
            .WhereIf(hasQuantityFilter, "oi.quantity > @minQty")
            .WithParams(new { minQty = 1 })
            .Build();

        // Act
        var results = await _context.QueryAsync<OrderItem>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.All(list, item => Assert.True(item.Quantity > 1));
        
        _output.WriteLine($"Found {list.Count} OrderItem records with quantity > 1");
    }

    [Fact]
    public async Task Join_WithLeftJoin_ReturnCustomerOrderDto()
    {
        // Arrange - LEFT JOIN để lấy cả customers không có orders
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                o.id as OrderId,
                COALESCE(o.amount, 0) as Amount,
                COALESCE(o.status, 'no-order') as Status
            FROM customers c
            LEFT JOIN orders o ON c.id = o.customerid
            ORDER BY c.name
            LIMIT 20")
            .Build();

        // Act
        var results = await _context.QueryAsync<CustomerOrderDto>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        
        // Có thể có customers không có orders (OrderId rỗng)
        _output.WriteLine($"Found {list.Count} customer-order pairs (including customers without orders)");
        var withoutOrders = list.Count(x => string.IsNullOrEmpty(x.OrderId));
        _output.WriteLine($"  {withoutOrders} customers without orders");
    }

    [Fact]
    public async Task Join_WithDynamic_ForAggregations()
    {
        // Arrange - Chỉ dùng dynamic cho aggregation queries phức tạp
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.name as CustomerName,
                COUNT(o.id) as TotalOrders,
                SUM(o.amount) as TotalAmount,
                AVG(o.amount) as AvgAmount
            FROM customers c
            LEFT JOIN orders o ON c.id = o.customerid
            GROUP BY c.name
            ORDER BY TotalAmount DESC
            LIMIT 10")
            .Build();

        // Act
        var results = await _context.QueryAsync<dynamic>(sql);

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        
        _output.WriteLine($"Found {list.Count} customer aggregations (dynamic)");
        if (list.Any())
        {
            var first = list.First();
            _output.WriteLine($"  Customer: {first.CustomerName}, Orders: {first.TotalOrders}, Total: {first.TotalAmount}");
        }
    }

    [Fact]
    public async Task Join_ThreeTables_WithMultiMapping()
    {
        // Arrange - JOIN 3 tables với multi-mapping: Customer + Order + OrderItem
        var sql = new TrinoSqlBuilder(@"
            SELECT 
                c.*,
                o.*,
                oi.*
            FROM customers c
            INNER JOIN orders o ON c.id = o.customerid
            INNER JOIN order_items oi ON o.id = oi.orderid
            LIMIT 10")
            .Build();

        // Act - Dùng QueryMultiMapAsync với 3 entities
        var results = await _context.QueryMultiMapAsync<Customer, Order, OrderItem, CustomerOrderItemWrapper>(
            sql,
            (customer, order, orderItem) => new CustomerOrderItemWrapper
            {
                Customer = customer,
                Order = order,
                OrderItem = orderItem
            }
        );

        // Assert
        Assert.NotNull(results);
        var list = results.ToList();
        Assert.NotEmpty(list);

        // Verify all 3 entities are populated correctly
        Assert.All(list, r =>
        {
            // Verify Customer
            Assert.NotNull(r.Customer);
            Assert.NotEmpty(r.Customer.Id);
            Assert.NotEmpty(r.Customer.Name);
            Assert.NotEmpty(r.Customer.Email);

            // Verify Order
            Assert.NotNull(r.Order);
            Assert.NotEmpty(r.Order.Id);
            Assert.NotEmpty(r.Order.CustomerId);
            Assert.True(r.Order.Amount > 0);

            // Verify OrderItem
            Assert.NotNull(r.OrderItem);
            Assert.NotEmpty(r.OrderItem.Id);
            Assert.NotEmpty(r.OrderItem.OrderId);
            Assert.NotEmpty(r.OrderItem.ProductName);
            Assert.True(r.OrderItem.Quantity > 0);
            Assert.True(r.OrderItem.Price > 0);

            // Verify JOIN relationships
            Assert.Equal(r.Customer.Id, r.Order.CustomerId);
            Assert.Equal(r.Order.Id, r.OrderItem.OrderId);
        });

        _output.WriteLine($"Found {list.Count} records from 3-table multi-mapping");
        var first = list.First();
        _output.WriteLine($"  Customer: {first.Customer.Name}");
        _output.WriteLine($"  Order: {first.Order.Id}, Amount: {first.Order.Amount}");
        _output.WriteLine($"  Item: {first.OrderItem.ProductName}, Qty: {first.OrderItem.Quantity}, Price: {first.OrderItem.Price}");
        _output.WriteLine($"  JOIN integrity verified: Customer.Id -> Order.CustomerId -> OrderItem.OrderId");
    }
}

/// <summary>
/// Wrapper class để chứa 3 entities từ 3-table JOIN
/// </summary>
public class CustomerOrderItemWrapper
{
    public Customer Customer { get; set; } = null!;
    public Order Order { get; set; } = null!;
    public OrderItem OrderItem { get; set; } = null!;
}
