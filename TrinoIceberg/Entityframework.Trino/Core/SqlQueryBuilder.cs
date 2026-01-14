using System.Globalization;
using System.Text;
using Trino.Data.ADO.Client;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Core;

/// <summary>
/// Fluent SQL Query Builder with WhereIf support for dynamic queries
/// </summary>
public class SqlQueryBuilder<T> where T : class, new()
{
    private readonly TrinoConnection _connection;
    private readonly string _baseSql;
    private readonly List<WhereCondition> _whereConditions = new();
    private readonly Dictionary<string, object?> _parameters = new();
    private readonly bool _enableSqlLogging;
    private string? _orderBy;
    private int? _limit;
    private int? _offset;

    internal SqlQueryBuilder(TrinoConnection connection, string baseSql, bool enableSqlLogging = false)
    {
        _connection = connection;
        _baseSql = baseSql;
        _enableSqlLogging = enableSqlLogging;
    }

    /// <summary>
    /// Set parameters from anonymous object or dictionary
    /// </summary>
    /// <example>
    /// .WithParams(new { name = "John", age = 30 })
    /// .WithParams(new Dictionary&lt;string, object&gt; { ["name"] = "John" })
    /// </example>
    public SqlQueryBuilder<T> WithParams(object parameters)
    {
        if (parameters == null) return this;

        // Handle Dictionary<string, object?>
        if (parameters is IDictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                _parameters[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            // Handle anonymous object
            var properties = parameters.GetType().GetProperties();
            foreach (var prop in properties)
            {
                _parameters[prop.Name] = prop.GetValue(parameters);
            }
        }
        return this;
    }

    /// <summary>
    /// Conditionally add WHERE clause - chỉ add nếu condition = true
    /// </summary>
    /// <example>
    /// .WithParams(new { name = "John" })
    /// .WhereIf(!string.IsNullOrEmpty(name), "c.Name LIKE @name")
    /// </example>
    public SqlQueryBuilder<T> WhereIf(bool condition, string whereClause)
    {
        if (condition)
        {
            _whereConditions.Add(new WhereCondition { Clause = whereClause });
        }
        return this;
    }

    /// <summary>
    /// Add WHERE clause unconditionally
    /// </summary>
    public SqlQueryBuilder<T> Where(string whereClause)
    {
        return WhereIf(true, whereClause);
    }

    /// <summary>
    /// Add ORDER BY clause
    /// </summary>
    public SqlQueryBuilder<T> OrderBy(string orderByClause)
    {
        _orderBy = orderByClause;
        return this;
    }

    /// <summary>
    /// Add LIMIT clause
    /// </summary>
    public SqlQueryBuilder<T> Take(int count)
    {
        _limit = count;
        return this;
    }

    /// <summary>
    /// Add OFFSET clause
    /// </summary>
    public SqlQueryBuilder<T> Skip(int count)
    {
        _offset = count;
        return this;
    }

    /// <summary>
    /// Execute query and return results
    /// </summary>
    public async Task<List<T>> ExecuteAsync()
    {
        var sql = BuildFinalSql();
        
        if (_enableSqlLogging)
        {
            Console.WriteLine($"[SQL] {sql}");
        }
        
        _connection.Open();
        using var command = new TrinoCommand(_connection, sql);
        using var reader = (TrinoDataReader)await command.ExecuteReaderAsync();
        
        var results = new List<T>();
        while (await reader.ReadAsync())
        {
            results.Add(MapToObject(reader));
        }
        
        return results;
    }

    /// <summary>
    /// Execute query and return first result or null
    /// </summary>
    public async Task<T?> FirstOrDefaultAsync()
    {
        var results = await Take(1).ExecuteAsync();
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Execute query and return count
    /// </summary>
    public async Task<int> CountAsync()
    {
        // Replace SELECT clause with COUNT(*)
        var sql = _baseSql;
        var selectIndex = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        var fromIndex = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        
        if (selectIndex >= 0 && fromIndex > selectIndex)
        {
            sql = "SELECT COUNT(*) " + sql.Substring(fromIndex);
        }
        
        sql = BuildFinalSql(sql);
        
        if (_enableSqlLogging)
        {
            Console.WriteLine($"[SQL] {sql}");
        }
        
        _connection.Open();
        using var command = new TrinoCommand(_connection, sql);
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return Convert.ToInt32(reader.GetValue(0));
        }
        
        return 0;
    }

    /// <summary>
    /// Build final SQL with WHERE, ORDER BY, LIMIT, OFFSET
    /// </summary>
    private string BuildFinalSql(string? baseSql = null)
    {
        var sql = new StringBuilder(baseSql ?? _baseSql);

        // Add WHERE clauses
        if (_whereConditions.Any())
        {
            // Check if base SQL already has WHERE
            var hasWhere = sql.ToString().IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) >= 0;
            
            if (hasWhere)
            {
                sql.Append(" AND ");
                sql.Append(string.Join(" AND ", _whereConditions.Select(w => $"({w.Clause})")));
            }
            else
            {
                sql.Append(" WHERE ");
                sql.Append(string.Join(" AND ", _whereConditions.Select(w => $"({w.Clause})")));
            }
        }

        // Add ORDER BY
        if (!string.IsNullOrEmpty(_orderBy))
        {
            sql.Append($" ORDER BY {_orderBy}");
        }

        // Add LIMIT
        if (_limit.HasValue)
        {
            sql.Append($" LIMIT {_limit.Value}");
        }

        // Add OFFSET
        if (_offset.HasValue)
        {
            sql.Append($" OFFSET {_offset.Value}");
        }

        // Replace parameters
        var finalSql = sql.ToString();
        foreach (var param in _parameters)
        {
            var safeValue = EscapeValue(param.Value);
            finalSql = finalSql.Replace($"@{param.Key}", safeValue);
        }

        return finalSql;
    }

    /// <summary>
    /// Escape value to prevent SQL injection
    /// </summary>
    private string EscapeValue(object? value)
    {
        if (value == null)
            return "NULL";

        if (value is string str)
            return $"'{str.Replace("'", "''")}'";

        if (value is DateTime dt)
            return $"TIMESTAMP '{dt:yyyy-MM-dd HH:mm:ss.fff}'";

        if (value is bool b)
            return b ? "TRUE" : "FALSE";

        if (value is decimal || value is double || value is float)
            return Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture);

        if (value is int || value is long || value is short)
            return value.ToString()!;

        return $"'{value.ToString()!.Replace("'", "''")}'";
    }

    /// <summary>
    /// Map DataReader row to object
    /// </summary>
    private T MapToObject(TrinoDataReader reader)
    {
        var obj = new T();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            try
            {
                object? value = null;
                try
                {
                    value = reader[prop.Name];
                }
                catch
                {
                    try
                    {
                        value = reader[prop.Name.ToLower()];
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (value != null && value != DBNull.Value)
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    
                    if (targetType == typeof(decimal) && value is double dbl)
                    {
                        prop.SetValue(obj, Convert.ToDecimal(dbl));
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
                // Ignore mapping errors
            }
        }

        return obj;
    }

    private class WhereCondition
    {
        public string Clause { get; set; } = string.Empty;
    }
}
