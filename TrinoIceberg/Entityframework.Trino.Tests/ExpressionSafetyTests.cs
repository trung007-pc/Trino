using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Tests to verify that expression-based APIs are safe from SQL injection attacks
/// </summary>
public class ExpressionSafetyTests
{
    private readonly IcebergDbContext _context;

    public ExpressionSafetyTests()
    {
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
    }

    [Fact]
    public async Task Where_WithMaliciousString_ShouldBeSafe()
    {
        // Arrange - SQL injection attempt in variable
        var maliciousId = "'; DROP TABLE customers; --";
        var testId = $"SAFE_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Safe Test", 
            Email = "safe@test.com" 
        });

        // Act - Expression trees safely parameterize values
        var result = await _context.Customers
            .Where(c => c.Id == maliciousId)
            .FirstOrDefaultAsync();

        // Assert - No SQL injection, just returns null
        Assert.Null(result);

        // Verify original data still exists
        var original = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        Assert.NotNull(original);

        // Cleanup
        await _context.Customers.RemoveAsync(original);
    }

    [Fact]
    public async Task Where_WithQuotesInValue_ShouldEscapeCorrectly()
    {
        // Arrange
        var testId = $"QUOTE_TEST_{Guid.NewGuid():N}";
        var nameWithQuote = "O'Brien"; // Contains single quote
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = nameWithQuote, 
            Email = "obrien@test.com" 
        });

        // Act - Expression handles quotes safely
        var result = await _context.Customers
            .Where(c => c.Name == nameWithQuote)
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(nameWithQuote, result.Name);

        // Cleanup
        await _context.Customers.RemoveAsync(result);
    }

    [Fact]
    public async Task Where_WithOrCondition_ShouldBeSafe()
    {
        // Arrange - Attempt to inject OR condition
        var maliciousEmail = "test@example.com' OR '1'='1";
        var testId = $"OR_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "OR Test", 
            Email = "legitimate@test.com" 
        });

        // Act - Expression treats entire string as literal value
        var result = await _context.Customers
            .Where(c => c.Email == maliciousEmail)
            .ToListAsync();

        // Assert - Returns empty, not all records
        Assert.Empty(result);

        // Cleanup
        var cleanup = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        if (cleanup != null)
            await _context.Customers.RemoveAsync(cleanup);
    }

    [Fact]
    public async Task Where_WithUnionAttack_ShouldBeSafe()
    {
        // Arrange - UNION-based SQL injection attempt
        var maliciousId = "' UNION SELECT * FROM customers WHERE '1'='1";
        
        // Act - Expression API safely handles this
        var result = await _context.Customers
            .Where(c => c.Id == maliciousId)
            .ToListAsync();

        // Assert - Returns empty, UNION is not executed
        Assert.Empty(result);
    }

    [Fact]
    public async Task Where_WithCommentAttack_ShouldBeSafe()
    {
        // Arrange - SQL comment injection attempt
        var maliciousName = "Test'; --";
        var testId = $"COMMENT_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Safe Name", 
            Email = "safe@test.com" 
        });

        // Act
        var result = await _context.Customers
            .Where(c => c.Name == maliciousName)
            .ToListAsync();

        // Assert - No results, comment syntax is treated as literal
        Assert.Empty(result);

        // Cleanup
        var cleanup = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        if (cleanup != null)
            await _context.Customers.RemoveAsync(cleanup);
    }

    [Fact]
    public async Task OrderBy_IsSafeFromInjection()
    {
        // Arrange - Column name is determined at compile time
        var testId1 = $"ORDER_TEST_1_{Guid.NewGuid():N}";
        var testId2 = $"ORDER_TEST_2_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId1, 
            Name = "B Customer", 
            Email = "b@test.com" 
        });
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId2, 
            Name = "A Customer", 
            Email = "a@test.com" 
        });

        // Act - OrderBy uses expression, not string
        var results = await _context.Customers
            .Where(c => c.Id == testId1 || c.Id == testId2)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("A Customer", results[0].Name);
        Assert.Equal("B Customer", results[1].Name);

        // Cleanup
        foreach (var customer in results)
        {
            await _context.Customers.RemoveAsync(customer);
        }
    }

    [Fact]
    public async Task Select_ProjectionIsSafe()
    {
        // Arrange
        var testId = $"SELECT_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Projection Test", 
            Email = "projection@test.com" 
        });

        // Act - Select uses expression for projection
        var result = await _context.Customers
            .Where(c => c.Id == testId)
            .Select(c => new { c.Name, c.Email })
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Projection Test", result.Name);

        // Cleanup
        var cleanup = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        if (cleanup != null)
            await _context.Customers.RemoveAsync(cleanup);
    }

    [Fact]
    public async Task Take_WithLargeNumber_ShouldBeSafe()
    {
        // Arrange - Attempt to cause issues with large LIMIT
        var testId = $"TAKE_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Take Test", 
            Email = "take@test.com" 
        });

        // Act - Take is parameterized safely
        var results = await _context.Customers
            .Where(c => c.Id == testId)
            .Take(999999999)
            .ToListAsync();

        // Assert
        Assert.Single(results);

        // Cleanup
        await _context.Customers.RemoveAsync(results[0]);
    }

    [Fact]
    public async Task ComplexWhere_WithMultipleConditions_ShouldBeSafe()
    {
        // Arrange
        var testId = $"COMPLEX_TEST_{Guid.NewGuid():N}";
        var maliciousEmail = "' OR 1=1 --";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Complex Test", 
            Email = "complex@test.com" 
        });

        // Act - Complex expression with malicious value
        var result = await _context.Customers
            .Where(c => c.Id == testId && c.Email == maliciousEmail)
            .FirstOrDefaultAsync();

        // Assert - Returns null, malicious email is just a literal
        Assert.Null(result);

        // Cleanup
        var cleanup = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        if (cleanup != null)
            await _context.Customers.RemoveAsync(cleanup);
    }

    [Fact]
    public async Task Count_IsSafeFromInjection()
    {
        // Arrange
        var testId = $"COUNT_TEST_{Guid.NewGuid():N}";
        
        await _context.Customers.AddAsync(new Customer 
        { 
            Id = testId, 
            Name = "Count Test", 
            Email = "count@test.com" 
        });

        // Act - Count uses expression-based WHERE
        var count = await _context.Customers
            .Where(c => c.Id == testId)
            .CountAsync();

        // Assert
        Assert.Equal(1, count);

        // Cleanup
        var cleanup = await _context.Customers
            .Where(c => c.Id == testId)
            .FirstOrDefaultAsync();
        if (cleanup != null)
            await _context.Customers.RemoveAsync(cleanup);
    }
}
