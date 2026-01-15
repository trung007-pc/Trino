using System.Data;
using Dapper;
using EntityFramework.Trino.Context;

namespace EntityFramework.Trino.Dapper;

/// <summary>
/// Extension methods để sử dụng Dapper mapping với Trino commands
/// Chỉ sử dụng Dapper cho object mapping, còn lại dùng Trino native
/// 
/// Có 2 cách sử dụng:
/// 1. Build SQL bằng TrinoSqlBuilder trước: var sql = new TrinoSqlBuilder(...).Build(); await QueryAsync(sql);
/// 2. Truyền SQL + parameters trực tiếp: await QueryAsync("SELECT * FROM customers WHERE id = @id", new { id = "123" });
/// </summary>
public static class TrinoExtensions
{
    /// <summary>
    /// Helper method để build SQL với parameters sử dụng TrinoSqlBuilder
    /// Chỉ build khi có parameters, nếu không có parameters thì SQL đã được build sẵn từ SqlBuilder
    /// </summary>
    private static string BuildSqlWithParameters(string sql, object? parameters)
    {
        // Nếu không có parameters, SQL đã được build sẵn (từ SqlBuilder.Build())
        if (parameters == null) return sql;
        
        // Nếu SQL không chứa @, nghĩa là đã được build rồi, không cần build lại
        if (!sql.Contains('@')) return sql;
        
        // Có parameters và SQL chứa @, dùng SqlBuilder để build
        var builder = new TrinoSqlBuilder(sql);
        builder.WithParams(parameters);
        return builder.Build();
    }

