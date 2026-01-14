using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Tests for WhereIf on TrinoDbSet - LINQ expression style
/// </summary>
public class TrinoDbSetWhereIfTests : IDisposable
{
    private readonly IcebergDbContext _context;
    private readonly List<string> _testCustomerIds = new();

    public TrinoDbSetWhereIfTests()
    {
        var connectionProperties = new TrinoConnectionProperties
        {
            Host = "localhost",
            Port = 8081,
            Catalog = "iceberg",
            Schema = "v4",
            User = "trino",
            EnableSsl = false
        };

        _context = new IcebergDbContext(connectionProperties);
    }

    [Fact]
    public async Task WhereIf_WithAllConditionsTrue_ShouldFilterCorrectly()
    {
        // Arrange
        var customerId1 = $"CUST_DBSET_{Guid.NewGuid():N}";
        var customerId2 = $"CUST_DBSET_{Guid.NewGuid():N}";
        
        _testCustomerIds.Add(customerId1);
        _testCustomerIds.Add(customerId2);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId1,
            Name = "WhereIf Test Active",
            Email = "active@whereif.com"
        });

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId2,
            Name = "WhereIf Test Inactive",
            Email = "inactive@whereif.com"
        });

        // Act - All conditions true
        string? searchName = "Active";
        string? emailDomain = "whereif.com";

        var results = await _context.Customers
            .WhereIf(!string.IsNullOrEmpty(searchName), c => c.Name.Contains(searchName))
            .WhereIf(!string.IsNullOrEmpty(emailDomain), c => c.Email.Contains(emailDomain))
            .ToListAsync();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, c => 
        {
            Assert.Contains("Active", c.Name);
            Assert.Contains("whereif.com", c.Email);
        });
    }

    [Fact]
    public async Task WhereIf_WithSomeConditionsFalse_ShouldOnlyApplyTrueConditions()
    {
        // Arrange
        var customerId1 = $"CUST_PARTIAL_{Guid.NewGuid():N}";
        var customerId2 = $"CUST_PARTIAL_{Guid.NewGuid():N}";
        
        _testCustomerIds.Add(customerId1);
        _testCustomerIds.Add(customerId2);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId1,
            Name = "Partial Test Alpha",
            Email = "alpha@partial.com"
        });

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId2,
            Name = "Partial Test Beta",
            Email = "beta@partial.com"
        });

        // Act - Only name filter active
        string? searchName = "Alpha";
        string? emailFilter = null; // ← null = skip this condition

        var results = await _context.Customers
            .WhereIf(!string.IsNullOrEmpty(searchName), c => c.Name.Contains(searchName))
            .WhereIf(!string.IsNullOrEmpty(emailFilter), c => c.Email.Contains(emailFilter!))
            .ToListAsync();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, c => Assert.Contains("Alpha", c.Name));
    }

    [Fact]
    public async Task WhereIf_WithAllConditionsFalse_ShouldReturnAll()
    {
        // Arrange
        var customerId = $"CUST_NOFILTER_{Guid.NewGuid():N}";
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "No Filter Test",
            Email = "nofilter@test.com"
        });

        // Act - All conditions false
        string? searchName = null;
        string? emailFilter = null;

        var results = await _context.Customers
            .WhereIf(!string.IsNullOrEmpty(searchName), c => c.Name.Contains(searchName!))
            .WhereIf(!string.IsNullOrEmpty(emailFilter), c => c.Email.Contains(emailFilter!))
            .ToListAsync();

        // Assert - Should return at least our test data
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task WhereIf_ComplexScenario_ShouldWork()
    {
        // Arrange
        var customerId = $"CUST_COMPLEX_{Guid.NewGuid():N}";
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Complex WhereIf Test",
            Email = "complex@whereif.com"
        });

        // Act - Multiple dynamic filters like ABP
        var nameFilter = "Complex";
        var emailFilter = "whereif.com";
        bool? applyNameFilter = true;
        bool? applyEmailFilter = true;

        var results = await _context.Customers
            .WhereIf(applyNameFilter == true && !string.IsNullOrEmpty(nameFilter), 
                c => c.Name.Contains(nameFilter))
            .WhereIf(applyEmailFilter == true && !string.IsNullOrEmpty(emailFilter), 
                c => c.Email.Contains(emailFilter))
            .ToListAsync();

        // Assert
        Assert.NotEmpty(results);
        var result = results.First();
        Assert.Contains("Complex", result.Name);
        Assert.Contains("whereif.com", result.Email);
    }

    [Fact]
    public async Task WhereIf_CombinedWithRegularWhere_ShouldWork()
    {
        // Arrange
        var customerId = $"CUST_COMBINED_{Guid.NewGuid():N}";
        _testCustomerIds.Add(customerId);

        await _context.Customers.AddAsync(new Customer
        {
            Id = customerId,
            Name = "Combined Test",
            Email = "combined@test.com"
        });

        // Act - Mix Where and WhereIf
        string? searchName = "Combined";

        var results = await _context.Customers
            .Where(c => c.Email.Contains("test.com"))  // Always applied
            .WhereIf(!string.IsNullOrEmpty(searchName), c => c.Name.Contains(searchName))  // Conditional
            .ToListAsync();

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, c => 
        {
            Assert.Contains("test.com", c.Email);
            Assert.Contains("Combined", c.Name);
        });
    }

    [Fact]
    public async Task WhereIf_WithTakeAndSkip_ShouldWork()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var customerId = $"CUST_PAGE_{i}_{Guid.NewGuid():N}";
            _testCustomerIds.Add(customerId);

            await _context.Customers.AddAsync(new Customer
            {
                Id = customerId,
                Name = $"Page Test {i}",
                Email = $"page{i}@test.com"
            });
        }

        // Act - Pagination with WhereIf
        string? nameFilter = "Page";
        int pageSize = 2;
        int pageNumber = 1; // Skip first 2, take next 2

        var results = await _context.Customers
            .WhereIf(!string.IsNullOrEmpty(nameFilter), c => c.Name.Contains(nameFilter))
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Count <= pageSize);
    }

    public void Dispose()
    {
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
}
