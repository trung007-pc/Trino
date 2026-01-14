using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Trino.Data.ADO.Client;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Core;

/// <summary>
/// Fluent query builder với auto-generated SELECT từ lambda expression
/// </summary>
public class FluentQueryBuilder<TResult> where TResult : class
{
    private readonly TrinoConnection _connection;
    private readonly string _fromClause;
    private readonly bool _enableSqlLogging;
    private Type[] _types = Array.Empty<Type>();
    private Delegate? _mapFunc;
    private List<string> _selectColumns = new();
    private List<string> _whereClauses = new();
    private Dictionary<string, object?>? _parameters;

    public FluentQueryBuilder(TrinoConnection connection, string fromClause, bool enableSqlLogging)
    {
        _connection = connection;
        _fromClause = fromClause;
        _enableSqlLogging = enableSqlLogging;
    }

    /// <summary>
    /// Select với dynamic types - Support UNLIMITED tables
    /// </summary>
    /// <example>
    /// // 2 tables
    /// .Select((Customer c, Order o) => ...)
    /// // 3 tables
    /// .Select((Customer c, Order o, OrderItem oi) => ...)
    /// // 7 tables
    /// .Select((T1 a, T2 b, T3 c, T4 d, T5 e, T6 f, T7 g) => ...)
    /// </example>
    public FluentQueryBuilder<TResult> Select(LambdaExpression selector)
    {
        // Extract types from lambda parameters
        _types = selector.Parameters
            .Select(p => p.Type)
            .ToArray();
        
        _mapFunc = selector.Compile();
        
        ParseSelectExpression(selector);
        
        return this;
    }
    
    // Convenience overloads cho type safety (giữ IntelliSense)
    public FluentQueryBuilder<TResult> Select<T1>(Expression<Func<T1, TResult>> selector)
        where T1 : class, new()
        => Select((LambdaExpression)selector);
    
