using Confluent.Kafka;
using KafkaPerformance.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace KafkaPerformance.Consumer;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("KAFKA CONSUMER - API REQUEST PROCESSOR");
        Console.WriteLine("========================================");

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var kafkaBootstrapServers = configuration["Kafka:BootstrapServers"];
        var kafkaTopic = configuration["Kafka:Topic"];
        var kafkaGroupId = configuration["Kafka:GroupId"];
        var autoOffsetReset = configuration["Kafka:AutoOffsetReset"];

        Console.WriteLine($"Database: {connectionString?.Split(';').FirstOrDefault(s => s.Contains("Host"))}");
        Console.WriteLine($"Kafka: {kafkaBootstrapServers}");
        Console.WriteLine($"Topic: {kafkaTopic}");
        Console.WriteLine($"Group ID: {kafkaGroupId}");
        Console.WriteLine("========================================\n");

        // Setup DbContext
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        // Setup Kafka Consumer
        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaBootstrapServers,
            GroupId = kafkaGroupId,
            AutoOffsetReset = autoOffsetReset == "Earliest" ? AutoOffsetReset.Earliest : AutoOffsetReset.Latest,
            EnableAutoCommit = false,
            SessionTimeoutMs = 6000,
            MaxPollIntervalMs = 300000
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(kafkaTopic);

        Console.WriteLine("Consumer started. Press Ctrl+C to exit.\n");

        int totalProcessed = 0;
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));

                    if (consumeResult != null)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received message:");
                        Console.WriteLine($"  Partition: {consumeResult.Partition.Value}");
                        Console.WriteLine($"  Offset: {consumeResult.Offset.Value}");
                        Console.WriteLine($"  Key: {consumeResult.Message.Key}");

                        try
                        {
                            var messageData = JsonSerializer.Deserialize<JsonElement>(consumeResult.Message.Value);
                            var id = Guid.Parse(messageData.GetProperty("Id").GetString()!);
                            var maCSKCB = messageData.GetProperty("MaCSKCB").GetString()!;

                            Console.WriteLine($"  ID: {id}");
                            Console.WriteLine($"  MaCSKCB: {maCSKCB}");

                            // Process message - update status in database
                            using var dbContext = new AppDbContext(optionsBuilder.Options);
                            var apiRequest = await dbContext.ApiRequests.FindAsync(id);

                            if (apiRequest != null)
                            {
                                apiRequest.Status = "Processed";
                                await dbContext.SaveChangesAsync();

                                totalProcessed++;
                                Console.WriteLine($"  ✓ Status updated to 'Processed' | Total: {totalProcessed}");

                                // Commit offset
                                consumer.Commit(consumeResult);
                            }
                            else
                            {
                                Console.WriteLine($"  ⚠ Warning: Record not found in database");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ✗ Error processing message: {ex.Message}");
                        }

                        Console.WriteLine();
                    }
                }
                catch (ConsumeException ex)
                {
                    Console.WriteLine($"Consume error: {ex.Error.Reason}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            consumer.Close();
            Console.WriteLine($"\nConsumer stopped. Total messages processed: {totalProcessed}");
        }
    }
}
