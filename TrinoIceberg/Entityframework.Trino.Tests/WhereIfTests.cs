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
/// Test cases cho WhereIf - dynamic conditional queries
/// TẤT CẢ đều dùng TrinoSqlBuilder
/// </summary>
public class WhereIfTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;

    public WhereIfTests(ITestOutputHelper output)
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
    public async Task WhereIf_AllConditionsTrue()
    {
        // Arrange - Cả 2 điều kiện đều true
        var hasNameFilter = true;
        var hasEmailFilter = true;

        var sql = new TrinoSqlBuilder("SELECT * FROM customers LIMIT 10")
            .WhereIf(hasNameFilter, "LENGTH(name) > @minLength")
            .WhereIf(hasEmailFilter, "email LIKE @emailPattern")
            .WithParams(new { 
                minLength = 3,
                emailPattern = "%@%"
            })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        Assert.NotNull(customers);
        Assert.Contains("WHERE", sql);
        Assert.Contains("LENGTH(name)", sql);
        Assert.Contains("email LIKE", sql);
        
        _output.WriteLine($"SQL: {sql}");
    }

    [Fact]
    public async Task WhereIf_NoConditions()
    {
        // Arrange - Không có điều kiện nào
        var hasFilter = false;

        var sql = new TrinoSqlBuilder("SELECT * FROM customers LIMIT 5")
            .WhereIf(hasFilter, "name = @name")
            .WithParams(new { name = "Test" })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        Assert.NotNull(customers);
        Assert.DoesNotContain("WHERE", sql);
        
        _output.WriteLine($"SQL without WHERE: {sql}");
    }

    [Fact]
    public async Task WhereIf_WithOrderByAndLimit()
    {
        // Arrange - WHERE phải chèn đúng vị trí (trước ORDER BY)
        var hasFilter = true;

        var sql = new TrinoSqlBuilder("SELECT * FROM customers ORDER BY name LIMIT 10")
            .WhereIf(hasFilter, "LENGTH(name) > @minLength")
            .WithParams(new { minLength = 3 })
            .Build();

        // Act
        var customers = await _context.QueryAsync<Customer>(sql);

        // Assert
        Assert.NotNull(customers);
        
        // WHERE phải đứng trước ORDER BY
        var wherePos = sql.IndexOf("WHERE");
        var orderPos = sql.IndexOf("ORDER BY");
        Assert.True(wherePos < orderPos, "WHERE should come before ORDER BY");
        
        _output.WriteLine($"SQL: {sql}");
    }
}
