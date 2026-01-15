using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using EntityFramework.Trino.Context;
using EntityFramework.Trino.Iceberg;
using EntityFramework.Trino.Iceberg.Models;

namespace EntityFramework.Trino.Tests;

public class CrudOperationsTests
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;

    public CrudOperationsTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("localhost", 8081, "iceberg", "v4");
    }

    [Fact]
    public async Task Add_FirstOrDefault_ShouldInsertSuccessfully()
    {
        // Arrange
        var customerId = $"TEST_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = customerId,
            Name = "Test Customer",
            Email = "test@example.com"
        };

        // Act
        _context.Customers.Add(customer);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal("Test Customer", found.Name);
        Assert.Equal("test@example.com", found.Email);
    }
    
    
    [Fact]
    public async Task Add_SingleCustomer_ShouldInsertSuccessfully()
    {
        // Arrange
        var customerId = $"TEST_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = customerId,
            Name = "Test Customer",
            Email = "test@example.com"
        };

        // Act
        _context.Customers.Add(customer);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal("Test Customer", found.Name);
        Assert.Equal("test@example.com", found.Email);
    }

    [Fact]
    public async Task AddAsync_SingleCustomer_ShouldInsertSuccessfully()
    {
        // Arrange
        var customerId = $"TEST_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = customerId,
            Name = "Test Customer Async",
            Email = "async@example.com"
        };

        // Act
        await _context.Customers.AddAsync(customer);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal("Test Customer Async", found.Name);
    }

    [Fact]
    public async Task AddRange_MultipleCustomers_ShouldInsertAll()
    {
        // Arrange
        var prefix = $"BULK_{Guid.NewGuid().ToString("N")[..8]}";
        var customers = new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "Bulk 1", Email = "bulk1@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "Bulk 2", Email = "bulk2@test.com" }
        };

        try
        {
            // Act
            await _context.Customers.AddRangeAsync(customers);

            // Assert - Query đơn giản, Iceberg eventual consistency tự handle
            await Task.Delay(100); // Delay nhẹ cho Iceberg commit
            
            var found = await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .ToListAsync();

            // Nếu chỉ thấy 1, retry 1 lần nữa
            if (found.Count < 2)
            {
                await Task.Delay(300);
                found = await _context.Customers
                    .Where(c => c.Id.Contains(prefix))
                    .ToListAsync();
            }

            Assert.Equal(2, found.Count);
            Assert.Contains(found, c => c.Name == "Bulk 1");
            Assert.Contains(found, c => c.Name == "Bulk 2");
        }
        finally
        {
            // Cleanup
            var toDelete = await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .ToListAsync();
            
            if (toDelete.Any())
            {
                await _context.Customers.RemoveRangeAsync(toDelete);
            }
        }
    }

    [Fact]
    public async Task Update_ExistingCustomer_ShouldModifySuccessfully()
    {
        // Arrange
        var customerId = $"UPD_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = customerId,
            Name = "Original Name",
            Email = "original@example.com"
        };
        await _context.Customers.AddAsync(customer);

        // Act
        customer.Name = "Updated Name";
        customer.Email = "updated@example.com";
        await _context.Customers.UpdateAsync(customer);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal("Updated Name", found.Name);
        Assert.Equal("updated@example.com", found.Email);
    }

    [Fact]
    public async Task BulkUpdate_WithWhere_ShouldUpdateMatchingRecords()
    {
        // Arrange
        var prefix = $"BULKUP_{Guid.NewGuid().ToString("N")[..8]}";
        await _context.Customers.AddRangeAsync(new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "Original 1", Email = "o1@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "Original 2", Email = "o2@test.com" }
        });

        // Act - Update each customer individually (Dictionary-based API removed for security)
        var toUpdate = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();
        
        foreach (var customer in toUpdate)
        {
            customer.Name = "BULK UPDATED";
            await _context.Customers.UpdateAsync(customer);
        }

        // Assert
        var updated = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();

        Assert.All(updated, c => Assert.Equal("BULK UPDATED", c.Name));
    }

    [Fact]
    public async Task Remove_ExistingCustomer_ShouldDeleteSuccessfully()
    {
        // Arrange
        var customerId = $"DEL_{Guid.NewGuid().ToString("N")[..8]}";
        var customer = new Customer
        {
            Id = customerId,
            Name = "To Delete",
            Email = "delete@example.com"
        };
        await _context.Customers.AddAsync(customer);

        // Act
        await _context.Customers.RemoveAsync(customer);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        Assert.Null(found);
    }

    [Fact]
    public async Task RemoveRange_MultipleCustomers_ShouldDeleteAll()
    {
        // Arrange
        var prefix = $"DELR_{Guid.NewGuid().ToString("N")[..8]}";
        var customers = new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "Del 1", Email = "del1@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "Del 2", Email = "del2@test.com" }
        };
        await _context.Customers.AddRangeAsync(customers);

        // Act
        await _context.Customers.RemoveRangeAsync(customers);

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();

        Assert.Empty(found);
    }

    [Fact]
    public async Task DeleteAsync_WithWhere_ShouldDeleteMatchingRecords()
    {
        // Arrange
        var prefix = $"DELW_{Guid.NewGuid().ToString("N")[..8]}";
        await _context.Customers.AddRangeAsync(new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "Del Where 1", Email = "dw1@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "Del Where 2", Email = "dw2@test.com" }
        });

        // Act
        await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .DeleteAsync();

        // Assert
        var found = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();

        Assert.Empty(found);
    }

    [Fact]
    public async Task BulkWorkflow_InsertUpdateDelete_ShouldExecuteInOrder()
    {
        // Arrange
        var prefix = $"WORKFLOW_{Guid.NewGuid().ToString("N")[..8]}";
        var customers = Enumerable.Range(1, 3)
            .Select(i => new Customer
            {
                Id = $"{prefix}_{i}",
                Name = $"Workflow {i}",
                Email = $"wf{i}@test.com"
            })
            .ToArray();

        // Act & Assert - Insert
        await _context.Customers.AddRangeAsync(customers);
        var inserted = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();
        Assert.Equal(3, inserted.Count);

        // Act & Assert - Update (using individual updates for security)
        foreach (var customer in inserted)
        {
            customer.Name = "UPDATED";
            await _context.Customers.UpdateAsync(customer);
        }
        var updated = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();
        Assert.All(updated, c => Assert.Equal("UPDATED", c.Name));

        // Act & Assert - Delete
        await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .DeleteAsync();
        var deleted = await _context.Customers
            .Where(c => c.Id.Contains(prefix))
            .ToListAsync();
        Assert.Empty(deleted);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
