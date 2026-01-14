using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using EntityFramework.Trino.Context;
using Trino.Data.ADO.Server;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;

namespace EntityFramework.Trino.Tests;

public class QueryOperationsTests
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;

    public QueryOperationsTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
    }

    [Fact]
    public async Task ToListAsync_ShouldReturnAllCustomers()
    {
        // Act
        var customers = await _context.Customers.ToListAsync();

        // Assert
        Assert.NotNull(customers);
        Assert.NotEmpty(customers);
    }

    [Fact]
    public async Task Where_WithLambda_ShouldFilterCorrectly()
    {
        // Arrange
        var testId = $"WHERE_LAMBDA_{Guid.NewGuid().ToString("N")[..8]}";
        await _context.Customers.AddAsync(new Customer
        {
            Id = testId,
            Name = "Lambda Test",
            Email = "lambda@test.com"
        });

        // Act
        var customer = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(customer);
        Assert.Equal(testId, customer.Id);
        
        // Cleanup
        await _context.Customers.RemoveAsync(customer);
    }

    [Fact]
    public async Task Where_WithStringCondition_ShouldFilterCorrectly()
    {
        // Arrange
        var testId = $"WHERE_STR_{Guid.NewGuid().ToString("N")[..8]}";
        await _context.Customers.AddAsync(new Customer
        {
            Id = testId,
            Name = "String Test",
            Email = "string@test.com"
        });

        // Act
        var customers = await _context.Customers
            .Where(x => x.Id == testId)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(customers);
        Assert.All(customers, c => Assert.Equal(testId, c.Id));
        
        // Cleanup
        foreach (var c in customers)
            await _context.Customers.RemoveAsync(c);
    }

    [Fact]
    public async Task OrderBy_Ascending_ShouldSortCorrectly()
    {
        // Arrange
        var prefix = $"ORDER_{Guid.NewGuid().ToString("N")[..8]}";
        var testData = new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "Charlie", Email = "c@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "Alice", Email = "a@test.com" },
            new Customer { Id = $"{prefix}_3", Name = "Bob", Email = "b@test.com" }
        };
        await _context.Customers.AddRangeAsync(testData);

        // Act
        var customers = await _context.Customers
            .Where(c => c.Id.StartsWith(prefix))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Assert
        var names = customers.Select(c => c.Name).ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, names);
        
        // Cleanup
        await _context.Customers.RemoveRangeAsync(testData);
    }

    [Fact]
    public async Task OrderByDescending_ShouldSortCorrectly()
    {
        // Act
        var customers = await _context.Customers
            .OrderByDescending(c => c.Name)
            .Take(5)
            .ToListAsync();

        // Assert
        var names = customers.Select(c => c.Name).ToList();
        var sortedNames = names.OrderByDescending(n => n).ToList();
        Assert.Equal(sortedNames, names);
    }

    [Fact]
    public async Task Take_ShouldLimitResults()
    {
        // Act
        var customers = await _context.Customers
            .Take(3)
            .ToListAsync();

        // Assert
        Assert.Equal(3, customers.Count);
    }

    [Fact]
    public async Task Skip_ShouldSkipRecords()
    {
        // Arrange
        var allCustomers = await _context.Customers
            .OrderBy(c => c.Id)
            .ToListAsync();

        // Act
        var skipped = await _context.Customers
            .OrderBy(c => c.Id)
            .Skip(2)
            .ToListAsync();

        // Assert
        Assert.Equal(allCustomers.Count - 2, skipped.Count);
        Assert.Equal(allCustomers[2].Id, skipped[0].Id);
    }

    [Fact]
    public async Task SkipAndTake_ShouldPaginateCorrectly()
    {
        // Act
        var page = await _context.Customers
            .OrderBy(c => c.Id)
            .Skip(1)
            .Take(2)
            .ToListAsync();

        // Assert
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnTotalCount()
    {
        // Act
        var count = await _context.Customers.CountAsync();

        // Assert
        Assert.True(count > 0);
    }

    [Fact]
    public async Task AnyAsync_WithoutCondition_ShouldReturnTrue()
    {
        // Act
        var hasAny = await _context.Customers.AnyAsync();

        // Assert
        Assert.True(hasAny);
    }

    [Fact]
    public async Task AnyAsync_WithLambda_ShouldCheckCondition()
    {
        // Arrange
        var testId = $"ANY_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = testId,
            Name = "Any Test",
            Email = "any@test.com"
        };
        await _context.Customers.AddAsync(customer);

        // Act
        var exists = await _context.Customers
            .AnyAsync(c => c.Id == testId);

        // Assert
        Assert.True(exists);
        
        // Cleanup
        await _context.Customers.RemoveAsync(customer);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ShouldReturnFirstRecord()
    {
        // Act
        var first = await _context.Customers
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(first);
    }

    [Fact]
    public async Task FirstAsync_WithEmptyResult_ShouldThrowException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _context.Customers
                .Where(c => c.Id == "NONEXISTENT_ID_12345")
                .FirstAsync();
        });
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithMultipleResults_ShouldThrowException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _context.Customers.SingleOrDefaultAsync();
        });
    }

    [Fact]
    public async Task SingleAsync_WithSingleResult_ShouldReturnRecord()
    {
        // Arrange - Create a unique test record
        var testId = $"SINGLE_{Guid.NewGuid().ToString("N")[..8]}";
        await _context.Customers.AddAsync(new Customer
        {
            Id = testId,
            Name = "Single Test",
            Email = "single@test.com"
        });

        // Act
        var single = await _context.Customers
            .Where(c => c.Id == testId)
            .SingleAsync();

        // Assert
        Assert.NotNull(single);
        Assert.Equal(testId, single.Id);

        // Cleanup
        await _context.Customers.RemoveAsync(single);
    }

    [Fact]
    public async Task MethodChaining_ComplexQuery_ShouldWork()
    {
        // Arrange
        var testId = $"CHAIN_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = testId,
            Name = "Chain Test",
            Email = "chain@test.com"
        };
        await _context.Customers.AddAsync(customer);

        // Act
        var result = await _context.Customers
            .Where(c => c.Id == testId)
            .OrderBy(c => c.Name)
            .Take(5)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(result);
        
        // Cleanup
        await _context.Customers.RemoveAsync(customer);
    }

    [Fact]
    public async Task MethodContainAndWhereIF_ShouldWork()
    {
        // Arrange
        var testId = $"CHAIN_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = testId,
            Name = "Chain Test tn3",
            Email = "chain@test.com"
        };
        await _context.Customers.AddAsync(customer);
        var isCoddition = true;
        // Act
        var result = await _context.Customers
            .WhereIf(false,c => c.Name.Contains("tn3"))
            .OrderBy(c => c.Name)
            .Take(5)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(result);
        
        // Cleanup
        await _context.Customers.RemoveAsync(customer);
    }
    
    
    
    [Fact]
    public async Task Immutability_MultipleQueries_ShouldBeIndependent()
    {
        // Arrange
        var baseQuery = _context.Customers;

        // Act
        var query1 = await baseQuery.Where(c => c.Id == "1").ToListAsync();
        var query2 = await baseQuery.Where(c => c.Id == "2").ToListAsync();
        var query3 = await baseQuery.ToListAsync(); // Should return all, not affected by query1 or query2

        // Assert - Each query should be independent
        if (query1.Any()) Assert.All(query1, c => Assert.Equal("1", c.Id));
        if (query2.Any()) Assert.All(query2, c => Assert.Equal("2", c.Id));
        Assert.True(query3.Count >= query1.Count + query2.Count); // query3 should have more records
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
