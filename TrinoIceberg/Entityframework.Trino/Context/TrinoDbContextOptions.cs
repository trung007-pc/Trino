using System;

namespace EntityFramework.Trino.Context;

/// <summary>
/// Options cho TrinoDbContext - giống DbContextOptions trong EF
/// </summary>
public class TrinoDbContextOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8081;
    public string Catalog { get; set; } = "iceberg";
    public string Schema { get; set; } = "default";
    public string User { get; set; } = "admin";
    public bool EnableSqlLogging { get; set; } = false;

    internal string GetConnectionString()
    {
        return $"Host={Host};Port={Port};Catalog={Catalog};Schema={Schema};User={User}";
    }
}

/// <summary>
/// Generic options for typed DbContext
/// </summary>
public class TrinoDbContextOptions<TContext> : TrinoDbContextOptions where TContext : TrinoDbContext
{
}
