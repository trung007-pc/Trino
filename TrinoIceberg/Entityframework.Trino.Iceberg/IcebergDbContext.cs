using Trino.Data.ADO.Server;
using EntityFramework.Trino.Context;
using EntityFramework.Trino.Core;
using EntityFramework.Trino.Iceberg.Models;
using kcb.KcbService.Entities.TongHopKcbs;

namespace EntityFramework.Trino.Iceberg;

/// <summary>
/// Application DbContext for Iceberg/Trino database
/// Shared library để dùng cho cả Tests và Web API projects
/// </summary>
public class IcebergDbContext : TrinoDbContext
{
    // DbSets - giống như DbSet<T> trong Entity Framework
    public TrinoDbSet<Customer> Customers { get; private set; }
    public TrinoDbSet<Order> Orders { get; private set; }
    public TrinoDbSet<OrderItem> OrderItems { get; private set; }
    public TrinoDbSet<TongHopKcb> TongHopKcbs { get; private set; }

    public IcebergDbContext(TrinoConnectionProperties properties) : base(properties)
    {
        // Khởi tạo DbSets với table names
        Customers = Set<Customer>("customers");
        Orders = Set<Order>("orders");
        OrderItems = Set<OrderItem>("order_items");
        TongHopKcbs = Set<TongHopKcb>("tonghopkcb");
    }

    /// <summary>
    /// Helper method để tạo DbContext với connection info
    /// </summary>
    public static IcebergDbContext Create(string host = "localhost", int port = 8081, 
        string catalog = "iceberg", string schema = "v4", string user = "trino")
    {
        return new IcebergDbContext(new TrinoConnectionProperties
        {
            Host = host,
            Port = port,
            Catalog = catalog,
            Schema = schema,
            User = user,
            EnableSsl = false
        });
    }
}
