using Trino.Data.ADO.Server;
using Trino.Data.ADO.Client;
using EntityFramework.Trino.Core;

namespace EntityFramework.Trino.Context;

/// <summary>
/// Base class cho Trino DbContext - giống DbContext của Entity Framework
/// </summary>
public abstract class TrinoDbContext : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Internal connection for Dapper integration - accessible for extension methods
    /// </summary>
    internal TrinoConnection Connection { get; private set; }
    private bool _disposed;
    private readonly bool _enableSqlLogging = true; // Mặc định luôn log

    protected TrinoDbContext(TrinoConnectionProperties properties)
    {
        Connection = new TrinoConnection(properties);
        // EnableSqlLogging không có trong official library, mặc định luôn log
    }

    /// <summary>
    /// Helper method để tạo DbSet cho một entity
    /// </summary>
    protected TrinoDbSet<T> Set<T>(string tableName) where T : class, new()
    {
        return new TrinoDbSet<T>(Connection, tableName);
    }
    
    /// <summary>
    /// Get connection string info
    /// </summary>
    public string ConnectionString => Connection.ConnectionString;

    /// <summary>
    /// Get connection information for display
    /// </summary>
    public string GetConnectionInfo()
    {
        //var props = Connection.Properties;
        //return $"{props.Host}:{props.Port} | Catalog={props.Catalog}, Schema={props.Schema}, User={props.User}";

        return null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            Connection?.Dispose();
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (Connection != null)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Builder để tạo TrinoConnectionProperties một cách dễ dàng
/// </summary>
public class TrinoDbContextOptionsBuilder
{
    private readonly TrinoConnectionProperties _properties = new();

    public TrinoDbContextOptionsBuilder UseTrino(
        string host,
        int port = 8080,
        string catalog = "iceberg",
        string schema = "v3",
        string user = "admin",
        bool useSsl = false)
    {
        _properties.Host = host;
        _properties.Port = port;
        _properties.Catalog = catalog;
        _properties.Schema = schema;
        _properties.User = user;
        _properties.EnableSsl = useSsl;
        return this;
    }

    public TrinoDbContextOptionsBuilder UseServer(Uri serverUri, string catalog, string schema, string user = "admin")
    {
        var useSsl = serverUri.Scheme == "https";
        _properties.Host = serverUri.Host;
        _properties.Port = serverUri.Port > 0 ? serverUri.Port : (useSsl ? 443 : 8080);
        _properties.EnableSsl = useSsl;
        _properties.Catalog = catalog;
        _properties.Schema = schema;
        _properties.User = user;
        return this;
    }

    public TrinoDbContextOptionsBuilder WithTestConnection(bool testConnection = true)
    {
        _properties.TestConnection = testConnection;
        return this;
    }

    public TrinoDbContextOptionsBuilder WithSessionProperty(string key, string value)
    {
        _properties.SessionProperties ??= new Dictionary<string, string>();
        _properties.SessionProperties[key] = value;
        return this;
    }

    public TrinoConnectionProperties Build()
    {
        return _properties;
    }
}