    public FluentQueryBuilder<TResult> Select<T1, T2>(Expression<Func<T1, T2, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        => Select((LambdaExpression)selector);

    public FluentQueryBuilder<TResult> Select<T1, T2, T3>(Expression<Func<T1, T2, T3, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        => Select((LambdaExpression)selector);
    
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4>(Expression<Func<T1, T2, T3, T4, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        => Select((LambdaExpression)selector);
    
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5>(Expression<Func<T1, T2, T3, T4, T5, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        => Select((LambdaExpression)selector);
    
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5, T6>(Expression<Func<T1, T2, T3, T4, T5, T6, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        where T6 : class, new()
        => Select((LambdaExpression)selector);
    
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5, T6, T7>(Expression<Func<T1, T2, T3, T4, T5, T6, T7, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        where T6 : class, new()
        where T7 : class, new()
        => Select((LambdaExpression)selector);

    /// <summary>
    /// Add parameters để chống SQL injection
    /// </summary>
    /// <example>
    /// .WithParams(new { customerId = "CUST_123", status = "Active" })
    /// </example>
    public FluentQueryBuilder<TResult> WithParams(object parameters)
    {
        _parameters = new Dictionary<string, object?>();
        
        foreach (var prop in parameters.GetType().GetProperties())
        {
            _parameters[prop.Name] = prop.GetValue(parameters);
        }
        
        return this;
    }

    /// <summary>
    /// Internal WithParams for copying from non-generic builder
    /// </summary>
    internal FluentQueryBuilder<TResult> WithParams(Dictionary<string, object?>? parameters)
    {
        if (parameters != null)
        {
            _parameters = new Dictionary<string, object?>(parameters);
        }
        return this;
    }

    internal FluentQueryBuilder<TResult> WithWhereClauses(List<string> whereClauses)
    {
        _whereClauses = new List<string>(whereClauses);
        return this;
    }

    public FluentQueryBuilder<TResult> WhereIf(bool condition, string whereClause)
    {
        if (condition && !string.IsNullOrWhiteSpace(whereClause))
        {
            _whereClauses.Add(whereClause);
        }
        return this;
    }

    /// <summary>
    /// Parse lambda expression để extract SELECT columns
    /// </summary>
    private void ParseSelectExpression(LambdaExpression expression)
    {
        // Build mapping: parameter index → table alias từ FROM clause
        var parameterAliases = new Dictionary<int, string>();
        for (int i = 0; i < _types.Length; i++)
        {
            parameterAliases[i] = GetTableAlias(_types[i]);
        }

        // Handle MemberInitExpression: new T { Prop1 = expr1, Prop2 = expr2 }
        if (expression.Body is MemberInitExpression memberInit)
        {
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    // Case 1: CustomerName = c.Name (MemberExpression)
                    if (assignment.Expression is MemberExpression memberExpr && 
                        memberExpr.Expression is ParameterExpression paramExpr)
                    {
                        var paramIndex = expression.Parameters.IndexOf(paramExpr);
                        if (paramIndex >= 0 && paramIndex < _types.Length)
                        {
                            var alias = parameterAliases[paramIndex];
                            _selectColumns.Add($"{alias}.{memberExpr.Member.Name}");
                        }
                    }
                    // Case 2: Order = o (ParameterExpression) → SELECT o.*
                    else if (assignment.Expression is ParameterExpression directParamExpr)
                    {
                        var paramIndex = expression.Parameters.IndexOf(directParamExpr);
                        if (paramIndex >= 0 && paramIndex < _types.Length)
                        {
                            var paramType = _types[paramIndex];
                            var alias = parameterAliases[paramIndex];
                            AddAllColumnsForType(paramType, alias);
                        }
                    }
                }
            }
            return;
        }

        // Handle NewExpression: new T(arg1, arg2, ...)
        if (expression.Body is not NewExpression newExpr)
            return;

        // Iterate through constructor arguments
        for (int i = 0; i < newExpr.Arguments.Count; i++)
        {
            var arg = newExpr.Arguments[i];
            var member = newExpr.Members?[i];

            if (arg is ParameterExpression paramExpr)
            {
                // Case: Customer = c1 → SELECT c.*
                var paramIndex = expression.Parameters.IndexOf(paramExpr);
                if (paramIndex >= 0 && paramIndex < _types.Length)
                {
                    var paramType = _types[paramIndex];
                    var alias = parameterAliases[paramIndex];
                    AddAllColumnsForType(paramType, alias);
                }
            }
            else if (arg is MemberExpression memberExpr)
            {
                // Case: Name = c1.Name → SELECT c.Name
                if (memberExpr.Expression is ParameterExpression paramInMember)
                {
                    var paramIndex = expression.Parameters.IndexOf(paramInMember);
                    if (paramIndex >= 0 && paramIndex < _types.Length)
                    {
                        var alias = parameterAliases[paramIndex];
                        var propertyName = memberExpr.Member.Name;
                        _selectColumns.Add($"{alias}.{propertyName}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add tất cả columns cho type (c.*)
    /// </summary>
    private void AddAllColumnsForType(Type type, string alias)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        foreach (var prop in properties)
        {
            // Skip navigation properties (complex types)
            if (prop.PropertyType.IsClass && 
                prop.PropertyType != typeof(string) && 
                !prop.PropertyType.IsPrimitive)
            {
                continue;
            }
            
            _selectColumns.Add($"{alias}.{prop.Name}");
        }
    }

    /// <summary>
    /// Extract table alias từ FROM clause
    /// </summary>
    private string GetTableAlias(Type type)
    {
        var tableName = GetTableName(type);
        
        // Try to find alias in FROM clause: "customers c"
        var pattern = $@"{tableName}\s+(\w+)";
        var match = System.Text.RegularExpressions.Regex.Match(_fromClause, pattern, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success)
            return match.Groups[1].Value;
        
        return tableName;
    }

    /// <summary>
    /// Get table name from type (Customer → customers)
    /// </summary>
    private string GetTableName(Type type)
    {
        // Simple pluralization
        var name = type.Name.ToLower();
        return name.EndsWith("y") 
            ? name.Substring(0, name.Length - 1) + "ies"
            : name + "s";
    }

    /// <summary>
    /// Build và execute SQL
    /// </summary>
    public async Task<List<TResult>> ExecuteAsync()
    {
        if (_mapFunc == null)
            throw new InvalidOperationException("Select() must be called before ExecuteAsync()");

        // Generate SELECT clause
        var selectClause = _selectColumns.Any() 
            ? $"SELECT {string.Join(", ", _selectColumns)}"
            : "SELECT *";

        var sql = $"{selectClause} {_fromClause}";

        if (_whereClauses.Count > 0)
        {
            if (_fromClause.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) >= 0)
                sql += " AND ";
            else
                sql += " WHERE ";
            sql += string.Join(" AND ", _whereClauses);
        }

        // Replace parameters để chống SQL injection
        if (_parameters != null)
        {
            foreach (var param in _parameters)
            {
                var paramValue = param.Value?.ToString()?.Replace("'", "''") ?? "NULL";
                sql = sql.Replace($"@{param.Key}", paramValue == "NULL" ? "NULL" : $"'{paramValue}'");
            }
        }

        if (_enableSqlLogging)
        {
            Console.WriteLine($"[SQL] {sql}");
        }

        _connection.Open();
        using var command = new TrinoCommand(_connection, sql);
        using var dataReader = await command.ExecuteReaderAsync();
        var reader = (TrinoDataReader)dataReader;

        var results = new List<TResult>();

        // Detect column mapping strategy
        var columnMap = DetectColumnMapping(reader);

        while (await reader.ReadAsync())
        {
            var objects = new List<object>();

            foreach (var type in _types)
            {
                var obj = MapObject(type, reader, columnMap[type]);
                objects.Add(obj);
            }

            var result = (TResult)_mapFunc.DynamicInvoke(objects.ToArray())!;
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Detect column mapping: 3-level algorithm cho UNLIMITED tables
    /// </summary>
    private Dictionary<Type, List<int>> DetectColumnMapping(TrinoDataReader reader)
    {
        // Level 1: Multiple duplicate detection (cho nhiều tables)
        var duplicateIndexes = FindAllDuplicateColumnIndexes(reader);
        if (duplicateIndexes.Count == _types.Length - 1)
        {
            // Split by duplicates
            var map = new Dictionary<Type, List<int>>();
            int startCol = 0;
            
            for (int i = 0; i < _types.Length; i++)
            {
                int endCol = (i < duplicateIndexes.Count) 
                    ? duplicateIndexes[i] - 1 
                    : reader.FieldCount - 1;
                
                map[_types[i]] = Enumerable.Range(startCol, endCol - startCol + 1).ToList();
                
                // Update startCol cho iteration tiếp theo
                if (i < duplicateIndexes.Count)
                {
                    startCol = duplicateIndexes[i];
                }
            }
            
            return map;
        }

        // Level 2: Property name matching
        var columnTypeMap = MatchColumnsToTypes(reader);
        if (columnTypeMap.Count > 0)
        {
            var grouped = columnTypeMap
                .GroupBy(x => x.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

            if (grouped.Count == _types.Length)
                return grouped;
        }

        // Level 3: Split evenly
        return SplitColumnsEvenly(reader.FieldCount);
    }

    /// <summary>
    /// Find ALL duplicate column indexes (cho nhiều tables)
    /// </summary>
    private List<int> FindAllDuplicateColumnIndexes(TrinoDataReader reader)
    {
        var indexes = new List<int>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (seen.Contains(colName))
            {
                indexes.Add(i);
            }
            seen.Add(colName);
        }

        return indexes;
    }

    private Dictionary<int, Type> MatchColumnsToTypes(TrinoDataReader reader)
    {
        var map = new Dictionary<int, Type>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var colName = reader.GetName(i);

            foreach (var type in _types)
            {
                var prop = type.GetProperty(colName, 
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop != null)
                {
                    map[i] = type;
                    break;
                }
            }
        }

        return map;
    }

    private Dictionary<Type, List<int>> SplitColumnsEvenly(int totalColumns)
    {
        var map = new Dictionary<Type, List<int>>();
        int colsPerType = totalColumns / _types.Length;
        int startCol = 0;

        foreach (var type in _types)
        {
            map[type] = Enumerable.Range(startCol, colsPerType).ToList();
            startCol += colsPerType;
        }

        return map;
    }

    private object MapObject(Type type, TrinoDataReader reader, List<int> columnIndexes)
    {
        var obj = Activator.CreateInstance(type)!;

        foreach (var colIndex in columnIndexes)
        {
            var colName = reader.GetName(colIndex);
            
            // Remove alias prefix if exists: "c.Name" → "Name"
            var dotIndex = colName.IndexOf('.');
            if (dotIndex > 0)
                colName = colName.Substring(dotIndex + 1);

            var prop = type.GetProperty(colName, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop != null)
            {
                var value = reader.GetValue(colIndex);
                if (value != null && value != DBNull.Value)
                {
                    SetPropertyValue(obj, prop, value);
                }
            }
        }

        return obj;
    }

    private void SetPropertyValue(object target, PropertyInfo prop, object value)
    {
        try
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (targetType == typeof(decimal))
            {
                // Handle Trino BigDecimal type
                if (value.GetType().Name == "TrinoBigDecimal")
                {
                    prop.SetValue(target, decimal.Parse(value.ToString()!));
                }
                else if (value is double dbl)
                {
                    prop.SetValue(target, Convert.ToDecimal(dbl));
                }
                else
                {
                    prop.SetValue(target, Convert.ToDecimal(value));
                }
            }
            else if (targetType == typeof(DateTime) && value is string dateStr)
            {
                prop.SetValue(target, DateTime.Parse(dateStr));
            }
            else if (targetType == typeof(int) && value is long lng)
            {
                prop.SetValue(target, Convert.ToInt32(lng));
            }
            else if (targetType.IsEnum && value is string enumStr)
            {
                prop.SetValue(target, Enum.Parse(targetType, enumStr));
            }
            else
            {
                prop.SetValue(target, Convert.ChangeType(value, targetType));
            }
        }
        catch
        {
            // Ignore conversion errors
        }
    }
}
