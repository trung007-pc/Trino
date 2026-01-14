using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Core;

/// <summary>
/// Represents a projection query - giống IQueryable<T> projection trong LINQ
/// Lazy evaluation - chỉ execute khi gọi ToListAsync()
/// </summary>
public class TrinoProjection<TSource, TResult> where TSource : class, new()
{
    private readonly TrinoConnection _connection;
    private readonly string _tableName;
    private readonly Expression<Func<TSource, TResult>> _selector;
    private readonly List<string>? _whereConditions;
    private readonly List<string>? _orderByColumns;
    private readonly int? _takeCount;
    private readonly int? _skipCount;

    public TrinoProjection(
        TrinoConnection connection,
        string tableName,
        Expression<Func<TSource, TResult>> selector,
        List<string>? whereConditions = null,
        List<string>? orderByColumns = null,
        int? takeCount = null,
        int? skipCount = null)
    {
        _connection = connection;
        _tableName = tableName;
        _selector = selector;
        _whereConditions = whereConditions;
        _orderByColumns = orderByColumns;
        _takeCount = takeCount;
        _skipCount = skipCount;
    }

    /// <summary>
    /// Execute query và return results - giống ToListAsync() trong EF
    /// </summary>
    public async Task<List<TResult>> ToListAsync()
    {
        var columns = ExtractSelectColumns(_selector);
        var sql = BuildSql(columns);
        
        Console.WriteLine($"[DEBUG SQL] {sql}");
        
        _connection.Open();
        using var command = new TrinoCommand(_connection, sql);
        using var reader = await command.ExecuteReaderAsync();
        
        var results = new List<TResult>();
        var columnNames = columns.ToArray();
        
        while (await reader.ReadAsync())
        {
            var result = MapToType(reader, columnNames, _selector);
            results.Add(result);
        }
        
        return results;
    }

    /// <summary>
    /// Execute query và return first result hoặc default - giống FirstOrDefaultAsync()
    /// </summary>
    public async Task<TResult?> FirstOrDefaultAsync()
    {
        var columns = ExtractSelectColumns(_selector);
        var sql = BuildSql(columns, limitOverride: 1);
        
        Console.WriteLine($"[DEBUG SQL] {sql}");
        
        _connection.Open();
        using var command = new TrinoCommand(_connection, sql);
        using var reader = await command.ExecuteReaderAsync();
        
        var columnNames = columns.ToArray();
        
        if (await reader.ReadAsync())
        {
            return MapToType(reader, columnNames, _selector);
        }
        
        return default;
    }

    /// <summary>
    /// Build SQL query
    /// </summary>
    private string BuildSql(List<string> columns, int? limitOverride = null)
    {
        var selectClause = string.Join(", ", columns);
        var whereClause = _whereConditions != null && _whereConditions.Any()
            ? " WHERE " + string.Join(" AND ", _whereConditions)
            : "";
        var orderByClause = _orderByColumns != null && _orderByColumns.Any()
            ? " ORDER BY " + string.Join(", ", _orderByColumns)
            : "";
        
        // Handle LIMIT and OFFSET
        // Trino doesn't support OFFSET directly, need to use subquery with ROW_NUMBER
        if (_skipCount.HasValue && _skipCount.Value > 0)
        {
            // Use ROW_NUMBER() for pagination
            var orderBy = _orderByColumns != null && _orderByColumns.Any() 
                ? string.Join(", ", _orderByColumns)
                : columns[0]; // Default order by first column
            
            var limit = limitOverride ?? _takeCount ?? int.MaxValue;
            var offset = _skipCount.Value;
            
            var innerSelect = $"SELECT {selectClause}, ROW_NUMBER() OVER (ORDER BY {orderBy}) as rn FROM {_tableName}{whereClause}";
            return $"SELECT {string.Join(", ", columns)} FROM ({innerSelect}) WHERE rn > {offset} AND rn <= {offset + limit}";
        }
        
        // Simple LIMIT without OFFSET
        var limitClause = limitOverride.HasValue 
            ? $" LIMIT {limitOverride.Value}"
            : _takeCount.HasValue 
                ? $" LIMIT {_takeCount.Value}"
                : "";

        return $"SELECT {selectClause} FROM {_tableName}{whereClause}{orderByClause}{limitClause}";
    }

    /// <summary>
    /// Extract column names từ Select expression
    /// </summary>
    private List<string> ExtractSelectColumns(Expression<Func<TSource, TResult>> selector)
    {
        var columns = new List<string>();
        
        if (selector.Body is NewExpression newExpr)
        {
            // Anonymous type: new { c.Name, c.Email }
            foreach (var arg in newExpr.Arguments)
            {
                if (arg is MemberExpression memberExpr)
                {
                    columns.Add(GetColumnName(memberExpr.Member as PropertyInfo));
                }
            }
        }
        else if (selector.Body is MemberInitExpression memberInitExpr)
        {
            // Named type: new CustomerDto { Name = c.Name, Email = c.Email }
            foreach (var binding in memberInitExpr.Bindings)
            {
                if (binding is MemberAssignment assignment && 
                    assignment.Expression is MemberExpression memberExpr)
                {
                    columns.Add(GetColumnName(memberExpr.Member as PropertyInfo));
                }
            }
        }
        else if (selector.Body is MemberExpression singleMember)
        {
            // Single property: c => c.Name
            columns.Add(GetColumnName(singleMember.Member as PropertyInfo));
        }
        
        return columns;
    }

    /// <summary>
    /// Get column name from property (check for [Column] attribute)
    /// </summary>
    private string GetColumnName(PropertyInfo? property)
    {
        if (property == null)
            return "*";

        var columnAttr = property.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
        return columnAttr?.Name ?? property.Name.ToLower();
    }

    /// <summary>
    /// Map DataReader row to target type
    /// </summary>
    private TResult MapToType(DbDataReader reader, string[] columnNames, Expression<Func<TSource, TResult>> selector)
    {
        if (selector.Body is NewExpression newExpr)
        {
            // Anonymous type or constructor with parameters
            var args = new object?[newExpr.Arguments.Count];
            for (int i = 0; i < columnNames.Length && i < args.Length; i++)
            {
                args[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            return (TResult)newExpr.Constructor!.Invoke(args);
        }
        else if (selector.Body is MemberInitExpression memberInitExpr)
        {
            // Named type with property initialization
            var instance = Activator.CreateInstance<TResult>();
            var bindings = memberInitExpr.Bindings.Cast<MemberAssignment>().ToList();
            
            for (int i = 0; i < columnNames.Length && i < bindings.Count; i++)
            {
                var prop = bindings[i].Member as PropertyInfo;
                if (prop != null && prop.CanWrite)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    prop.SetValue(instance, value);
                }
            }
            return instance;
        }
        else if (selector.Body is MemberExpression)
        {
            // Single value
            var value = reader.IsDBNull(0) ? null : reader.GetValue(0);
            return (TResult)value!;
        }
        
        throw new NotSupportedException($"Select expression type not supported: {selector.Body.GetType().Name}");
    }
}
