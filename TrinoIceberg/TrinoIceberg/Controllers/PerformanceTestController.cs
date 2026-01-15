using EntityFramework.Trino.Iceberg;
using Microsoft.AspNetCore.Mvc;
using kcb.KcbService.Entities.TongHopKcbs;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;

namespace TrinoIceberg.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformanceTestController : ControllerBase
{
    private readonly IcebergDbContext _context;
    private readonly ILogger<PerformanceTestController> _logger;

    public PerformanceTestController(IcebergDbContext context, ILogger<PerformanceTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Test insert 100k records vào nhiều bảng song song
    /// </summary>
    /// <param name="tableCount">Số lượng bảng (1-15). Mặc định = 1</param>
    /// <param name="recordsPerTable">Số records mỗi bảng. Mặc định = 100,000</param>
    /// <param name="batchSize">Batch size. Mặc định = 1000</param>
    [HttpPost("insert-parallel")]
    public async Task<ActionResult<PerformanceTestResult>> InsertParallel(
        [FromQuery] int tableCount = 1,
        [FromQuery] int recordsPerTable = 100_000,
        [FromQuery] int batchSize = 1000)
    {
        // Validate parameters
        if (tableCount < 1 || tableCount > 15)
        {
            return BadRequest(new { error = "tableCount must be between 1 and 15" });
        }

        if (recordsPerTable < 1000 || recordsPerTable > 1_000_000)
        {
            return BadRequest(new { error = "recordsPerTable must be between 1,000 and 1,000,000" });
        }

        if (batchSize < 100 || batchSize > 10_000)
        {
            return BadRequest(new { error = "batchSize must be between 100 and 10,000" });
        }

        var testRunId = $"PERF_{DateTime.Now:yyyyMMdd_HHmmss}";
        var totalInsertSw = Stopwatch.StartNew();

        _logger.LogInformation("═══════════════════════════════════════════════════════════");
        _logger.LogInformation("PARALLEL PERFORMANCE TEST STARTED");
        _logger.LogInformation($"Test Run ID: {testRunId}");
        _logger.LogInformation($"Table Count: {tableCount}");
        _logger.LogInformation($"Records per Table: {recordsPerTable:N0}");
        _logger.LogInformation($"Batch Size: {batchSize:N0}");
        _logger.LogInformation($"Total Records: {tableCount * recordsPerTable:N0}");
        _logger.LogInformation("═══════════════════════════════════════════════════════════");

        var tableResults = new ConcurrentBag<TablePerformanceResult>();

        try
        {
            // Generate table names
            var tableNames = GenerateTableNames(tableCount);
            
            // Generate sample data để đo size
            _logger.LogInformation("Generating sample data to measure batch size...");
            var sampleRecords = GenerateFakeTongHopKcbRecords(recordsPerTable, testRunId, "sample");
            var batchSizeInfo = MeasureBatchSize(sampleRecords, batchSize);
            
            _logger.LogInformation($"📊 Data Size: Total {batchSizeInfo.TotalDataSizeMB:N2} MB | " +
                                 $"Per Batch ({batchSize:N0} records): {batchSizeInfo.BatchDataSizeMB:N2} MB");
            
            // Chạy song song insert cho tất cả các bảng với Task.Run để tạo threads riêng biệt
            var tasks = tableNames.Select(tableName => 
                Task.Run(() => InsertToTableAsync(tableName, testRunId, recordsPerTable, batchSize, tableResults))
            ).ToArray();

            await Task.WhenAll(tasks);

            totalInsertSw.Stop();

            // Tính toán statistics
            var result = CalculateResults(tableResults, totalInsertSw.ElapsedMilliseconds, 
                tableCount, recordsPerTable, batchSizeInfo);

            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("PARALLEL PERFORMANCE TEST COMPLETED");
            _logger.LogInformation($"Total Time: {result.TotalTimeSeconds:F2} seconds");
            _logger.LogInformation($"Overall Throughput: {result.OverallThroughputRecordsPerSecond:N0} records/sec");
            _logger.LogInformation("═══════════════════════════════════════════════════════════");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during parallel performance test");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Insert 100k records vào 1 bảng cụ thể
    /// </summary>
    private async Task InsertToTableAsync(
        string tableName, 
        string testRunId, 
        int totalRecords, 
        int batchSize,
        ConcurrentBag<TablePerformanceResult> results)
    {
        var sw = Stopwatch.StartNew();
        var totalBatches = totalRecords / batchSize;
        var batchTimes = new List<long>();

        _logger.LogInformation($"[{tableName}] Starting insert of {totalRecords:N0} records...");

        try
        {
            // Generate fake data
            var allRecords = GenerateFakeTongHopKcbRecords(totalRecords, testRunId, tableName);

            // Insert theo batch
            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                var batch = allRecords.Skip(batchIndex * batchSize).Take(batchSize).ToList();
                
                var batchSw = Stopwatch.StartNew();
                
                // Insert vào bảng tương ứng
                await InsertBatchToTable(tableName, batch);
                
                batchSw.Stop();
                batchTimes.Add(batchSw.ElapsedMilliseconds);

                // Log progress mỗi 10 batches
                if ((batchIndex + 1) % 10 == 0 || batchIndex == totalBatches - 1)
                {
                    var progress = (batchIndex + 1) * 100.0 / totalBatches;
                    var overallAvg = batchTimes.Average();
                    var last10Batches = batchTimes.Skip(Math.Max(0, batchTimes.Count - 10)).ToList();
                    var last10Avg = last10Batches.Average();
                    
                    _logger.LogInformation(
                        $"[{tableName}] [{batchIndex + 1,3}/{totalBatches}] Progress: {progress:F1}% | " +
                        $"Last 10 avg: {last10Avg:F0}ms | Overall avg: {overallAvg:F0}ms");
                }
            }

            sw.Stop();

            var result = new TablePerformanceResult
            {
                TableName = tableName,
                TotalRecords = totalRecords,
                TotalTimeMs = sw.ElapsedMilliseconds,
                TotalTimeSeconds = Math.Round(sw.ElapsedMilliseconds / 1000.0, 2),
                AvgBatchTimeMs = Math.Round(batchTimes.Average(), 2),
                MinBatchTimeMs = batchTimes.Min(),
                MaxBatchTimeMs = batchTimes.Max(),
                ThroughputRecordsPerSecond = Math.Round(totalRecords / (sw.ElapsedMilliseconds / 1000.0), 2),
                Success = true
            };

            results.Add(result);
            
            _logger.LogInformation(
                $"[{tableName}] ✅ Completed in {result.TotalTimeSeconds:F2}s | " +
                $"Throughput: {result.ThroughputRecordsPerSecond:N0} records/sec");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, $"[{tableName}] ❌ Error during insert");
            
            results.Add(new TablePerformanceResult
            {
                TableName = tableName,
                Success = false,
                ErrorMessage = ex.Message,
                TotalTimeMs = sw.ElapsedMilliseconds,
                TotalTimeSeconds = Math.Round(sw.ElapsedMilliseconds / 1000.0, 2)
            });
        }
    }

    /// <summary>
    /// Insert batch vào bảng cụ thể
    /// </summary>
    private async Task InsertBatchToTable(string tableName, List<TongHopKcb> batch)
    {
        // Get DbSet cho bảng tương ứng
        var dbSet = GetDbSetForTable(tableName);
        await dbSet.AddRangeV1Async(batch);
    }

    /// <summary>
    /// Lấy DbSet cho table name tương ứng
    /// </summary>
    private dynamic GetDbSetForTable(string tableName)
    {
        // Sử dụng reflection hoặc switch case để get DbSet
        return tableName.ToLower() switch
        {
            "tonghopkcb" => _context.TongHopKcbs,
            "tonghopkcb1" => _context.Set<TongHopKcb>("tonghopkcb1"),
            "tonghopkcb2" => _context.Set<TongHopKcb>("tonghopkcb2"),
            "tonghopkcb3" => _context.Set<TongHopKcb>("tonghopkcb3"),
            "tonghopkcb4" => _context.Set<TongHopKcb>("tonghopkcb4"),
            "tonghopkcb5" => _context.Set<TongHopKcb>("tonghopkcb5"),
            "tonghopkcb6" => _context.Set<TongHopKcb>("tonghopkcb6"),
            "tonghopkcb7" => _context.Set<TongHopKcb>("tonghopkcb7"),
            "tonghopkcb8" => _context.Set<TongHopKcb>("tonghopkcb8"),
            "tonghopkcb9" => _context.Set<TongHopKcb>("tonghopkcb9"),
            "tonghopkcb10" => _context.Set<TongHopKcb>("tonghopkcb10"),
            "tonghopkcb11" => _context.Set<TongHopKcb>("tonghopkcb11"),
            "tonghopkcb12" => _context.Set<TongHopKcb>("tonghopkcb12"),
            "tonghopkcb13" => _context.Set<TongHopKcb>("tonghopkcb13"),
            "tonghopkcb14" => _context.Set<TongHopKcb>("tonghopkcb14"),
            "tonghopkcb15" => _context.Set<TongHopKcb>("tonghopkcb15"),
            _ => _context.TongHopKcbs
        };
    }

    /// <summary>
    /// Generate table names dựa trên count
    /// </summary>
    private List<string> GenerateTableNames(int count)
    {
        var tables = new List<string>();
        
        if (count >= 1)
            tables.Add("tonghopkcb");
        
        for (int i = 1; i < count; i++)
        {
            tables.Add($"tonghopkcb{i}");
        }
        
        return tables;
    }

    /// <summary>
    /// Generate fake records
    /// </summary>
    private List<TongHopKcb> GenerateFakeTongHopKcbRecords(int count, string testRunId, string tableName)
    {
        var random = new Random(42 + tableName.GetHashCode()); // Different seed per table
        var records = new List<TongHopKcb>(count);
        var baseDate = new DateTime(2024, 1, 1);

        for (int i = 0; i < count; i++)
        {
            var record = new TongHopKcb
            {
                Id = Guid.NewGuid(),
                // Primary keys
                MA_LK = $"{testRunId}_{tableName}_{i:D8}",
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
    /// Đo khối lượng của batch dữ liệu
    /// </summary>
    private BatchSizeInfo MeasureBatchSize(List<TongHopKcb> allRecords, int batchSize)
    {
        // Đo tổng tất cả records
        var totalJson = JsonSerializer.Serialize(allRecords);
        var totalBytes = System.Text.Encoding.UTF8.GetByteCount(totalJson);
        var totalMB = totalBytes / (1024.0 * 1024.0);
        
        // Đo 1 batch
        var oneBatch = allRecords.Take(batchSize).ToList();
        var batchJson = JsonSerializer.Serialize(oneBatch);
        var batchBytes = System.Text.Encoding.UTF8.GetByteCount(batchJson);
        var batchMB = batchBytes / (1024.0 * 1024.0);
        
        return new BatchSizeInfo
        {
            TotalRecords = allRecords.Count,
            BatchSize = batchSize,
            TotalDataSizeMB = Math.Round(totalMB, 2),
            BatchDataSizeMB = Math.Round(batchMB, 2),
            TotalDataSizeBytes = totalBytes,
            BatchDataSizeBytes = batchBytes
        };
    }

    /// <summary>
    /// Tính toán kết quả tổng hợp
    /// </summary>
    private PerformanceTestResult CalculateResults(
        ConcurrentBag<TablePerformanceResult> tableResults,
        long totalTimeMs,
        int tableCount,
        int recordsPerTable,
        BatchSizeInfo batchSizeInfo)
    {
        var successfulTables = tableResults.Where(r => r.Success).ToList();
        var totalRecords = tableCount * recordsPerTable;

        return new PerformanceTestResult
        {
            TestTimestamp = DateTime.UtcNow,
            TableCount = tableCount,
            RecordsPerTable = recordsPerTable,
            TotalRecords = totalRecords,
            TotalTimeMs = totalTimeMs,
            TotalTimeSeconds = Math.Round(totalTimeMs / 1000.0, 2),
            SuccessfulTables = successfulTables.Count,
            FailedTables = tableCount - successfulTables.Count,
            OverallThroughputRecordsPerSecond = Math.Round(totalRecords / (totalTimeMs / 1000.0), 2),
            AverageThroughputPerTable = successfulTables.Any() 
                ? Math.Round(successfulTables.Average(t => t.ThroughputRecordsPerSecond), 2)
                : 0,
            FastestTable = successfulTables.OrderByDescending(t => t.ThroughputRecordsPerSecond).FirstOrDefault()?.TableName,
            SlowestTable = successfulTables.OrderBy(t => t.ThroughputRecordsPerSecond).FirstOrDefault()?.TableName,
            
            // Data size info
            TotalDataSizeMB = batchSizeInfo.TotalDataSizeMB,
            BatchDataSizeMB = batchSizeInfo.BatchDataSizeMB,
            TableResults = tableResults.OrderBy(t => t.TableName).ToList()
        };
    }
}

#region Result Models

public class PerformanceTestResult
{
    public DateTime TestTimestamp { get; set; }
    public int TableCount { get; set; }
    public int RecordsPerTable { get; set; }
    public int TotalRecords { get; set; }
    public long TotalTimeMs { get; set; }
    public double TotalTimeSeconds { get; set; }
    public int SuccessfulTables { get; set; }
    public int FailedTables { get; set; }
    public double OverallThroughputRecordsPerSecond { get; set; }
    public double AverageThroughputPerTable { get; set; }
    public string? FastestTable { get; set; }
    public string? SlowestTable { get; set; }
    
    // Data size metrics
    public double TotalDataSizeMB { get; set; }
    public double BatchDataSizeMB { get; set; }
    
    public List<TablePerformanceResult> TableResults { get; set; } = new();
}

public class TablePerformanceResult
{
    public string TableName { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public long TotalTimeMs { get; set; }
    public double TotalTimeSeconds { get; set; }
    public double AvgBatchTimeMs { get; set; }
    public long MinBatchTimeMs { get; set; }
    public long MaxBatchTimeMs { get; set; }
    public double ThroughputRecordsPerSecond { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BatchSizeInfo
{
    public int TotalRecords { get; set; }
    public int BatchSize { get; set; }
    public double TotalDataSizeMB { get; set; }
    public double BatchDataSizeMB { get; set; }
    public long TotalDataSizeBytes { get; set; }
    public long BatchDataSizeBytes { get; set; }
}

#endregion
