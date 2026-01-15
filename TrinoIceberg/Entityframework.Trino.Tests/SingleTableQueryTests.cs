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
/// Test cases cho query 1 table đơn giản với filters
/// TẤT CẢ đều dùng TrinoSqlBuilder
/// </summary>
public class SingleTableQueryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;
    private readonly List<string> _insertedCustomerIds = new();

    public SingleTableQueryTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
        TrinoTypeHandlers.Initialize();
    }

    public void Dispose()
    {
        // Cleanup - xóa test data đã insert
        try
        {
            foreach (var id in _insertedCustomerIds)
            {
                var deleteSql = new TrinoSqlBuilder("DELETE FROM customers WHERE id = @id")
                    .WithParams(new { id })
                    .Build();
                _context.ExecuteAsync(deleteSql).Wait();
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Cleanup warning: {ex.Message}");
        }
        finally
        {
            _context?.Dispose();
        }
    }

    /// <summary>
    /// Helper method để insert test customers
    /// </summary>
    private async Task InsertTestCustomersAsync()
    {
        var testCustomers = new[]
        {
            new { Id = "test-001", Name = "John Smith", Email = "john.smith@gmail.com" },
            new { Id = "test-002", Name = "Alice Johnson", Email = "alice.j@example.com" },
            new { Id = "test-003", Name = "Bob Johnson", Email = "bob.johnson@gmail.com" },
            new { Id = "test-004", Name = "Charlie Brown", Email = "charlie@hotmail.com" },
            new { Id = "test-005", Name = "David Wilson-Martinez", Email = "david.w@example.com" }
        };

        foreach (var customer in testCustomers)
        {
            var insertSql = new TrinoSqlBuilder("INSERT INTO customers (id, name, email) VALUES (@id, @name, @email)")
                .WithParams(customer)
                .Build();
            
            await _context.ExecuteAsync(insertSql);
            _insertedCustomerIds.Add(customer.Id);
        }

        _output.WriteLine($"Inserted {testCustomers.Length} test customers");
    }

    [Fact]
    public async Task Query_AllCustomers_NoFilter()
    {
        // Arrange - Dùng SqlBuilder không có WHERE
        var sql = new TrinoSqlBuilder("SELECT * FROM customers LIMIT 10")
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        Assert.NotNull(customers);
        var list = customers.ToList();
        Assert.NotEmpty(list);
        
        _output.WriteLine($"Found {list.Count} customers");
    }

    [Fact]
    public async Task Query_CountCustomers()
    {
        // Arrange - SqlBuilder cho COUNT query
        var sql = new TrinoSqlBuilder("SELECT COUNT(*) FROM customers")
            .Build();

        // Act
        var count = await _context.ExecuteScalarAsync<long>(sql);

        // Assert
        Assert.True(count > 0);
        _output.WriteLine($"Total customers: {count}");
    }

    [Fact]
    public async Task Query_WithSingleFilter_StringParameter()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Filter theo name với parameter
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE name = @name LIMIT 10")
            .WithParams(new { name = "Alice Johnson" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c => Assert.Equal("Alice Johnson", c.Name));
        _output.WriteLine($"Found {list.Count} customers named Alice Johnson");
    }

    [Fact]
    public async Task Query_WithMultipleFilters()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Multiple WHERE conditions
        var sql = new TrinoSqlBuilder(
                "SELECT * FROM customers WHERE email LIKE @emailPattern AND name LIKE @namePattern ORDER BY name LIMIT 20")
            .WithParams(new { emailPattern = "%@gmail.com%", namePattern = "%John%" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c =>
        {
            Assert.Contains("@gmail.com", c.Email);
            Assert.Contains("John", c.Name);
        });
        _output.WriteLine($"Found {list.Count} customers named John with gmail");
    }

    [Fact]
    public async Task Query_WhereIf_ConditionTrue()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // WhereIf với condition = true
        bool hasNameFilter = true;
        var sql = new TrinoSqlBuilder("SELECT * FROM customers ORDER BY name LIMIT 10")
            .WhereIf(hasNameFilter, "name LIKE @pattern")
            .WithParams(new { pattern = "%John%" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c => Assert.Contains("John", c.Name));
        _output.WriteLine($"Found {list.Count} customers with 'John' in name");
    }

    [Fact]
    public async Task Query_WhereIf_ConditionFalse()
    {
        // Arrange - WhereIf với condition = false, không add filter
        bool hasNameFilter = false;
        var sql = new TrinoSqlBuilder("SELECT * FROM customers ORDER BY name LIMIT 5")
            .WhereIf(hasNameFilter, "name LIKE @pattern")
            .WithParams(new { pattern = "%John%" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.NotEmpty(list);
        // Không filter nên có thể có bất kỳ name nào
        _output.WriteLine($"No filter applied, found {list.Count} customers");
    }

    [Fact]
    public async Task Query_MultipleWhereIf_Mixed()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Nhiều WhereIf, một số true một số false
        bool hasNameFilter = true;
        bool hasIdFilter = false;
        bool hasEmailFilter = true;

        var sql = new TrinoSqlBuilder("SELECT * FROM customers ORDER BY name LIMIT 20")
            .WhereIf(hasNameFilter, "name LIKE @namePattern")
            .WhereIf(hasIdFilter, "id = @customerId")
            .WhereIf(hasEmailFilter, "email LIKE @emailPattern")
            .WithParams(new
            {
                namePattern = "%John%",
                customerId = "cust123",
                emailPattern = "%@example.com"
            })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c =>
        {
            Assert.Contains("John", c.Name);
            Assert.Contains("@example.com", c.Email);
            // id không được filter vì hasIdFilter = false
        });
        _output.WriteLine($"Found {list.Count} customers named John with example.com email");
    }

    [Fact]
    public async Task Query_WithEmailDomainFilter()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Filter theo email domain
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE email LIKE @domain ORDER BY email LIMIT 10")
            .WithParams(new { domain = "%@hotmail.com" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c => Assert.Contains("@hotmail.com", c.Email));
        _output.WriteLine($"Found {list.Count} customers with hotmail.com email");
    }

    [Fact]
    public async Task Query_WithIdFilter()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Filter by specific ID
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE id = @customerId")
            .WithParams(new { customerId = "test-001" })
            .Build();

        // Act
        var customer = await _context.QueryFirstOrDefaultAsync<Customer>(sql);

        // Assert
        Assert.NotNull(customer);
        Assert.Equal("test-001", customer.Id);
        Assert.Equal("John Smith", customer.Name);
    }

    [Fact]
    public async Task Query_WithLimitPagination()
    {
        // Arrange - Simple pagination with LIMIT only
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE email LIKE @emailPattern ORDER BY name LIMIT @limit")
            .WithParams(new
            {
                emailPattern = "%@%",
                limit = 10
            })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.True(list.Count <= 10);
        Assert.All(list, c => Assert.Contains("@", c.Email));
        _output.WriteLine($"Found {list.Count} customers (limit 10)");
    }

    [Fact]
    public async Task Query_WithInOperator()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Filter với IN operator
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE id IN (@id1, @id2, @id3) ORDER BY name LIMIT 15")
            .WithParams(new { id1 = "test-001", id2 = "test-002", id3 = "test-003" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c => Assert.Contains(c.Id, new[] { "test-001", "test-002", "test-003" }));
        _output.WriteLine($"Found {list.Count} customers with specific IDs");
    }

    [Fact]
    public async Task Query_WithStringFunction()
    {
        // Arrange - Insert test data trước
        await InsertTestCustomersAsync();
        
        // Filter with string function (length)
        var sql = new TrinoSqlBuilder("SELECT * FROM customers WHERE LENGTH(name) > @minLength ORDER BY name LIMIT 10")
            .WithParams(new { minLength = 10 })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.All(list, c => Assert.True(c.Name.Length > 10));
        _output.WriteLine($"Found {list.Count} customers with name length > 10");
    }

    [Fact]
    public async Task Query_TakeAndSkip_ForPagination()
    {
        // Arrange - Insert test data trước để có đủ records
        await InsertTestCustomersAsync();
        
        const int pageSize = 2;
        const int pageNumber = 2; // Page 2
        const int skipCount = (pageNumber - 1) * pageSize;
        
        // Dùng OFFSET + LIMIT cho pagination (Trino hỗ trợ OFFSET)
        var sql = new TrinoSqlBuilder("SELECT * FROM customers ORDER BY name OFFSET @skip LIMIT @take")
            .WithParams(new 
            { 
                skip = skipCount,
                take = pageSize
            })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        var list = customers.ToList();
        Assert.True(list.Count <= pageSize);
        Assert.True(list.Count > 0);
        
        _output.WriteLine($"Page {pageNumber} (skip {skipCount}, take {pageSize}): {string.Join(", ", list.Select(c => c.Name))}");
    }
}
