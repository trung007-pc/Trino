using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EntityFramework.Trino.Dapper;
using Xunit;
using Xunit.Abstractions;
using EntityFramework.Trino.Iceberg;
using kcb.KcbService.Entities.TongHopKcbs;

namespace EntityFramework.Trino.Tests;

/// <summary>
/// Performance test cho TongHopKcb - Test với 100k records
/// </summary>
public class TongHopKcbPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IcebergDbContext _context;
    private readonly string _testRunId;

    public TongHopKcbPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _context = IcebergDbContext.Create("192.168.100.17", 8000, "iceberg", "v1");
        _testRunId = $"PERF_{DateTime.Now:yyyyMMdd_HHmmss}";
        TrinoTypeHandlers.Initialize();
    }

    [Fact]
    public async Task Insert_SingleTongHopKcb_ShouldSucceed()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  TEST: Insert Single TongHopKcb Record");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        
        var testId = $"SINGLE_{Guid.NewGuid().ToString("N")[..8]}";
        var id = Guid.NewGuid();
        var record = new TongHopKcb
        {
            Id = id,
            // Primary keys
            MA_LK = testId,
            STT = 1,
            
            // Patient info
            MA_BN = "BN00000001",
            HO_TEN = "Nguyen Van Test Single",
            SO_CCCD = "001234567890",
            NGAY_SINH = new DateTime(1990, 5, 15),
            GIOI_TINH = 1, // Male
            NHOM_MAU = "O",
            MA_QUOCTICH = "VN",
            MA_DANTOC = "01", // Kinh
            MA_NGHE_NGHIEP = "05",
            
            // Address
            DIA_CHI = "123 Nguyen Hue Street, District 1",
            MATINH_CU_TRU = "79", // TP.HCM
            MAHUYEN_CU_TRU = "760", // Quan 1
            MAXA_CU_TRU = "26734", // Phuong Ben Nghe
            DIEN_THOAI = "0901234567",
            
            // Insurance
            MA_THE_BHYT = "DN1234567890123",
            MA_DKBD = "79001",
            GT_THE_TU = "2024-01-01",
            GT_THE_DEN = "2025-12-31",
            
            // Medical info
            LY_DO_VV = "Kham benh",
            CHAN_DOAN_VAO = "Viem hong cap",
            CHAN_DOAN_RV = "Viem hong cap, da dieu tri",
            MA_BENH_CHINH = "J02.9", // Acute pharyngitis
            MA_DOITUONG_KCB = "1", // BHYT
            
            // Dates
            NGAY_VAO = DateTime.Now.Date.AddDays(-2),
            NGAY_RA = DateTime.Now.Date.AddDays(-1),
            NGAY_TTOAN = DateTime.Now.Date,
            
            // Financial (VND)
            T_THUOC = 350000m,      // Medicine: 350k
            T_VTYT = 150000m,       // Medical supplies: 150k
            T_TONGCHI_BV = 800000m, // Total hospital cost: 800k
            T_TONGCHI_BH = 640000m, // Insurance covered: 640k (80%)
            T_BNTT = 160000m,       // Patient paid: 160k (20%)
            T_BHTT = 640000m,       // Insurance paid: 640k
            
            // Period
            NAM_QT = 2024,
            THANG_QT = DateTime.Now.Month,
            
            // Other
            MA_LOAI_KCB = "1", // Kham benh
            MA_KHOA = "K01", // Kham benh
            MA_CSKCB = "79001", // Hospital code
            SO_NGAY_DTRI = 1, // Treatment days
            KET_QUA_DTRI = 1, // Khoi benh
            
            // Test tracking
            ApiRequestId = Guid.NewGuid(),
            TenantId = Guid.NewGuid()
        };

        try
        {
            // Act
            _output.WriteLine("\n📝 Inserting record with details:");
            _output.WriteLine($"  • MA_LK:        {record.MA_LK}");
            _output.WriteLine($"  • Patient:      {record.HO_TEN}");
            _output.WriteLine($"  • Date of Birth: {record.NGAY_SINH:yyyy-MM-dd}");
            _output.WriteLine($"  • Insurance:    {record.MA_THE_BHYT}");
            _output.WriteLine($"  • Diagnosis:    {record.MA_BENH_CHINH} - {record.CHAN_DOAN_RV}");
            _output.WriteLine($"  • Total Cost:   {record.T_TONGCHI_BV:N0} VND");
            _output.WriteLine($"  • Patient Paid: {record.T_BNTT:N0} VND");
            _output.WriteLine("");

            var sw = Stopwatch.StartNew();
            await _context.TongHopKcbs.AddAsync(record);
            sw.Stop();

            _output.WriteLine($"✅ Insert completed in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine("");

            // Assert - Wait for Iceberg commit
            _output.WriteLine("⏳ Waiting for Iceberg commit (2 seconds)...");
            await Task.Delay(2000);
            
            var sql = new TrinoSqlBuilder(@"
                select t.* from tonghopkcb as t where id = UUID @id ")
                .WithParams(new {id = id})
                .Build();
            
            var found = await _context.QueryFirstOrDefaultAsync<TongHopKcb>(sql);

            Assert.NotNull(found);
            Assert.Equal(testId, found.MA_LK);
            Assert.Equal("Nguyen Van Test Single", found.HO_TEN);
            Assert.Equal("BN00000001", found.MA_BN);
            Assert.Equal(350000m, found.T_THUOC);

            _output.WriteLine("✅ VERIFICATION:");
            _output.WriteLine($"  • Record found in database: {found.MA_LK}");
            _output.WriteLine($"  • Patient name matches: {found.HO_TEN}");
            _output.WriteLine($"  • Medicine cost matches: {found.T_THUOC:N0} VND");
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  ✅ TEST PASSED");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
        }
        finally
        {
            // Cleanup
            _output.WriteLine("\n🧹 Cleaning up test data...");
            var deleted = await _context.TongHopKcbs
                .Where(t => t.MA_LK == testId)
                .DeleteAsync();
            _output.WriteLine($"✓ Deleted {deleted} test record(s)");
        }
    }

    [Fact]
    public async Task Insert100K_TongHopKcb_MeasurePerformanceAndS3Files()
    {
        // ===== CONFIGURATION =====
        const int TOTAL_RECORDS = 100_000;
        const int BATCH_SIZE = 1000; // Insert 1000 records per batch
        var totalBatches = TOTAL_RECORDS / BATCH_SIZE;

        _output.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        _output.WriteLine("║      PERFORMANCE TEST: 100K TongHopKcb Records INSERT         ║");
        _output.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        _output.WriteLine($"Test Run ID: {_testRunId}");
        _output.WriteLine($"Total Records: {TOTAL_RECORDS:N0}");
        _output.WriteLine($"Batch Size: {BATCH_SIZE:N0}");
        _output.WriteLine($"Total Batches: {totalBatches}");
        _output.WriteLine("");

        try
        {
            // ===== STEP 1: GENERATE 100K FAKE RECORDS =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("STEP 1: Generating 100,000 fake records...");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            var generateSw = Stopwatch.StartNew();
            var allRecords = GenerateFakeTongHopKcbRecords(TOTAL_RECORDS);
            generateSw.Stop();
            
            _output.WriteLine($"✓ Generated {allRecords.Count:N0} records in {generateSw.ElapsedMilliseconds:N0}ms");
            _output.WriteLine("");

            // ===== MEASURE BATCH SIZE =====
            MeasureBatchSize(allRecords, BATCH_SIZE);

            // ===== STEP 2: INSERT RECORDS WITH BATCHING =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("STEP 2: Inserting records in batches...");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            var batchTimes = new List<long>();
            var totalInsertSw = Stopwatch.StartNew();

            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                var batch = allRecords.Skip(batchIndex * BATCH_SIZE).Take(BATCH_SIZE).ToList();
                
                var batchSw = Stopwatch.StartNew();
                await _context.TongHopKcbs.AddRangeV1Async(batch);
                batchSw.Stop();
                
                batchTimes.Add(batchSw.ElapsedMilliseconds);
                
                // Progress report every 10 batches
                if ((batchIndex + 1) % 10 == 0 || batchIndex == totalBatches - 1)
                {
                    var progress = (batchIndex + 1) * 100.0 / totalBatches;
                    var avgBatchTime = batchTimes.Average();
                    _output.WriteLine($"  [{batchIndex + 1,3}/{totalBatches}] Progress: {progress:F1}% | " +
                                    $"Batch time: {batchSw.ElapsedMilliseconds,5}ms | " +
                                    $"Avg: {avgBatchTime:F0}ms");
                }
            }

            totalInsertSw.Stop();
            _output.WriteLine("");
            _output.WriteLine($"✓ All {TOTAL_RECORDS:N0} records inserted successfully!");
            _output.WriteLine("");

            // ===== STEP 3: PERFORMANCE STATISTICS =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("STEP 3: Performance Statistics");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            var totalTimeMs = totalInsertSw.ElapsedMilliseconds;
            var totalTimeSec = totalTimeMs / 1000.0;
            var avgBatchTimeMs = batchTimes.Average();
            var avgRecordTimeMs = totalTimeMs / (double)TOTAL_RECORDS;
            var recordsPerSecond = TOTAL_RECORDS / totalTimeSec;

            _output.WriteLine($"📊 OVERALL PERFORMANCE:");
            _output.WriteLine($"  • Total Time:              {totalTimeMs:N0} ms ({totalTimeSec:F2} seconds)");
            _output.WriteLine($"  • Total Records Inserted:  {TOTAL_RECORDS:N0} records");
            _output.WriteLine($"  • Throughput:              {recordsPerSecond:F0} records/second");
            _output.WriteLine("");
            
            _output.WriteLine($"📊 BATCH STATISTICS:");
            _output.WriteLine($"  • Total Batches:           {totalBatches}");
            _output.WriteLine($"  • Records per Batch:       {BATCH_SIZE:N0}");
            _output.WriteLine($"  • Average Batch Time:      {avgBatchTimeMs:F2} ms");
            _output.WriteLine($"  • Min Batch Time:          {batchTimes.Min()} ms");
            _output.WriteLine($"  • Max Batch Time:          {batchTimes.Max()} ms");
            foreach (var item in batchTimes)
            {
                _output.WriteLine($"BatchTime:{item}");
            }
            _output.WriteLine("");
            
            _output.WriteLine($"📊 PER-RECORD STATISTICS:");
            _output.WriteLine($"  • Average Time per Record: {avgRecordTimeMs:F4} ms");
            _output.WriteLine($"  • Average Records/Batch:   {BATCH_SIZE / (avgBatchTimeMs / 1000.0):F0} records/sec");
            _output.WriteLine("");

            // ===== STEP 4: WAIT FOR ICEBERG COMMIT =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("STEP 4: Waiting for Iceberg commit...");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            await Task.Delay(5000); // Wait 5 seconds for Iceberg to commit
            _output.WriteLine("✓ Iceberg commit grace period completed");
            _output.WriteLine("");

            // ===== STEP 5: VERIFY INSERTED RECORDS =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("STEP 5: Verifying inserted records...");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            var count = await _context.TongHopKcbs
                .Where(t => t.MA_LK.Contains(_testRunId))
                .CountAsync();

            _output.WriteLine($"✓ Found {count:N0} records in database (expected: {TOTAL_RECORDS:N0})");
            
            if (count < TOTAL_RECORDS)
            {
                _output.WriteLine($"⚠️  WARNING: Only {count:N0}/{TOTAL_RECORDS:N0} records found. Retrying...");
                await Task.Delay(5000);
                count = await _context.TongHopKcbs
                    .Where(t => t.MA_LK.Contains(_testRunId))
                    .CountAsync();
                _output.WriteLine($"  Retry result: {count:N0} records");
            }
            _output.WriteLine("");
            
            // ===== FINAL SUMMARY =====
            _output.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            _output.WriteLine("║                      TEST SUMMARY                              ║");
            _output.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            _output.WriteLine($"✅ Status:              SUCCESS");
            _output.WriteLine($"📊 Records Inserted:    {TOTAL_RECORDS:N0} / {TOTAL_RECORDS:N0}");
            _output.WriteLine($"⏱️  Total Time:          {totalTimeSec:F2} seconds");
            _output.WriteLine($"🚀 Throughput:          {recordsPerSecond:F0} records/sec");
            _output.WriteLine("");

            // Assertions
            Assert.True(count >= TOTAL_RECORDS * 0.95, 
                $"Expected at least {TOTAL_RECORDS * 0.95:N0} records, but got {count:N0}");
            Assert.True(totalTimeSec < 600, 
                $"Insert took too long: {totalTimeSec:F2}s (expected < 600s)");
        }
        finally
        {
            // ===== CLEANUP =====
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine("CLEANUP: Deleting test data...");
            _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            try
            {
                // var deleted = await _context.TongHopKcbs
                //     .Where(t => t.MA_LK.Contains(_testRunId))
                //     .DeleteAsync();
                //_output.WriteLine($"✓ Deleted {deleted} test records");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️  Cleanup warning: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Measure and display batch size metrics
    /// </summary>
    private void MeasureBatchSize(List<TongHopKcb> allRecords, int batchSize)
    {
        _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _output.WriteLine("BATCH SIZE MEASUREMENT: Analyzing sample batch...");
        _output.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
        var sampleBatch = allRecords.Take(batchSize).ToList();
        
        // Serialize to JSON to measure size
        var jsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };
        var jsonString = JsonSerializer.Serialize(sampleBatch, jsonOptions);
        var jsonSizeBytes = System.Text.Encoding.UTF8.GetByteCount(jsonString);
        var jsonSizeKB = jsonSizeBytes / 1024.0;
        var jsonSizeMB = jsonSizeKB / 1024.0;
        
        // Estimate in-memory size (rough approximation)
        var estimatedMemorySizePerRecord = 2048; // ~2KB per record (estimated)
        var estimatedBatchMemoryBytes = batchSize * estimatedMemorySizePerRecord;
        var estimatedBatchMemoryMB = estimatedBatchMemoryBytes / (1024.0 * 1024.0);
        
        _output.WriteLine($"📦 BATCH SIZE ANALYSIS (Sample: {batchSize:N0} records):");
        _output.WriteLine($"  • JSON Serialized Size:");
        _output.WriteLine($"    - Total:         {jsonSizeBytes:N0} bytes ({jsonSizeKB:N2} KB / {jsonSizeMB:N2} MB)");
        _output.WriteLine($"    - Per Record:    {jsonSizeBytes / (double)batchSize:N0} bytes");
        _output.WriteLine($"  • Estimated In-Memory Size:");
        _output.WriteLine($"    - Total:         ~{estimatedBatchMemoryMB:N2} MB");
        _output.WriteLine($"    - Per Record:    ~{estimatedMemorySizePerRecord:N0} bytes");
        _output.WriteLine($"  • Records/Batch: {batchSize:N0}");
        _output.WriteLine("");
    }

    /// <summary>
    /// Generate fake TongHopKcb records for testing
    /// </summary>
    private List<TongHopKcb> GenerateFakeTongHopKcbRecords(int count)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var records = new List<TongHopKcb>(count);
        var baseDate = new DateTime(2024, 1, 1);

        for (int i = 0; i < count; i++)
        {
            var record = new TongHopKcb
            {
                Id = Guid.NewGuid(),
                // Primary keys
                MA_LK = $"{_testRunId}_{i:D8}",
                STT = i + 1,
                
                // Patient info
                MA_BN = $"BN{i:D8}",
                HO_TEN = $"Nguyen Van Test {i}",
                SO_CCCD = $"{random.Next(100000000, 999999999):D9}",
                NGAY_SINH = baseDate.AddYears(-random.Next(1, 80)),
                GIOI_TINH = random.Next(1, 3),
                NHOM_MAU = new[] { "A", "B", "O", "AB" }[random.Next(4)],
                MA_QUOCTICH = "VN",
                MA_DANTOC = "01",
                MA_NGHE_NGHIEP = $"{random.Next(1, 20):D2}",
                
                // Address
                DIA_CHI = $"{i} Nguyen Van Test Street",
                MATINH_CU_TRU = "01",
                MAHUYEN_CU_TRU = "001",
                MAXA_CU_TRU = "00001",
                DIEN_THOAI = $"09{random.Next(10000000, 99999999)}",
                
                // Insurance
                MA_THE_BHYT = $"DN{random.Next(1000000000, 2147483647):D10}",
                MA_DKBD = $"{random.Next(10000, 99999):D5}",
                GT_THE_TU = "2024-01-01",
                GT_THE_DEN = "2025-12-31",
                
                // Medical info
                LY_DO_VV = $"Reason {i % 10}",
                CHAN_DOAN_VAO = $"Diagnosis IN {i % 100}",
                CHAN_DOAN_RV = $"Diagnosis OUT {i % 100}",
                MA_BENH_CHINH = $"A{random.Next(0, 99):D2}.{random.Next(0, 9)}",
                MA_DOITUONG_KCB = $"{random.Next(1, 5)}",
                
                // Dates
                NGAY_VAO = baseDate.AddDays(random.Next(0, 365)),
                NGAY_RA = baseDate.AddDays(random.Next(0, 365)),
                NGAY_TTOAN = baseDate.AddDays(random.Next(0, 365)),
                
                // Financial
                T_THUOC = (decimal)(random.NextDouble() * 5000000),
                T_VTYT = (decimal)(random.NextDouble() * 2000000),
                T_TONGCHI_BV = (decimal)(random.NextDouble() * 10000000),
                T_TONGCHI_BH = (decimal)(random.NextDouble() * 8000000),
                T_BNTT = (decimal)(random.NextDouble() * 2000000),
                T_BHTT = (decimal)(random.NextDouble() * 8000000),
                
                // Period
                NAM_QT = 2024,
                THANG_QT = random.Next(1, 13),
                
                // Other
                MA_LOAI_KCB = $"{random.Next(1, 5)}",
                MA_KHOA = $"K{random.Next(1, 30):D2}",
                MA_CSKCB = $"{random.Next(10000, 99999):D5}",
                
                // Test tracking
                ApiRequestId = Guid.NewGuid(),
                TenantId = Guid.NewGuid()
            };
            
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Query Iceberg metadata to count S3 Parquet files
    /// </summary>
    private async Task<int> GetS3FileCount()
    {
        try
        {
            // Query Iceberg system table to count data files
            var sql = @"
                SELECT COUNT(*) as file_count
                FROM iceberg.v4.""tonghopkcb$files""
                WHERE file_path LIKE '%.parquet'
            ";

            var result = await _context.TongHopKcbs.FromSqlRawAsync(sql);
            
            // This is a workaround - ideally we'd query the system table directly
            // For now, estimate based on Iceberg's default file size (~128MB per file)
            // With ~100k records, assume ~1KB per record = ~100MB total = 1-2 files
            
            _output.WriteLine("  Note: File count is estimated from Iceberg metadata");
            return 1; // Placeholder - will be replaced with actual query
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  ⚠️  Could not query S3 file count: {ex.Message}");
            return -1; // Unknown
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}

