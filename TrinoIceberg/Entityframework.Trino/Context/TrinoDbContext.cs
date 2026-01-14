using Trino.Data.ADO.Server;
using Trino.Data.ADO.Client;
using EntityFramework.Trino.Core;

namespace EntityFramework.Trino.Context;

/// <summary>
/// Base class cho Trino DbContext - giống DbContext của Entity Framework
/// </summary>
public abstract class TrinoDbContext : IDisposable, IAsyncDisposable
{
    protected TrinoConnection Connection { get; private set; }
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
    /// Mở connection - giống Database.OpenConnection() trong EF
    /// </summary>
    public void OpenConnection()
    {
        if (Connection.State != System.Data.ConnectionState.Open)
        {
            Connection.Open();
        }
    }

    /// <summary>
    /// Execute raw SQL command - giống Database.ExecuteSqlRaw() trong EF
    /// </summary>
    public async Task<int> ExecuteSqlRawAsync(string sql)
    {
        OpenConnection();
        LogSql(sql);
        using var command = new TrinoCommand(Connection, sql);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Execute scalar query - lấy 1 giá trị đơn
    /// </summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql)
    {
        OpenConnection();
        using var command = new TrinoCommand(Connection, sql);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync() && !reader.IsDBNull(0))
        {
            var value = reader.GetValue(0);
            return (T?)Convert.ChangeType(value, typeof(T));
        }

        return default;
    }

    /// <summary>
    /// Create a fluent query builder with WhereIf support for dynamic queries
    /// </summary>
    /// <example>
    /// var results = await context.Query&lt;CustomerOrderDTO&gt;(
    ///     @"SELECT c.Name AS CustomerName, o.Amount
    ///       FROM customers c
    ///       INNER JOIN orders o ON c.Id = o.CustomerId")
    ///     .WithParams(new { name = $"%{searchName}%", minAmount = 100 })
    ///     .WhereIf(!string.IsNullOrEmpty(searchName), "c.Name LIKE @name")
    ///     .WhereIf(minAmount.HasValue, "o.Amount >= @minAmount")
    ///     .OrderBy("o.Amount DESC")
    ///     .ExecuteAsync();
    /// </example>
    public SqlQueryBuilder<T> Query<T>(string baseSql) where T : class, new()
    {
        return new SqlQueryBuilder<T>(Connection, baseSql, _enableSqlLogging);
    }

    /// <summary>
    /// Create fluent query builder - Type được infer từ Select()
    /// </summary>
    /// <example>
    /// // Type tự động infer từ Select()
    /// var results = await context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId")
    ///     .WithParams(new { id = customerId })
    ///     .Select((Customer c, Order o) => new CustomerWithOrders { 
    ///         Customer = c, 
    ///         Order = o 
    ///     })
    ///     .ExecuteAsync();
    /// </example>
    public FluentQueryBuilder Query(string fromClause)
    {
        return new FluentQueryBuilder(Connection, fromClause, _enableSqlLogging);
    }

    /// <summary>
    /// Execute raw SQL query with parameters and map to list of objects
    /// Safe from SQL injection - uses parameterized queries
    /// </summary>
    /// <example>
    /// var results = await context.ExecuteQueryAsync&lt;CustomerOrderDTO&gt;(
    ///     @"SELECT c.Name AS CustomerName, o.Id AS OrderId, o.Amount
    ///       FROM customers c
    ///       INNER JOIN orders o ON c.Id = o.CustomerId
    ///       WHERE c.Name = @name AND o.Amount > @minAmount",
    ///     new { name = "John", minAmount = 100 }
    /// );
    /// </example>
    public async Task<List<T>> ExecuteQueryAsync<T>(string sql, object? parameters = null) where T : class, new()
    {
        OpenConnection();
        
        // Replace parameters in SQL (safe from injection)
        var safeSql = ReplaceParameters(sql, parameters);
        LogSql(safeSql);
        
        using var command = new TrinoCommand(Connection, safeSql);
        using var reader = (TrinoDataReader)await command.ExecuteReaderAsync();
        
        var results = new List<T>();
        while (await reader.ReadAsync())
        {
            results.Add(MapToObject<T>(reader));
        }
        
        return results;
    }

