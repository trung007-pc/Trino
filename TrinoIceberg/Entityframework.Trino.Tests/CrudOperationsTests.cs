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
    public async Task AddRangeV1_MultipleCustomers_ShouldInsertAllWithSingleQuery()
    {
        // Arrange
        _output.WriteLine("===== TEST: AddRangeV1 - Multiple VALUES INSERT =====");
        var prefix = $"V1BULK_{Guid.NewGuid().ToString("N")[..8]}";
        var customers = new[]
        {
            new Customer { Id = $"{prefix}_1", Name = "V1 Bulk 1", Email = "v1bulk1@test.com" },
            new Customer { Id = $"{prefix}_2", Name = "V1 Bulk 2", Email = "v1bulk2@test.com" },
            new Customer { Id = $"{prefix}_3", Name = "V1 Bulk 3", Email = "v1bulk3@test.com" },
            new Customer { Id = $"{prefix}_4", Name = "V1 Bulk 3", Email = "v1bulk3@test.com" },
            new Customer { Id = $"{prefix}_5", Name = "V1 Bulk 3", Email = "v1bulk3@test.com" },
            new Customer { Id = $"{prefix}_6", Name = "V1 Bulk 3", Email = "v1bulk3@test.com" },
            new Customer { Id = $"{prefix}_7", Name = "V1 Bulk 3", Email = "v1bulk3@test.com" },
        };

        try
        {
            _output.WriteLine($"Inserting {customers.Length} customers with AddRangeV1...");
            
            // Act - Sử dụng multiple VALUES INSERT (nhanh hơn 30x)
            _context.Customers.AddRangeV1(customers);

            _output.WriteLine("Insert completed. Waiting for Iceberg commit...");
            await Task.Delay(100); // Delay nhẹ cho Iceberg commit
            
            // Assert
            var found = await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .ToListAsync();
            

            _output.WriteLine($"Found {found.Count} customers after insert");
            Assert.Equal(7, found.Count);
            Assert.Contains(found, c => c.Name == "V1 Bulk 1");
            Assert.Contains(found, c => c.Name == "V1 Bulk 2");
            Assert.Contains(found, c => c.Name == "V1 Bulk 3");
        }
        finally
        {
            // Cleanup
            _output.WriteLine("Cleaning up test data...");
            var toDelete = await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .ToListAsync();
            
            if (toDelete.Any())
            {
                await _context.Customers.RemoveRangeAsync(toDelete);
                _output.WriteLine($"Deleted {toDelete.Count} test records");
            }
        }
    }

    [Fact]
    public async Task AddRangeV1Async_LargeBatch_ShouldInsertFaster()
    {
        // Arrange
        _output.WriteLine("===== TEST: AddRangeV1Async - Large Batch INSERT =====");
        var prefix = $"LARGE_{Guid.NewGuid().ToString("N")[..8]}";
        var batchSize = 100;
        var customers = Enumerable.Range(1, batchSize)
            .Select(i => new Customer
            {
                Id = $"{prefix}_{i:D3}",
                Name = $"Large Batch Customer {i}",
                Email = $"large{i}@test.com"
            })
            .ToArray();

        try
        {
            _output.WriteLine($"Inserting {customers.Length} customers with AddRangeV1Async...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Act - Multiple VALUES INSERT (1 transaction thay vì 100 transactions)
            await _context.Customers.AddRangeV1Async(customers);
            
            sw.Stop();
            _output.WriteLine($"Insert completed in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine("Waiting for Iceberg commit...");
            
            await Task.Delay(200); // Delay cho Iceberg commit
            
            // Assert - Verify tất cả records đã insert
            var found = await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .CountAsync();

            // Retry nếu eventual consistency chưa sync
            if (found < batchSize)
            {
                _output.WriteLine($"Only found {found}/{batchSize} records, retrying...");
                await Task.Delay(500);
                found = await _context.Customers
                    .Where(c => c.Id.Contains(prefix))
                    .CountAsync();
            }

            _output.WriteLine($"Final count: {found}/{batchSize} customers");
            Assert.Equal(batchSize, found);
        }
        finally
        {
            // Cleanup
            _output.WriteLine("Cleaning up large batch test data...");
            await _context.Customers
                .Where(c => c.Id.Contains(prefix))
                .DeleteAsync();
            _output.WriteLine("Cleanup completed");
        }
    }

    [Fact]
    public async Task ComparePerformance_AddRangeVsAddRangeV1()
    {
        // Test so sánh performance giữa AddRange (N queries) vs AddRangeV1 (1 query)
        _output.WriteLine("===== PERFORMANCE COMPARISON: AddRange vs AddRangeV1 =====");
        
        var prefix1 = $"PERF1_{Guid.NewGuid().ToString("N")[..8]}";
        var prefix2 = $"PERF2_{Guid.NewGuid().ToString("N")[..8]}";
        var batchSize = 50; // Giảm xuống 50 để test nhanh hơn

        try
        {
            // Test 1: AddRange (N transactions)
            var customers1 = Enumerable.Range(1, batchSize)
                .Select(i => new Customer
                {
                    Id = $"{prefix1}_{i:D2}",
                    Name = $"Perf Test 1 - {i}",
                    Email = $"perf1_{i}@test.com"
                })
                .ToArray();

            _output.WriteLine($"\n[TEST 1] AddRange - Inserting {batchSize} records (N transactions)...");
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            await _context.Customers.AddRangeAsync(customers1);
            sw1.Stop();
            _output.WriteLine($"AddRange completed in {sw1.ElapsedMilliseconds}ms");

            // Test 2: AddRangeV1 (1 transaction)
            var customers2 = Enumerable.Range(1, batchSize)
                .Select(i => new Customer
                {
                    Id = $"{prefix2}_{i:D2}",
                    Name = $"Perf Test 2 - {i}",
                    Email = $"perf2_{i}@test.com"
                })
                .ToArray();

            _output.WriteLine($"\n[TEST 2] AddRangeV1 - Inserting {batchSize} records (1 transaction)...");
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            await _context.Customers.AddRangeV1Async(customers2);
            sw2.Stop();
            _output.WriteLine($"AddRangeV1 completed in {sw2.ElapsedMilliseconds}ms");

            // Performance analysis
            var speedup = (double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds;
            _output.WriteLine($"\n===== RESULTS =====");
            _output.WriteLine($"AddRange:   {sw1.ElapsedMilliseconds}ms ({batchSize} separate INSERTs)");
            _output.WriteLine($"AddRangeV1: {sw2.ElapsedMilliseconds}ms (1 multiple VALUES INSERT)");
            _output.WriteLine($"Speedup:    {speedup:F2}x faster");
            _output.WriteLine($"Time saved: {sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds}ms");

            // Verify data
            await Task.Delay(200);
            var count1 = await _context.Customers.Where(c => c.Id.Contains(prefix1)).CountAsync();
            var count2 = await _context.Customers.Where(c => c.Id.Contains(prefix2)).CountAsync();
            
            _output.WriteLine($"\nData verification:");
            _output.WriteLine($"Method 1 inserted: {count1}/{batchSize} records");
            _output.WriteLine($"Method 2 inserted: {count2}/{batchSize} records");

            Assert.True(speedup > 1, $"AddRangeV1 should be faster, but got {speedup:F2}x");
        }
        finally
        {
            // Cleanup
            _output.WriteLine("\nCleaning up performance test data...");
            await _context.Customers.Where(c => c.Id.Contains(prefix1)).DeleteAsync();
            await _context.Customers.Where(c => c.Id.Contains(prefix2)).DeleteAsync();
            _output.WriteLine("Cleanup completed");
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