    /// <summary>
    /// Execute raw SQL query và map kết quả thành typed objects bằng Dapper
    /// </summary>
    public static async Task<IEnumerable<T>> QueryAsync<T>(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            using var reader = command.ExecuteReader();
            var parser = reader.GetRowParser<T>();
            var results = new List<T>();
            
            while (reader.Read())
            {
                results.Add(parser(reader));
            }
            
            return results;
        });
    }

    /// <summary>
    /// Execute raw SQL query và return object đầu tiên hoặc default
    /// </summary>
    public static async Task<T?> QueryFirstOrDefaultAsync<T>(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var parser = reader.GetRowParser<T>();
                return parser(reader);
            }
            
            return default;
        });
    }

    /// <summary>
    /// Execute raw SQL query và return dynamic objects
    /// </summary>
    public static async Task<IEnumerable<dynamic>> QueryAsync(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            using var reader = command.ExecuteReader();
            var parser = reader.GetRowParser<dynamic>();
            var results = new List<dynamic>();
            
            while (reader.Read())
            {
                results.Add(parser(reader));
            }
            
            return results;
        });
    }

    /// <summary>
    /// Execute SQL command (INSERT, UPDATE, DELETE) và return số rows affected
    /// </summary>
    public static async Task<int> ExecuteAsync(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            return command.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Execute scalar SQL query và return một giá trị duy nhất
    /// </summary>
    public static async Task<T?> ExecuteScalarAsync<T>(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            var result = command.ExecuteScalar();
            if (result == null || result is DBNull)
                return default;
            
            return (T)Convert.ChangeType(result, typeof(T));
        });
    }

    /// <summary>
    /// Synchronous version - Execute raw SQL query với Dapper mapping
    /// </summary>
    public static IEnumerable<T> Query<T>(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        context.Connection.Open();
        using var command = context.Connection.CreateCommand();
        command.CommandText = BuildSqlWithParameters(sql, parameters);
        if (commandTimeout.HasValue)
            command.CommandTimeout = commandTimeout.Value;
        
        using var reader = command.ExecuteReader();
        var parser = reader.GetRowParser<T>();
        var results = new List<T>();
        
        while (reader.Read())
        {
            results.Add(parser(reader));
        }
        
        return results;
    }

    /// <summary>
    /// Synchronous version - Execute SQL command
    /// </summary>
    public static int Execute(this TrinoDbContext context, string sql, object? parameters = null, int? commandTimeout = null)
    {
        context.Connection.Open();
        using var command = context.Connection.CreateCommand();
        command.CommandText = BuildSqlWithParameters(sql, parameters);
        if (commandTimeout.HasValue)
            command.CommandTimeout = commandTimeout.Value;
        
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Query với multi-mapping để map vào objects có navigation properties
    /// Dùng cho JOIN queries với 2 entities (Customer + Order)
    /// </summary>
    /// <typeparam name="T1">Entity thứ nhất (e.g., Customer)</typeparam>
    /// <typeparam name="T2">Entity thứ hai (e.g., Order)</typeparam>
    /// <typeparam name="TReturn">Return type có navigation properties</typeparam>
    /// <param name="context">DbContext</param>
    /// <param name="sql">SQL query - columns phải có prefix để phân biệt (c.*, o.*)</param>
    /// <param name="map">Function để combine 2 entities thành return object</param>
    /// <param name="splitOn">Column name để split giữa 2 entities (default: "Id")</param>
    /// <param name="parameters">Query parameters</param>
    /// <param name="commandTimeout">Timeout</param>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, TReturn>(
        this TrinoDbContext context, 
        string sql, 
        Func<T1, T2, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;
            
            using var reader = command.ExecuteReader();
            var splitIndex = GetSplitIndex(reader, splitOn);
            var parser1 = reader.GetRowParser<T1>(startIndex: 0, length: splitIndex);
            var parser2 = reader.GetRowParser<T2>(startIndex: splitIndex);
            var results = new List<TReturn>();
            
            while (reader.Read())
            {
                // Check if first column of entity1 is NULL (RIGHT JOIN with no match)
                bool isEntity1Null = reader.IsDBNull(0);
                var entity1 = isEntity1Null ? default(T1) : parser1(reader);
                
                // Check if first column of entity2 is NULL (LEFT JOIN with no match)
                bool isEntity2Null = reader.IsDBNull(splitIndex);
                var entity2 = isEntity2Null ? default(T2) : parser2(reader);
                
                results.Add(map(entity1, entity2));
            }
            
            return results;
        });
    }

    /// <summary>
    /// Helper để tìm index của split column
    /// Nếu column name xuất hiện nhiều lần (ví dụ: c.id và o.id), 
    /// sẽ tìm occurrence thứ 2 để đảm bảo split đúng
    /// </summary>
    private static int GetSplitIndex(IDataReader reader, string splitOn)
    {
        var splitColumns = splitOn.Split(',').Select(s => s.Trim()).ToArray();
        int firstMatchIndex = -1;
        
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            if (splitColumns.Any(s => columnName.Equals(s, StringComparison.OrdinalIgnoreCase)))
            {
                if (firstMatchIndex == -1)
                {
                    // Lưu index của match đầu tiên
                    firstMatchIndex = i;
                }
                else
                {
                    // Tìm thấy occurrence thứ 2 - đây là split point đúng
                    // Console.WriteLine($"Found duplicate column '{columnName}' at index {i} (first at {firstMatchIndex}), using index {i}");
                    return i;
                }
            }
        }
        
        // Nếu chỉ tìm thấy 1 occurrence, return nó
        if (firstMatchIndex >= 0)
        {
            // Console.WriteLine($"Only one occurrence of '{splitOn}' found at index {firstMatchIndex}");
            return firstMatchIndex;
        }
        
        // Default: split ở giữa
        return reader.FieldCount / 2;
    }

    /// <summary>
    /// Helper để tìm nhiều split indexes khi có nhiều split points
    /// </summary>
    private static List<int> GetSplitIndexes(IDataReader reader, string splitOn)
    {
        var indexes = new List<int>();
        var splitColumns = splitOn.Split(',').Select(s => s.Trim()).ToArray();
        
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            if (splitColumns.Any(s => columnName.Equals(s, StringComparison.OrdinalIgnoreCase)))
            {
                indexes.Add(i);
            }
        }
        
        // Skip the first occurrence - it belongs to the first entity (T1)
        if (indexes.Count > 0)
        {
            indexes.RemoveAt(0);
        }
        
        return indexes;
    }

    /// <summary>
    /// Query với multi-mapping cho 3 entities
    /// </summary>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, T3, TReturn>(
        this TrinoDbContext context,
        string sql,
        Func<T1, T2, T3, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            using var reader = command.ExecuteReader();
            var splits = GetSplitIndexes(reader, splitOn);
            var split1 = splits.Count > 0 ? splits[0] : reader.FieldCount / 3;
            var split2 = splits.Count > 1 ? splits[1] : (reader.FieldCount * 2) / 3;
            
            var parser1 = reader.GetRowParser<T1>(startIndex: 0, length: split1);
            var parser2 = reader.GetRowParser<T2>(startIndex: split1, length: split2 - split1);
            var parser3 = reader.GetRowParser<T3>(startIndex: split2);
            var results = new List<TReturn>();

            while (reader.Read())
            {
                bool isEntity1Null = reader.IsDBNull(0);
                var entity1 = isEntity1Null ? default(T1) : parser1(reader);
                
                bool isEntity2Null = reader.IsDBNull(split1);
                var entity2 = isEntity2Null ? default(T2) : parser2(reader);
                
                bool isEntity3Null = reader.IsDBNull(split2);
                var entity3 = isEntity3Null ? default(T3) : parser3(reader);
                
                results.Add(map(entity1, entity2, entity3));
            }

            return results;
        });
    }

    /// <summary>
    /// Query với multi-mapping cho 4 entities
    /// </summary>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, T3, T4, TReturn>(
        this TrinoDbContext context,
        string sql,
        Func<T1, T2, T3, T4, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            using var reader = command.ExecuteReader();
            var splits = GetSplitIndexes(reader, splitOn);
            var split1 = splits.Count > 0 ? splits[0] : reader.FieldCount / 4;
            var split2 = splits.Count > 1 ? splits[1] : (reader.FieldCount * 2) / 4;
            var split3 = splits.Count > 2 ? splits[2] : (reader.FieldCount * 3) / 4;
            
            var parser1 = reader.GetRowParser<T1>(startIndex: 0, length: split1);
            var parser2 = reader.GetRowParser<T2>(startIndex: split1, length: split2 - split1);
            var parser3 = reader.GetRowParser<T3>(startIndex: split2, length: split3 - split2);
            var parser4 = reader.GetRowParser<T4>(startIndex: split3);
            var results = new List<TReturn>();

            while (reader.Read())
            {
                bool isEntity1Null = reader.IsDBNull(0);
                var entity1 = isEntity1Null ? default(T1) : parser1(reader);
                
                bool isEntity2Null = reader.IsDBNull(split1);
                var entity2 = isEntity2Null ? default(T2) : parser2(reader);
                
                bool isEntity3Null = reader.IsDBNull(split2);
                var entity3 = isEntity3Null ? default(T3) : parser3(reader);
                
                bool isEntity4Null = reader.IsDBNull(split3);
                var entity4 = isEntity4Null ? default(T4) : parser4(reader);
                
                results.Add(map(entity1, entity2, entity3, entity4));
            }

            return results;
        });
    }

    /// <summary>
    /// Query với multi-mapping cho 5 entities
    /// </summary>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, T3, T4, T5, TReturn>(
        this TrinoDbContext context,
        string sql,
        Func<T1, T2, T3, T4, T5, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            using var reader = command.ExecuteReader();
            var splits = GetSplitIndexes(reader, splitOn);
            var split1 = splits.Count > 0 ? splits[0] : reader.FieldCount / 5;
            var split2 = splits.Count > 1 ? splits[1] : (reader.FieldCount * 2) / 5;
            var split3 = splits.Count > 2 ? splits[2] : (reader.FieldCount * 3) / 5;
            var split4 = splits.Count > 3 ? splits[3] : (reader.FieldCount * 4) / 5;
            
            var parser1 = reader.GetRowParser<T1>(startIndex: 0, length: split1);
            var parser2 = reader.GetRowParser<T2>(startIndex: split1, length: split2 - split1);
            var parser3 = reader.GetRowParser<T3>(startIndex: split2, length: split3 - split2);
            var parser4 = reader.GetRowParser<T4>(startIndex: split3, length: split4 - split3);
            var parser5 = reader.GetRowParser<T5>(startIndex: split4);
            var results = new List<TReturn>();

            while (reader.Read())
            {
                bool isEntity1Null = reader.IsDBNull(0);
                var entity1 = isEntity1Null ? default(T1) : parser1(reader);
                
                bool isEntity2Null = reader.IsDBNull(split1);
                var entity2 = isEntity2Null ? default(T2) : parser2(reader);
                
                bool isEntity3Null = reader.IsDBNull(split2);
                var entity3 = isEntity3Null ? default(T3) : parser3(reader);
                
                bool isEntity4Null = reader.IsDBNull(split3);
                var entity4 = isEntity4Null ? default(T4) : parser4(reader);
                
                bool isEntity5Null = reader.IsDBNull(split4);
                var entity5 = isEntity5Null ? default(T5) : parser5(reader);
                
                results.Add(map(entity1, entity2, entity3, entity4, entity5));
            }

            return results;
        });
    }

    /// <summary>
    /// Query với multi-mapping cho 6 entities
    /// </summary>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, T3, T4, T5, T6, TReturn>(
        this TrinoDbContext context,
        string sql,
        Func<T1, T2, T3, T4, T5, T6, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            using var reader = command.ExecuteReader();
            var splits = GetSplitIndexes(reader, splitOn);
            var split1 = splits.Count > 0 ? splits[0] : reader.FieldCount / 6;
            var split2 = splits.Count > 1 ? splits[1] : (reader.FieldCount * 2) / 6;
            var split3 = splits.Count > 2 ? splits[2] : (reader.FieldCount * 3) / 6;
            var split4 = splits.Count > 3 ? splits[3] : (reader.FieldCount * 4) / 6;
            var split5 = splits.Count > 4 ? splits[4] : (reader.FieldCount * 5) / 6;
            
            var parser1 = reader.GetRowParser<T1>(startIndex: 0, length: split1);
            var parser2 = reader.GetRowParser<T2>(startIndex: split1, length: split2 - split1);
            var parser3 = reader.GetRowParser<T3>(startIndex: split2, length: split3 - split2);
            var parser4 = reader.GetRowParser<T4>(startIndex: split3, length: split4 - split3);
            var parser5 = reader.GetRowParser<T5>(startIndex: split4, length: split5 - split4);
            var parser6 = reader.GetRowParser<T6>(startIndex: split5);
            var results = new List<TReturn>();

            while (reader.Read())
            {
                bool isEntity1Null = reader.IsDBNull(0);
                var entity1 = isEntity1Null ? default(T1) : parser1(reader);
                
                bool isEntity2Null = reader.IsDBNull(split1);
                var entity2 = isEntity2Null ? default(T2) : parser2(reader);
                
                bool isEntity3Null = reader.IsDBNull(split2);
                var entity3 = isEntity3Null ? default(T3) : parser3(reader);
                
                bool isEntity4Null = reader.IsDBNull(split3);
                var entity4 = isEntity4Null ? default(T4) : parser4(reader);
                
                bool isEntity5Null = reader.IsDBNull(split4);
                var entity5 = isEntity5Null ? default(T5) : parser5(reader);
                
                bool isEntity6Null = reader.IsDBNull(split5);
                var entity6 = isEntity6Null ? default(T6) : parser6(reader);
                
                results.Add(map(entity1, entity2, entity3, entity4, entity5, entity6));
            }

            return results;
        });
    }

    /// <summary>
    /// Query với multi-mapping cho 7 entities
    /// </summary>
    public static async Task<IEnumerable<TReturn>> QueryMultiMapAsync<T1, T2, T3, T4, T5, T6, T7, TReturn>(
        this TrinoDbContext context,
        string sql,
        Func<T1, T2, T3, T4, T5, T6, T7, TReturn> map,
        string splitOn = "id",
        object? parameters = null,
        int? commandTimeout = null)
    {
        return await Task.Run(() =>
        {
            context.Connection.Open();
            using var command = context.Connection.CreateCommand();
            command.CommandText = BuildSqlWithParameters(sql, parameters);
            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            using var reader = command.ExecuteReader();
            var splits = GetSplitIndexes(reader, splitOn);
            var parser1 = reader.GetRowParser<T1>();
            var parser2 = reader.GetRowParser<T2>(startIndex: splits.Count > 0 ? splits[0] : reader.FieldCount / 7);
            var parser3 = reader.GetRowParser<T3>(startIndex: splits.Count > 1 ? splits[1] : (reader.FieldCount * 2) / 7);
            var parser4 = reader.GetRowParser<T4>(startIndex: splits.Count > 2 ? splits[2] : (reader.FieldCount * 3) / 7);
            var parser5 = reader.GetRowParser<T5>(startIndex: splits.Count > 3 ? splits[3] : (reader.FieldCount * 4) / 7);
            var parser6 = reader.GetRowParser<T6>(startIndex: splits.Count > 4 ? splits[4] : (reader.FieldCount * 5) / 7);
            var parser7 = reader.GetRowParser<T7>(startIndex: splits.Count > 5 ? splits[5] : (reader.FieldCount * 6) / 7);
            var results = new List<TReturn>();

            while (reader.Read())
            {
                var entity1 = parser1(reader);
                var entity2 = parser2(reader);
                var entity3 = parser3(reader);
                var entity4 = parser4(reader);
                var entity5 = parser5(reader);
                var entity6 = parser6(reader);
                var entity7 = parser7(reader);
                results.Add(map(entity1, entity2, entity3, entity4, entity5, entity6, entity7));
            }

            return results;
        });
    }
}
