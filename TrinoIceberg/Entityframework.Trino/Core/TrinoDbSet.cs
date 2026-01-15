using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Core;

/// <summary>
/// Represents a collection of entities that can be queried from Trino - giống DbSet<T> của Entity Framework
/// </summary>
public class TrinoDbSet<T> : IQueryable<T>, IEnumerable<T> where T : class, new()
{
    private readonly TrinoConnection _connection;
    private readonly string _tableName;
    private List<string>? _whereConditions;
    private List<string>? _selectColumns;
    private List<string>? _orderByColumns;
    private int? _takeCount;
    private int? _skipCount;

    public TrinoDbSet(TrinoConnection connection, string tableName)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _tableName = ValidateSqlIdentifier(tableName, nameof(tableName));
    }

    // Private constructor for cloning
    private TrinoDbSet(TrinoConnection connection, string tableName, 
        List<string>? whereConditions, List<string>? selectColumns, 
        List<string>? orderByColumns, int? takeCount, int? skipCount)
    {
        _connection = connection;
        _tableName = tableName;
        _whereConditions = whereConditions != null ? new List<string>(whereConditions) : null;
        _selectColumns = selectColumns != null ? new List<string>(selectColumns) : null;
        _orderByColumns = orderByColumns != null ? new List<string>(orderByColumns) : null;
        _takeCount = takeCount;
        _skipCount = skipCount;
    }

    // Clone method - tạo instance mới với state hiện tại
    private TrinoDbSet<T> Clone()
    {
        return new TrinoDbSet<T>(_connection, _tableName, _whereConditions, 
            _selectColumns, _orderByColumns, _takeCount, _skipCount);
    }

    public Type ElementType => typeof(T);
    public Expression Expression => Expression.Constant(this);
    public IQueryProvider Provider => throw new NotImplementedException("Full LINQ provider not implemented");

    #region Where Methods - LINQ Support

    /// <summary>
    /// Lọc records với lambda expression - giống Where() trong EF
    /// Type-safe and SQL injection protected
    /// Usage: context.Customers.Where(c => c.Id > 1)
    /// </summary>
    public TrinoDbSet<T> Where(Expression<Func<T, bool>> predicate)
    {
        var clone = Clone();
        var sqlCondition = TranslateExpression(predicate.Body);
        clone._whereConditions ??= new List<string>();
        clone._whereConditions.Add(sqlCondition);
        return clone;
    }

    /// <summary>
    /// Conditionally add WHERE clause - similar to ABP framework's WhereIf
    /// Only applies the filter if condition is true
    /// Type-safe and SQL injection protected
    /// </summary>
    /// <example>
    /// var query = context.Customers
    ///     .WhereIf(!string.IsNullOrEmpty(name), c => c.Name.Contains(name))
    ///     .WhereIf(minAge.HasValue, c => c.Age >= minAge.Value)
    ///     .WhereIf(isActive.HasValue, c => c.IsActive == isActive.Value);
    /// var results = await query.ToListAsync();
    /// </example>
    public TrinoDbSet<T> WhereIf(bool condition, Expression<Func<T, bool>> predicate)
    {
        if (condition)
        {
            return Where(predicate);
        }
        return Clone();
    }

    #endregion
    
    /// <summary>
    /// Lấy N records đầu tiên - giống Take() trong EF
    /// </summary>
    public TrinoDbSet<T> Take(int count)
    {
        var clone = Clone();
        clone._takeCount = count;
        return clone;
    }

    /// <summary>
    /// Bỏ qua N records đầu tiên - giống Skip() trong EF
    /// </summary>
    public TrinoDbSet<T> Skip(int count)
    {
        var clone = Clone();
        clone._skipCount = count;
        return clone;
    }

    /// <summary>
    /// Lấy tất cả records - giống ToListAsync() trong EF
    /// </summary>
    public async Task<List<T>> ToListAsync()
    {
        var sql = BuildSql();
        return await ExecuteQueryAsync(sql);
    }

    /// <summary>
    /// Lấy record đầu tiên hoặc null - giống FirstOrDefaultAsync() trong EF
    /// </summary>
    public async Task<T?> FirstOrDefaultAsync()
    {
        var originalTake = _takeCount;
        _takeCount = 1;
        var sql = BuildSql();
        _takeCount = originalTake;

        var results = await ExecuteQueryAsync(sql);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Đếm số lượng records - giống CountAsync() trong EF
    /// </summary>
    public async Task<long> CountAsync()
    {
        var whereClause = _whereConditions != null && _whereConditions.Any()
            ? " WHERE " + string.Join(" AND ", _whereConditions)
            : "";

        var sql = $"SELECT COUNT(*) FROM {_tableName}{whereClause}";

        using var command = new TrinoCommand(_connection, sql);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return reader.GetInt64(0);
        }

        return 0;
    }

    #region CUD Operations (Create, Update, Delete)

    /// <summary>
    /// Thêm entity mới - giống Add() trong EF
    /// </summary>
    public void Add(T entity)
    {
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => GetColumnName(p)));
        var values = string.Join(", ", properties.Select(p => FormatValue(p.GetValue(entity))));

        var sql = $"INSERT INTO {_tableName} ({columns}) VALUES ({values})";
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Thêm entity mới - async version
    /// </summary>
    public async Task AddAsync(T entity)
    {
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => GetColumnName(p)));
        var values = string.Join(", ", properties.Select(p => FormatValue(p.GetValue(entity))));

        var sql = $"INSERT INTO {_tableName} ({columns}) VALUES ({values})";
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Thêm nhiều entities - giống AddRange() trong EF
    /// </summary>
    public void AddRange(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            Add(entity);
        }
    }

    /// <summary>
    /// Thêm nhiều entities - async version
    /// </summary>
    public async Task AddRangeAsync(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            await AddAsync(entity);
        }
    }

    /// <summary>
    /// Thêm nhiều entities sử dụng multiple VALUES - HIỆU QUẢ HƠN 30x so với insert từng dòng
    /// INSERT INTO table (col1, col2) VALUES (val1, val2), (val3, val4), ...
    /// Nhanh hơn vì: 1 transaction thay vì N transactions, 1 network call thay vì N calls
    /// </summary>
    public void AddRangeV1(IEnumerable<T> entities)
    {
        var entityList = entities.ToList();
        if (!entityList.Any())
            return;

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => GetColumnName(p)));
        
        // Build multiple VALUES clauses: (val1, val2), (val3, val4), ...
        var valuesClauses = entityList.Select(entity =>
        {
            var values = string.Join(", ", properties.Select(p => FormatValue(p.GetValue(entity))));
            return $"({values})";
        });

        var sql = $"INSERT INTO {_tableName} ({columns}) VALUES {string.Join(", ", valuesClauses)}";
        // Console.WriteLine($"[DEBUG SQL - Multiple VALUES] Inserting {entityList.Count} records");
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Thêm nhiều entities sử dụng multiple VALUES - async version
    /// INSERT INTO table (col1, col2) VALUES (val1, val2), (val3, val4), ...
    /// Nhanh hơn 30x so với insert từng dòng (2s vs 60s cho 1000 rows)
    /// </summary>
    public async Task AddRangeV1Async(IEnumerable<T> entities)
    {
        var entityList = entities.ToList();
        if (!entityList.Any())
            return;

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null)
            .ToList();

        var columns = string.Join(", ", properties.Select(p => GetColumnName(p)));
        
        // Build multiple VALUES clauses: (val1, val2), (val3, val4), ...
        var valuesClauses = entityList.Select(entity =>
        {
            var values = string.Join(", ", properties.Select(p => FormatValue(p.GetValue(entity))));
            return $"({values})";
        });

        var sql = $"INSERT INTO {_tableName} ({columns}) VALUES {string.Join(", ", valuesClauses)}";
        // Console.WriteLine($"[DEBUG SQL - Multiple VALUES] Inserting {entityList.Count} records");
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        await command.ExecuteNonQueryAsync();
    }
    
    /// <summary>
    /// Cập nhật entity - giống Update() trong EF
    /// </summary>
    public void Update(T entity)
    {
        var keyProperty = GetKeyProperty();
        if (keyProperty == null)
            throw new InvalidOperationException($"Type {typeof(T).Name} must have a [Key] attribute to support Update");

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.CanWrite &&
                       p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null &&
                       p != keyProperty)
            .ToList();

        var setClause = string.Join(", ", properties.Select(p => 
            $"{GetColumnName(p)} = {FormatValue(p.GetValue(entity))}"));

        var keyColumnName = GetColumnName(keyProperty);
        var keyValue = FormatValue(keyProperty.GetValue(entity));

        var sql = $"UPDATE {_tableName} SET {setClause} WHERE {keyColumnName} = {keyValue}";
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Cập nhật entity - async version
    /// </summary>
    public async Task UpdateAsync(T entity)
    {
        var keyProperty = GetKeyProperty();
        if (keyProperty == null)
            throw new InvalidOperationException($"Type {typeof(T).Name} must have a [Key] attribute to support Update");

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.CanWrite &&
                       p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null &&
                       p != keyProperty)
            .ToList();

        var setClause = string.Join(", ", properties.Select(p => 
            $"{GetColumnName(p)} = {FormatValue(p.GetValue(entity))}"));

        var keyColumnName = GetColumnName(keyProperty);
        var keyValue = FormatValue(keyProperty.GetValue(entity));

        var sql = $"UPDATE {_tableName} SET {setClause} WHERE {keyColumnName} = {keyValue}";
        // Console.WriteLine($"[DEBUG SQL] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Xóa entity - giống Remove() trong EF
    /// </summary>
    public void Remove(T entity)
    {
        var keyProperty = GetKeyProperty();
        if (keyProperty == null)
            throw new InvalidOperationException($"Type {typeof(T).Name} must have a [Key] attribute to support Remove");

        var keyColumnName = GetColumnName(keyProperty);
        var keyValue = FormatValue(keyProperty.GetValue(entity));

        var sql = $"DELETE FROM {_tableName} WHERE {keyColumnName} = {keyValue}";
        
        // Console.WriteLine($"[SQL DELETE] {sql}");
        
        using var command = new TrinoCommand(_connection, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Xóa entity - async version
    /// </summary>
    public async Task RemoveAsync(T entity)
    {
        var keyProperty = GetKeyProperty();
        if (keyProperty == null)
            throw new InvalidOperationException($"Type {typeof(T).Name} must have a [Key] attribute to support Remove");

        var keyColumnName = GetColumnName(keyProperty);
        var keyValue = FormatValue(keyProperty.GetValue(entity));

        var sql = $"DELETE FROM {_tableName} WHERE {keyColumnName} = {keyValue}";
        
        // Console.WriteLine($"[SQL DELETE] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Xóa nhiều entities - giống RemoveRange() trong EF
    /// </summary>
    public void RemoveRange(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            Remove(entity);
        }
    }

    /// <summary>
    /// Xóa nhiều entities - async version
    /// </summary>
    public async Task RemoveRangeAsync(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
        {
            await RemoveAsync(entity);
        }
    }

    /// <summary>
    /// Xóa tất cả records thỏa điều kiện WHERE hiện tại
    /// </summary>
    public async Task<int> DeleteAsync()
    {
        var whereClause = _whereConditions != null && _whereConditions.Any()
            ? " WHERE " + string.Join(" AND ", _whereConditions)
            : "";

        var sql = $"DELETE FROM {_tableName}{whereClause}";
        
        // Console.WriteLine($"[SQL DELETE] {sql}");

        using var command = new TrinoCommand(_connection, sql);
        return await command.ExecuteNonQueryAsync();
    }
    
    private PropertyInfo? GetKeyProperty()
    {
        // 1. Ưu tiên property có [Key] attribute
        var keyProperty = typeof(T).GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);
        
        if (keyProperty != null)
            return keyProperty;
        
        // 2. Fallback: tìm property tên "Id" (case-insensitive)
        return typeof(T).GetProperties()
            .FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validate SQL identifier (table name, column name) để prevent SQL injection
    /// Chỉ cho phép: letters, numbers, underscore
    /// </summary>
    private static string ValidateSqlIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException($"{paramName} cannot be null or empty", paramName);
        
        // Check for valid SQL identifier: alphanumeric + underscore only
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException(
                $"{paramName} '{identifier}' contains invalid characters. Only letters, numbers, and underscores are allowed.",
                paramName);
        
        // Check length
        if (identifier.Length > 128)
            throw new ArgumentException($"{paramName} is too long (max 128 characters)", paramName);
        
        return identifier;
    }

    /// <summary>
    /// Validate multiple SQL identifiers
    /// </summary>
    private static void ValidateSqlIdentifiers(IEnumerable<string> identifiers, string paramName)
    {
        foreach (var identifier in identifiers)
        {
            ValidateSqlIdentifier(identifier, paramName);
        }
    }

    #endregion


    /// <summary>
    /// Execute raw SQL và map về entities
    /// ⚠️ WARNING: SQL INJECTION RISK - Only use with trusted SQL or parameterized queries
    /// </summary>
    public async Task<List<T>> FromSqlRawAsync(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL cannot be null or empty", nameof(sql));
        
        return await ExecuteQueryAsync(sql);
    }

    private string BuildSql()
    {
        var columns = _selectColumns != null && _selectColumns.Any()
            ? string.Join(", ", _selectColumns)
            : "*";

        var whereClause = _whereConditions != null && _whereConditions.Any()
            ? " WHERE " + string.Join(" AND ", _whereConditions)
            : "";

        var orderByClause = _orderByColumns != null && _orderByColumns.Any()
            ? " ORDER BY " + string.Join(", ", _orderByColumns)
            : "";

        // Trino syntax: OFFSET comes BEFORE LIMIT
        // Trino requires LIMIT when using OFFSET
        string offsetLimitClause = "";
        
        if (_skipCount.HasValue && !_takeCount.HasValue)
        {
            // Skip without Take - use OFFSET with large LIMIT
            offsetLimitClause = $" OFFSET {_skipCount.Value} LIMIT 999999999";
        }
        else if (_takeCount.HasValue && _skipCount.HasValue)
        {
            // Both Skip and Take
            offsetLimitClause = $" OFFSET {_skipCount.Value} LIMIT {_takeCount.Value}";
        }
        else if (_takeCount.HasValue)
        {
            // Take only
            offsetLimitClause = $" LIMIT {_takeCount.Value}";
        }

        var sql = $"SELECT {columns} FROM {_tableName}{whereClause}{orderByClause}{offsetLimitClause}";
        
        // Console.WriteLine($"[SQL QUERY] {sql}");
        
        return sql;
    }

    private async Task<List<T>> ExecuteQueryAsync(string sql)
    {
        var results = new List<T>();

        using var command = new TrinoCommand(_connection, sql);
        using var reader = await command.ExecuteReaderAsync();

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanWrite)
            .ToList();

        while (await reader.ReadAsync())
        {
            var entity = new T();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var prop = properties.FirstOrDefault(p =>
                    GetColumnName(p).Equals(columnName, StringComparison.OrdinalIgnoreCase));

                if (prop != null && !reader.IsDBNull(i))
                {
                    var value = reader.GetValue(i);
                    prop.SetValue(entity, Convert.ChangeType(value, prop.PropertyType));
                }
            }

            results.Add(entity);
        }

        return results;
    }

    private string GetColumnName(PropertyInfo property)
    {
        var columnAttr = property.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
        return columnAttr?.Name ?? property.Name;
    }

    private string FormatValue(object? value)
    {
        if (value == null) return "NULL";
        if (value is string s) return $"'{s.Replace("'", "''")}'";
        if (value is Guid g) return $"CAST('{g}' AS UUID)";
        if (value is DateTime dt) return $"TIMESTAMP '{dt:yyyy-MM-dd HH:mm:ss.fff}'";
        if (value is decimal dec) return dec.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "true" : "false";
        
        return value.ToString() ?? "NULL";
    }

    /// <summary>
    /// Translate Expression Tree to SQL - Simple implementation
    /// Supports: c => c.Property == value, c => c.Property > value, etc.
    /// </summary>
    private string TranslateExpression(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binary:
                return TranslateBinaryExpression(binary);
            
            case MethodCallExpression methodCall:
                return TranslateMethodCall(methodCall);
            
            case MemberExpression member:
                // Check if this is a property access on the entity (e.g., c.Id)
                if (member.Expression?.NodeType == ExpressionType.Parameter)
                {
                    return GetColumnName(member.Member as PropertyInfo ?? throw new InvalidOperationException());
                }
                // Otherwise it's a closure variable (e.g., customerId from outer scope)
                // Evaluate it to get the actual value
                else
                {
                    var value = EvaluateExpression(member);
                    return FormatValue(value);
                }
            
            case ConstantExpression constant:
                return FormatValue(constant.Value);
            
            case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
                return $"NOT ({TranslateExpression(unary.Operand)})";
            
            case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                return TranslateExpression(unary.Operand);
            
            default:
                throw new NotSupportedException($"Expression type {expression.NodeType} is not supported. Use string-based Where() for complex queries.");
        }
    }

    private object? EvaluateExpression(Expression expression)
    {
        // Compile and execute the expression to get its value
        var lambda = Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object)));
        var compiled = lambda.Compile();
        return compiled();
    }

    private string TranslateBinaryExpression(BinaryExpression binary)
    {
        var left = TranslateExpression(binary.Left);
        var right = TranslateExpression(binary.Right);
        
        var op = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            _ => throw new NotSupportedException($"Binary operator {binary.NodeType} is not supported")
        };
        
        // Handle method calls like Contains
        if (binary.Left is MethodCallExpression methodCall)
        {
            return TranslateMethodCall(methodCall);
        }
        
        return $"{left} {op} {right}";
    }

    private string TranslateMethodCall(MethodCallExpression methodCall)
    {
        if (methodCall.Method.DeclaringType == typeof(string))
        {
            var obj = TranslateExpression(methodCall.Object!);
            var arg = methodCall.Arguments.Count > 0 ? TranslateExpression(methodCall.Arguments[0]) : null;
            
            return methodCall.Method.Name switch
            {
                "Contains" => $"{obj} LIKE '%' || {arg} || '%'",
                "StartsWith" => $"{obj} LIKE {arg} || '%'",
                "EndsWith" => $"{obj} LIKE '%' || {arg}",
                "ToLower" => $"lower({obj})",
                "ToUpper" => $"upper({obj})",
                _ => throw new NotSupportedException($"String method {methodCall.Method.Name} is not supported")
            };
        }
        
        throw new NotSupportedException($"Method {methodCall.Method.Name} is not supported");
    }

    #region Additional EF-like Methods

    /// <summary>
    /// Lấy record đầu tiên hoặc throw exception - giống FirstAsync() trong EF
    /// </summary>
    public async Task<T> FirstAsync()
    {
        var result = await FirstOrDefaultAsync();
        if (result == null)
            throw new InvalidOperationException("Sequence contains no elements");
        return result;
    }

    /// <summary>
    /// Lấy record duy nhất hoặc null - giống SingleOrDefaultAsync() trong EF
    /// </summary>
    public async Task<T?> SingleOrDefaultAsync()
    {
        var results = await Take(2).ToListAsync();
        
        if (results.Count == 0)
            return null;
        
        if (results.Count > 1)
            throw new InvalidOperationException("Sequence contains more than one element");
        
        return results[0];
    }

    /// <summary>
    /// Lấy record duy nhất hoặc throw exception - giống SingleAsync() trong EF
    /// </summary>
    public async Task<T> SingleAsync()
    {
        var result = await SingleOrDefaultAsync();
        if (result == null)
            throw new InvalidOperationException("Sequence contains no elements");
        return result;
    }

    /// <summary>
    /// Kiểm tra có record nào thỏa điều kiện không - giống AnyAsync() trong EF
    /// </summary>
    public async Task<bool> AnyAsync()
    {
        var count = await CountAsync();
        return count > 0;
    }

    /// <summary>
    /// Kiểm tra có record nào thỏa điều kiện không - giống AnyAsync() trong EF
    /// </summary>
    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
    {
        return await Where(predicate).AnyAsync();
    }

    /// <summary>
    /// Sắp xếp tăng dần - giống OrderBy() trong EF
    /// </summary>
    public TrinoDbSet<T> OrderBy(Expression<Func<T, object>> keySelector)
    {
        var clone = Clone();
        var columnName = GetMemberName(keySelector);
        clone._orderByColumns ??= new List<string>();
        clone._orderByColumns.Add($"{columnName} ASC");
        return clone;
    }

    /// <summary>
    /// Sắp xếp giảm dần - giống OrderByDescending() trong EF
    /// </summary>
    public TrinoDbSet<T> OrderByDescending(Expression<Func<T, object>> keySelector)
    {
        var clone = Clone();
        var columnName = GetMemberName(keySelector);
        clone._orderByColumns ??= new List<string>();
        clone._orderByColumns.Add($"{columnName} DESC");
        return clone;
    }

    private string GetMemberName(Expression<Func<T, object>> expression)
    {
        var body = expression.Body;
        
        // Handle conversions
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            body = unary.Operand;
        }
        
        if (body is MemberExpression member)
        {
            var prop = member.Member as PropertyInfo;
            return GetColumnName(prop!);
        }
        
        throw new ArgumentException("Expression must be a member access");
    }

    #endregion

    public IEnumerator<T> GetEnumerator()
    {
        return ToListAsync().GetAwaiter().GetResult().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