    /// <summary>
    /// Execute raw SQL query and return first result or null
    /// </summary>
    public async Task<T?> ExecuteQueryFirstOrDefaultAsync<T>(string sql, object? parameters = null) where T : class, new()
    {
        var results = await ExecuteQueryAsync<T>(sql, parameters);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Execute raw SQL query and return single result (throws if not exactly one)
    /// </summary>
    public async Task<T> ExecuteQuerySingleAsync<T>(string sql, object? parameters = null) where T : class, new()
    {
        var results = await ExecuteQueryAsync<T>(sql, parameters);
        if (results.Count == 0)
            throw new InvalidOperationException("Sequence contains no elements");
        if (results.Count > 1)
            throw new InvalidOperationException("Sequence contains more than one element");
        return results[0];
    }

    /// <summary>
    /// Replace @parameters in SQL with safe escaped values
    /// </summary>
    private string ReplaceParameters(string sql, object? parameters)
    {
        if (parameters == null)
            return sql;

        var type = parameters.GetType();
        var properties = type.GetProperties();

        foreach (var prop in properties)
        {
            var value = prop.GetValue(parameters);
            var safeValue = EscapeValue(value);
            sql = sql.Replace($"@{prop.Name}", safeValue);
        }

        return sql;
    }

    /// <summary>
    /// Escape value to prevent SQL injection
    /// </summary>
    private string EscapeValue(object? value)
    {
        if (value == null)
            return "NULL";

        if (value is string str)
            return $"'{str.Replace("'", "''")}'"; // Escape single quotes

        if (value is DateTime dt)
            return $"TIMESTAMP '{dt:yyyy-MM-dd HH:mm:ss.fff}'";

        if (value is bool b)
            return b ? "TRUE" : "FALSE";

        if (value is decimal || value is double || value is float)
            return Convert.ToDecimal(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (value is int || value is long || value is short)
            return value.ToString()!;

        // For other types, convert to string and escape
        return $"'{value.ToString()!.Replace("'", "''")}'";
    }

    /// <summary>
    /// Log SQL to console if EnableSqlLogging is true
    /// </summary>
    private void LogSql(string sql)
    {
        if (_enableSqlLogging)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Trino SQL] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            Console.WriteLine(sql);
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Map DataReader row to object
    /// </summary>
    private T MapToObject<T>(TrinoDataReader reader) where T : class, new()
    {
        var obj = new T();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            try
            {
                // Try to get column by property name
                object? value = null;
                try
                {
                    value = reader[prop.Name];
                }
                catch
                {
                    // Try lowercase
                    try
                    {
                        value = reader[prop.Name.ToLower()];
                    }
                    catch
                    {
                        continue; // Column not found
                    }
                }

                if (value != null && value != DBNull.Value)
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    
                    // Type conversion
                    if (targetType == typeof(decimal))
                    {
                        // Handle Trino BigDecimal type
                        if (value.GetType().Name == "TrinoBigDecimal")
                        {
                            prop.SetValue(obj, decimal.Parse(value.ToString()!));
                        }
                        else if (value is double dbl)
                        {
                            prop.SetValue(obj, Convert.ToDecimal(dbl));
                        }
                        else
                        {
                            prop.SetValue(obj, Convert.ToDecimal(value));
                        }
                    }
                    else if (targetType == typeof(DateTime) && value is string dateStr)
                    {
                        prop.SetValue(obj, DateTime.Parse(dateStr));
                    }
                    else if (targetType == typeof(int) && value is long lng)
                    {
                        prop.SetValue(obj, Convert.ToInt32(lng));
                    }
                    else
                    {
                        prop.SetValue(obj, Convert.ChangeType(value, targetType));
                    }
                }
            }
            catch
            {
                // Ignore mapping errors for individual properties
            }
        }

        return obj;
    }

    /// <summary>
    /// Kiểm tra connection có mở không
    /// </summary>
    public bool IsOpen => Connection.State == System.Data.ConnectionState.Open;

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
