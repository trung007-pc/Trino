using EntityFramework.Trino.Iceberg;
using kcb.KcbService.Entities.TongHopKcbs;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ConsoleApp;

public class TableInserter
{
    private readonly IcebergDbContext _context;
    private readonly ILogger _logger;
    private readonly Random _random;

    public TableInserter(IcebergDbContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _random = new Random(42);
    }

    public async Task<InsertResult> InsertToTableAsync(string tableName, int totalRecords, int batchSize)
    {
        var sw = Stopwatch.StartNew();
        var totalBatches = totalRecords / batchSize;
        var batchTimes = new List<long>();
        var testRunId = $"PERF_{DateTime.Now:yyyyMMdd_HHmmss}";

        _logger.LogInformation("[{TableName}] Starting insert of {TotalRecords:N0} records...", tableName, totalRecords);

        try
        {
            // Generate all fake data
            _logger.LogInformation("[{TableName}] Generating {TotalRecords:N0} fake records...", tableName, totalRecords);
            var allRecords = GenerateFakeTongHopKcbRecords(totalRecords, testRunId, tableName);
            _logger.LogInformation("[{TableName}] Data generation completed", tableName);

            // Get DbSet for the target table
            var dbSet = GetDbSetForTable(tableName);

            // Insert in batches
            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                var batch = allRecords.Skip(batchIndex * batchSize).Take(batchSize).ToList();

                var batchSw = Stopwatch.StartNew();
                await dbSet.AddRangeV1Async(batch);
                batchSw.Stop();

                batchTimes.Add(batchSw.ElapsedMilliseconds);

                // Log progress every 10 batches or at the end
                if ((batchIndex + 1) % 10 == 0 || batchIndex == totalBatches - 1)
                {
                    var progress = (batchIndex + 1) * 100.0 / totalBatches;
                    var overallAvg = batchTimes.Average();
                    var last10Batches = batchTimes.Skip(Math.Max(0, batchTimes.Count - 10)).ToList();
                    var last10Avg = last10Batches.Average();

                    _logger.LogInformation(
                        "[{TableName}] [{Current,3}/{Total}] Progress: {Progress:F1}% | " +
                        "Last 10 avg: {Last10Avg:F0}ms | Overall avg: {OverallAvg:F0}ms",
                        tableName, batchIndex + 1, totalBatches, progress, last10Avg, overallAvg);
                }
            }

            sw.Stop();

            return new InsertResult
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
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[{TableName}] Error during insert", tableName);
            throw;
        }
    }

    private dynamic GetDbSetForTable(string tableName)
    {
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
            _ => throw new ArgumentException($"Unknown table name: {tableName}")
        };
    }

    private List<TongHopKcb> GenerateFakeTongHopKcbRecords(int count, string testRunId, string tableName)
    {
        var random = new Random(42 + tableName.GetHashCode());
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
}

public class InsertResult
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

public class App
{
    public static bool Go(Func<bool> func)
    {
        return func.Invoke();
    }
}
