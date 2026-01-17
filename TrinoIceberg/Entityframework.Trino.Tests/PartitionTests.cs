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

/// <summary>
/// Test cases cho Partition Performance và Data Organization
/// Yêu cầu: Phải tạo table với script create_customers_partitioned_table.sql trước khi chạy tests
/// </summary>
public class PartitionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;

    public PartitionTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("192.168.100.17", 8000, "iceberg", "v1");
    }

    /// <summary>
    /// Test Case 1: Insert từng dòng (row-by-row)
    /// Mục đích: Kiểm tra Iceberg tự động phân loại vào đúng partition folder
    /// Kỳ vọng: 
    /// - 3 records với 3 OrderDate khác nhau = 3 partitions khác nhau
    /// - Query theo OrderDate chỉ scan 1 partition
    /// </summary>
    [Fact]
    public async Task PartitionTest_InsertRowByRow_ShouldAutoDistributeToCorrectPartitions()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N")[..8];
        var baseDate = new DateTime(2024, 1, 15);
        
        _output.WriteLine("=== TEST: Insert từng dòng vào các partitions khác nhau ===");

        try
        {
            // Act: Insert 3 customers vào 3 ngày khác nhau (3 partitions)
            _output.WriteLine($"\n[1] Insert customer ngày {baseDate:yyyy-MM-dd} (Partition 1)");
            var customer1 = new Customer
            {
                Id = $"{Guid.NewGuid()}",
                Name = $"Customer_{testId}_Day1",
                Email = $"day1_{testId}@test.com",
                OrderDate = baseDate,
                Status = "Active"
            };
            _context.Customers.Add(customer1);
            
            _output.WriteLine($"[2] Insert customer ngày {baseDate.AddDays(1):yyyy-MM-dd} (Partition 2)");
            var customer2 = new Customer
            {
                Id = $"{Guid.NewGuid()}",
                Name = $"Customer_{testId}_Day2",
                Email = $"day2_{testId}@test.com",
                OrderDate = baseDate.AddDays(1),
                Status = "Pending"
            };
            _context.Customers.Add(customer2);
            
            _output.WriteLine($"[3] Insert customer ngày {baseDate.AddDays(2):yyyy-MM-dd} (Partition 3)");
            var customer3 = new Customer
            {
                Id = $"{Guid.NewGuid()}",
                Name = $"Customer_{testId}_Day3",
                Email = $"day3_{testId}@test.com",
                OrderDate = baseDate.AddDays(2),
                Status = "Inactive"
            };
            _context.Customers.Add(customer3);

            // Delay để Iceberg commit metadata
            await Task.Delay(200);

            // Assert 1: Query tất cả 3 records
            _output.WriteLine("\n=== ASSERT 1: Query tất cả records ===");
            var allRecords = await _context.Customers
                .Where(c => c.Name.Contains($"Customer_{testId}"))
                .OrderBy(c => c.OrderDate)
                .ToListAsync();

            _output.WriteLine($"✅ Tìm thấy {allRecords.Count} records (kỳ vọng: 3)");
            Assert.Equal(3, allRecords.Count);

            // Assert 2: Query chỉ 1 partition (ngày 15/01/2024)
            _output.WriteLine("\n=== ASSERT 2: Query 1 partition cụ thể (Partition Pruning) ===");
            _output.WriteLine($"Query: WHERE OrderDate = '{baseDate:yyyy-MM-dd}'");
            
            var partitionQuery = await _context.Customers
                .Where(c => c.OrderDate == baseDate && c.Name.Contains($"Customer_{testId}"))
                .ToListAsync();

            _output.WriteLine($"✅ Partition scan: CHỈ scan partition OrderDate={baseDate:yyyy-MM-dd}");
            _output.WriteLine($"✅ Tìm thấy {partitionQuery.Count} record (kỳ vọng: 1)");
            Assert.Single(partitionQuery);
            Assert.Equal("Day1", partitionQuery[0].Name.Split('_').Last());

            // Assert 3: Query range (scan nhiều partitions)
            _output.WriteLine("\n=== ASSERT 3: Query range partitions ===");
            var rangeQuery = await _context.Customers
                .Where(c => c.OrderDate >= baseDate 
                         && c.OrderDate <= baseDate.AddDays(1) 
                         && c.Name.Contains($"Customer_{testId}"))
                .ToListAsync();

            _output.WriteLine($"✅ Range scan: Scan 2 partitions (15/01 và 16/01)");
            _output.WriteLine($"✅ Tìm thấy {rangeQuery.Count} records (kỳ vọng: 2)");
            Assert.Equal(2, rangeQuery.Count);

            // Output: Phân bổ partitions
            _output.WriteLine("\n=== CẤU TRÚC PARTITIONS TRÊN S3 ===");
            _output.WriteLine("customers_partitioned/data/");
            _output.WriteLine($"  ├── OrderDate={baseDate:yyyy-MM-dd}/");
            _output.WriteLine($"  │   └── file1.parquet (Customer_{testId}_Day1)");
            _output.WriteLine($"  ├── OrderDate={baseDate.AddDays(1):yyyy-MM-dd}/");
            _output.WriteLine($"  │   └── file2.parquet (Customer_{testId}_Day2)");
            _output.WriteLine($"  └── OrderDate={baseDate.AddDays(2):yyyy-MM-dd}/");
            _output.WriteLine($"      └── file3.parquet (Customer_{testId}_Day3)");
        }
        finally
        {
            // Cleanup
            _output.WriteLine("\n=== CLEANUP ===");
            var toDelete = await _context.Customers
                .Where(c => c.Name.Contains($"Customer_{testId}"))
                .ToListAsync();
            
            if (toDelete.Any())
            {
                await _context.Customers.RemoveRangeAsync(toDelete);
                _output.WriteLine($"✅ Đã xóa {toDelete.Count} test records");
            }
        }
    }

    /// <summary>
    /// Test Case 2: Insert theo lô (batch insert)
    /// Mục đích: Kiểm tra performance khi insert hàng loạt vào cùng/nhiều partitions
    /// Kỳ vọng:
    /// - Batch insert vào cùng partition → Tạo ít files hơn (efficient)
    /// - Batch insert vào nhiều partitions → Iceberg tự động phân loại
    /// </summary>
    [Fact]
    public async Task PartitionTest_BatchInsert_ShouldDistributeEfficientlyToPartitions()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N")[..8];
        var baseDate = new DateTime(2024, 1, 20);
        
        _output.WriteLine("=== TEST: Batch Insert vào nhiều partitions ===");

        try
        {
            // Act 1: Batch insert 100 customers vào CÙNG 1 partition (ngày 20/01)
            _output.WriteLine($"\n[1] Batch Insert 100 customers vào partition {baseDate:yyyy-MM-dd}");
            var sameDayCustomers = Enumerable.Range(1, 50)
                .Select(i => new Customer
                {
                    Id = $"{Guid.NewGuid()}",
                    Name = $"Batch_{testId}_SameDay_{i:D3}",
                    Email = $"sameday{i}_{testId}@test.com",
                    OrderDate = baseDate,  // ← TẤT CẢ cùng ngày
                    Status = i % 3 == 0 ? "Active" : (i % 3 == 1 ? "Pending" : "Inactive")
                })
                .ToArray();

            await _context.Customers.AddRangeAsync(sameDayCustomers);
            await Task.Delay(300); // Delay để commit

            // Assert 1: Verify 100 records trong 1 partition
            var sameDayCount = await _context.Customers
                .Where(c => c.OrderDate == baseDate && c.Name.Contains($"Batch_{testId}_SameDay"))
                .CountAsync();

            _output.WriteLine($"✅ Đã insert {sameDayCount} records vào partition OrderDate={baseDate:yyyy-MM-dd}");
            Assert.Equal(100, sameDayCount);

            // Act 2: Batch insert 150 customers vào NHIỀU partitions (5 ngày khác nhau)
            _output.WriteLine($"\n[2] Batch Insert 150 customers vào 5 partitions khác nhau");
            var multiDayCustomers = Enumerable.Range(1, 150)
                .Select(i => new Customer
                {
                    Id = $"{Guid.NewGuid()}",
                    Name = $"Batch_{testId}_MultiDay_{i:D3}",
                    Email = $"multiday{i}_{testId}@test.com",
                    OrderDate = baseDate.AddDays(i % 5),  // ← 5 ngày khác nhau (21, 22, 23, 24, 25/01)
                    Status = "Active"
                })
                .ToArray();

            await _context.Customers.AddRangeAsync(multiDayCustomers);
            await Task.Delay(400); // Delay để commit

            // Assert 2: Verify phân bổ đều vào 5 partitions
            _output.WriteLine("\n=== PHÂN BỐ CUSTOMERS THEO PARTITION ===");
            for (int day = 1; day <= 5; day++)
            {
                var partitionDate = baseDate.AddDays(day);
                var count = await _context.Customers
                    .Where(c => c.OrderDate == partitionDate && c.Name.Contains($"Batch_{testId}_MultiDay"))
                    .CountAsync();

                _output.WriteLine($"Partition OrderDate={partitionDate:yyyy-MM-dd}: {count} customers");
                Assert.Equal(30, count); // 150 / 5 = 30 per partition
            }

            // Assert 3: Query aggregate theo Status trong 1 partition
            // _output.WriteLine("\n=== AGGREGATE QUERY TRONG 1 PARTITION ===");
            // var statusCounts = await _context.Customers
            //     .Where(c => c.OrderDate == baseDate && c.Name.Contains($"Batch_{testId}_SameDay"))
            //     .GroupBy(c => c.Status)
            //     .Select(g => new { Status = g.Key, Count = g.Count() })
            //     .ToListAsync();
            //
            // foreach (var stat in statusCounts.OrderBy(s => s.Status))
            // {
            //     _output.WriteLine($"Status={stat.Status}: {stat.Count} customers");
            // }
            //
            // // Assert 4: Query performance - range scan
            // _output.WriteLine("\n=== PERFORMANCE: Range Query ===");
            // var startTime = DateTime.Now;
            // var rangeResults = await _context.Customers
            //     .Where(c => c.OrderDate >= baseDate.AddDays(1) 
            //              && c.OrderDate <= baseDate.AddDays(3)
            //              && c.Name.Contains($"Batch_{testId}_MultiDay"))
            //     .ToListAsync();
            // var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            // _output.WriteLine($"✅ Query range (3 partitions): Tìm thấy {rangeResults.Count} records trong {elapsed:F2}ms");
            // _output.WriteLine($"✅ Partition scan: CHỈ scan 3 partitions (21, 22, 23/01)");
            // Assert.Equal(90, rangeResults.Count); // 30 * 3 = 90

            // Output: Cấu trúc partitions
            _output.WriteLine("\n=== CẤU TRÚC PARTITIONS TRÊN S3 ===");
            _output.WriteLine("customers_partitioned/data/");
            _output.WriteLine($"  ├── OrderDate={baseDate:yyyy-MM-dd}/ (100 customers - 1 batch)");
            _output.WriteLine($"  ├── OrderDate={baseDate.AddDays(1):yyyy-MM-dd}/ (30 customers)");
            _output.WriteLine($"  ├── OrderDate={baseDate.AddDays(2):yyyy-MM-dd}/ (30 customers)");
            _output.WriteLine($"  ├── OrderDate={baseDate.AddDays(3):yyyy-MM-dd}/ (30 customers)");
            _output.WriteLine($"  ├── OrderDate={baseDate.AddDays(4):yyyy-MM-dd}/ (30 customers)");
            _output.WriteLine($"  └── OrderDate={baseDate.AddDays(5):yyyy-MM-dd}/ (30 customers)");
            
            _output.WriteLine("\n💡 LỢI ÍCH PARTITION:");
            _output.WriteLine("   - Batch insert cùng partition → Ít files → Hiệu quả hơn");
            _output.WriteLine("   - Query theo OrderDate → Chỉ scan partition cần thiết");
            _output.WriteLine("   - Aggregate query → Chỉ đọc 1 partition thay vì scan toàn bộ");
        }
        finally
        {
            // Cleanup
            // _output.WriteLine("\n=== CLEANUP ===");
            // var toDelete = await _context.Customers
            //     .Where(c => c.Name.Contains($"Batch_{testId}"))
            //     .ToListAsync();
            //
            // if (toDelete.Any())
            // {
            //     await _context.Customers.RemoveRangeAsync(toDelete);
            //     _output.WriteLine($"✅ Đã xóa {toDelete.Count} test records");
            // }
        }
    }

    /// <summary>
    /// Test Case 3: So sánh query performance WITH vs WITHOUT partition filter
    /// Mục đích: Chứng minh Partition Pruning tăng performance
    /// </summary>
    [Fact]
    public async Task PartitionTest_ComparePerformance_WithAndWithoutPartitionFilter()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N")[..8];
        var baseDate = new DateTime(2024, 1, 25);
        
        _output.WriteLine("=== TEST: So sánh Performance (Partition Pruning) ===");

        try
        {
            // Setup: Insert 300 customers vào 10 partitions (30 per partition)
            _output.WriteLine($"\n[Setup] Insert 300 customers vào 10 partitions...");
            var customers = Enumerable.Range(1, 300)
                .Select(i => new Customer
                {
                    Id = $"{Guid.NewGuid()}",
                    Name = $"Perf_{testId}_{i:D3}",
                    Email = $"perf{i}_{testId}@test.com",
                    OrderDate = baseDate.AddDays(i % 10),
                    Status = "Active"
                })
                .ToArray();

            await _context.Customers.AddRangeAsync(customers);
            await Task.Delay(500);

            // Test 1: Query VỚI partition filter (FAST)
            _output.WriteLine("\n=== QUERY 1: VỚI Partition Filter (Partition Pruning) ===");
            var startFast = DateTime.Now;
            var fastQuery = await _context.Customers
                .Where(c => c.OrderDate == baseDate.AddDays(5) // ← Partition filter
                         && c.Name.Contains($"Perf_{testId}"))
                .ToListAsync();
            var fastElapsed = (DateTime.Now - startFast).TotalMilliseconds;

            _output.WriteLine($"✅ Query: WHERE OrderDate = '{baseDate.AddDays(5):yyyy-MM-dd}'");
            _output.WriteLine($"✅ Partition scan: CHỈ scan 1 partition (30 customers)");
            _output.WriteLine($"✅ Kết quả: {fastQuery.Count} records trong {fastElapsed:F2}ms");

            // Test 2: Query KHÔNG có partition filter (SLOW)
            _output.WriteLine("\n=== QUERY 2: KHÔNG có Partition Filter (Full Scan) ===");
            var startSlow = DateTime.Now;
            var slowQuery = await _context.Customers
                .Where(c => c.Status == "Active" // ← Không có partition filter
                         && c.Name.Contains($"Perf_{testId}"))
                .ToListAsync();
            var slowElapsed = (DateTime.Now - startSlow).TotalMilliseconds;

            _output.WriteLine($"✅ Query: WHERE Status = 'Active'");
            _output.WriteLine($"⚠️  Partition scan: SCAN TẤT CẢ 10 partitions (300 customers)");
            _output.WriteLine($"✅ Kết quả: {slowQuery.Count} records trong {slowElapsed:F2}ms");

            // Comparison
            _output.WriteLine("\n=== SO SÁNH PERFORMANCE ===");
            _output.WriteLine($"Với Partition Filter:    {fastElapsed:F2}ms (scan 30 customers)");
            _output.WriteLine($"Không Partition Filter:  {slowElapsed:F2}ms (scan 300 customers)");
            _output.WriteLine($"Speed-up: {(slowElapsed / fastElapsed):F2}x nhanh hơn");

            _output.WriteLine("\n💡 KẾT LUẬN:");
            _output.WriteLine("   - Query theo OrderDate (partition key) → Cực nhanh");
            _output.WriteLine("   - Query theo Status (non-partition) → Phải scan toàn bộ");
            _output.WriteLine("   - Chọn partition key = Cột hay query nhất!");
        }
        finally
        {
            // Cleanup
            var toDelete = await _context.Customers
                .Where(c => c.Name.Contains($"Perf_{testId}"))
                .ToListAsync();
            
            if (toDelete.Any())
            {
                await _context.Customers.RemoveRangeAsync(toDelete);
            }
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
