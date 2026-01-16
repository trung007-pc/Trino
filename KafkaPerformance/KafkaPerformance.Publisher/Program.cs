using Confluent.Kafka;
using KafkaPerformance.Shared.Data;
using KafkaPerformance.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace KafkaPerformance.Publisher;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("KAFKA PUBLISHER - API REQUEST SENDER");
        Console.WriteLine("========================================");

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var kafkaBootstrapServers = configuration["Kafka:BootstrapServers"];
        var kafkaTopic = configuration["Kafka:Topic"];
        var batchSize = int.Parse(configuration["Kafka:BatchSize"] ?? "100");
        var pollIntervalMs = int.Parse(configuration["Kafka:PollIntervalMs"] ?? "5000");

        Console.WriteLine($"Database: {connectionString?.Split(';').FirstOrDefault(s => s.Contains("Host"))}");
        Console.WriteLine($"Kafka: {kafkaBootstrapServers}");
        Console.WriteLine($"Topic: {kafkaTopic}");
        Console.WriteLine($"Batch Size: {batchSize}");
        Console.WriteLine($"Poll Interval: {pollIntervalMs}ms");
        Console.WriteLine("========================================\n");

        // Setup DbContext
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        // Setup Kafka Producer
        var config = new ProducerConfig
        {
            BootstrapServers = kafkaBootstrapServers,
            Acks = Acks.Leader,
            LingerMs = 10,
            BatchSize = 16384,
            CompressionType = CompressionType.Snappy
        };

        using var producer = new ProducerBuilder<string, string>(config).Build();

        Console.WriteLine("Publisher started. Press Ctrl+C to exit.\n");

        int totalSent = 0;
        var lastProcessedId = Guid.Empty;

        try
        {
            while (true)
            {
                using var dbContext = new AppDbContext(optionsBuilder.Options);

                // Query pending requests from database
                var pendingRequests = await dbContext.ApiRequests
                    .Where(r => r.Status == "Pending")
                    .OrderBy(r => r.CreatedAt)
                    .Take(batchSize)
                    .ToListAsync();

                if (pendingRequests.Any())
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found {pendingRequests.Count} pending requests");

                    int batchSent = 0;
                    foreach (var request in pendingRequests)
                    {
                        try
                        {
                            var message = JsonSerializer.Serialize(new
                            {
                                request.Id,
                                request.MaCSKCB,
                                request.Status,
                                request.CreatedAt
                            });

                            var result = await producer.ProduceAsync(kafkaTopic, new Message<string, string>
                            {
                                Key = request.Id.ToString(),
                                Value = message
                            });

                            // Update status to 'Sent'
                            request.Status = "Sent";
                            batchSent++;
                            lastProcessedId = request.Id;

                            Console.WriteLine($"  ✓ Sent: {request.Id} | MaCSKCB: {request.MaCSKCB} | Partition: {result.Partition.Value}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ✗ Error sending {request.Id}: {ex.Message}");
                        }
                    }

                    // Save changes to database
                    await dbContext.SaveChangesAsync();
                    totalSent += batchSent;

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch completed: {batchSent} sent | Total: {totalSent}\n");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No pending requests. Waiting...");
                }

                // Wait before next poll
                await Task.Delay(pollIntervalMs);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            producer.Flush(TimeSpan.FromSeconds(10));
            Console.WriteLine($"\nPublisher stopped. Total messages sent: {totalSent}");
        }
    }
}
