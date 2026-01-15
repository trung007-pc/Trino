// See https://aka.ms/new-console-template for more information

using ConsoleApp;
using System.Diagnostics;

Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           MULTI-THREADED PERFORMANCE TEST                  ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var tables = new List<string>() { "A", "B", "C","D","E","F","G" };

Console.WriteLine($"Testing {tables.Count} tables in PARALLEL...");
Console.WriteLine();

var sw = Stopwatch.StartNew();

// Chạy đa luồng với Task.Run
var tasks = tables.Select(tableName =>
    {
        App app = new App();
        
        return app.Run(tableName); 
    }
).ToArray();

// Chờ tất cả tasks hoàn thành
await Task.WhenAll(tasks);

sw.Stop();
Console.ReadKey();
Console.WriteLine("kết thúc");
