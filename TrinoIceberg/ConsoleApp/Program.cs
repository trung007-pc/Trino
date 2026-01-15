using ConsoleApp;
using EntityFramework.Trino.Iceberg;
using kcb.KcbService.Entities.TongHopKcbs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Trino.Data.ADO.Server;

// Parse command line arguments
if (args.Length == 0)
{
    Console.WriteLine("Usage: ConsoleApp <tableName> [recordsCount] [batchSize]");
    Console.WriteLine("Example: ConsoleApp tonghopkcb1 100000 1000");
    Console.WriteLine("Available tables: tonghopkcb, tonghopkcb1-tonghopkcb15");
    return 1;
}

var tableName = args[0].ToLower();
var recordsCount = args.Length > 1 ? int.Parse(args[1]) : 100_000;
var batchSize = args.Length > 2 ? int.Parse(args[2]) : 1000;

// Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

// Setup logging with configuration
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConfiguration(configuration.GetSection("Logging"))
        .AddConsole()
        .AddFilter("Trino", LogLevel.Warning)
        .AddFilter("EntityFramework", LogLevel.Warning)
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddFilter("System", LogLevel.Warning);
});

var logger = loggerFactory.CreateLogger<Program>();

// Create Trino connection
var trinoConfig = configuration.GetSection("Trino");
var connectionProperties = new TrinoConnectionProperties
{
    Host = trinoConfig["Host"] ?? "localhost",
    Port = int.Parse(trinoConfig["Port"] ?? "8081"),
    Catalog = trinoConfig["Catalog"] ?? "iceberg",
    Schema = trinoConfig["Schema"] ?? "v1",
    User = trinoConfig["User"] ?? "admin",
    EnableSsl = bool.Parse(trinoConfig["EnableSsl"] ?? "false")
};

logger.LogInformation("═══════════════════════════════════════════════════════════");
logger.LogInformation("PERFORMANCE TEST - SINGLE TABLE INSERT");
logger.LogInformation("═══════════════════════════════════════════════════════════");
logger.LogInformation("Table Name: {TableName}", tableName);
logger.LogInformation("Records Count: {RecordsCount:N0}", recordsCount);
logger.LogInformation("Batch Size: {BatchSize:N0}", batchSize);
logger.LogInformation("Total Batches: {TotalBatches}", recordsCount / batchSize);
logger.LogInformation("Trino: {Host}:{Port} / {Catalog}.{Schema}", 
    connectionProperties.Host, connectionProperties.Port, 
    connectionProperties.Catalog, connectionProperties.Schema);
logger.LogInformation("═══════════════════════════════════════════════════════════");

try
{
    using var dbContext = new IcebergDbContext(connectionProperties);
    var inserter = new TableInserter(dbContext, logger);
    
    var result = await inserter.InsertToTableAsync(tableName, recordsCount, batchSize);
    
    logger.LogInformation("═══════════════════════════════════════════════════════════");
    logger.LogInformation("COMPLETED SUCCESSFULLY");
    logger.LogInformation("═══════════════════════════════════════════════════════════");
    logger.LogInformation("Total Time: {TotalTime:F2}s", result.TotalTimeSeconds);
    logger.LogInformation("Total Records: {TotalRecords:N0}", result.TotalRecords);
    logger.LogInformation("Throughput: {Throughput:N0} records/sec", result.ThroughputRecordsPerSecond);
    logger.LogInformation("Avg Batch Time: {AvgBatchTime:F2}ms", result.AvgBatchTimeMs);
    logger.LogInformation("Min Batch Time: {MinBatchTime}ms", result.MinBatchTimeMs);
    logger.LogInformation("Max Batch Time: {MaxBatchTime}ms", result.MaxBatchTimeMs);
    logger.LogInformation("═══════════════════════════════════════════════════════════");
    
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "ERROR during insert operation");
    return 1;
}
