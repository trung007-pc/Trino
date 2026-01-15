using System.Text.RegularExpressions;

namespace EntityFramework.Trino.Dapper;

/// <summary>
/// SQL Builder để xây dựng câu truy vấn động với điều kiện WHERE và tham số
/// Hỗ trợ SQL injection protection
/// </summary>
public class TrinoSqlBuilder
{
    private string _sql;
    private readonly Dictionary<string, object?> _parameters = new();
    private readonly List<string> _whereConditions = new();

    public TrinoSqlBuilder(string sql)
    {
        _sql = sql ?? throw new ArgumentNullException(nameof(sql));
    }

    /// <summary>
    /// Thêm điều kiện WHERE nếu condition = true
    /// </summary>
    /// <param name="condition">Điều kiện để thêm WHERE clause</param>
    /// <param name="whereClause">Câu WHERE với named parameter (VD: "name LIKE @name")</param>
    /// <returns>Builder instance để chain</returns>
    public TrinoSqlBuilder WhereIf(bool condition, string whereClause)
    {
        if (condition && !string.IsNullOrWhiteSpace(whereClause))
        {
            _whereConditions.Add(whereClause);
        }
        return this;
    }

    /// <summary>
    /// Thêm tham số từ một object (anonymous object hoặc dictionary)
    /// VD: .WithParams(new { id = 1, name = "John" })
    /// </summary>
    /// <param name="parameters">Object chứa các tham số</param>
    /// <returns>Builder instance để chain</returns>
    public TrinoSqlBuilder WithParams(object parameters)
    {
        if (parameters == null) return this;

        // Xử lý Dictionary<string, object>
        if (parameters is IDictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                _parameters[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            // Xử lý anonymous object hoặc POCO
            var properties = parameters.GetType().GetProperties();
            foreach (var prop in properties)
            {
                _parameters[prop.Name] = prop.GetValue(parameters);
            }
        }

        return this;
    }

    /// <summary>
    /// Build SQL cuối cùng với tất cả WHERE conditions và parameters được escape
    /// </summary>
    /// <returns>SQL string với giá trị đã được escape (SQL injection safe)</returns>
    public string Build()
    {
        var finalSql = _sql;

        // Thêm WHERE conditions nếu có
        if (_whereConditions.Any())
        {
            // Tìm vị trí để insert WHERE clause (trước ORDER BY, LIMIT, OFFSET, FETCH)
            var upperSql = finalSql.ToUpper();
            var insertPosition = finalSql.Length;
            
            // Tìm các keywords phải đứng sau WHERE
            var keywords = new[] { " ORDER BY ", " LIMIT ", " OFFSET ", " FETCH " };
            foreach (var keyword in keywords)
            {
                var pos = upperSql.LastIndexOf(keyword);
                if (pos >= 0 && pos < insertPosition)
                {
                    insertPosition = pos;
                }
            }
            
            // Check xem đã có WHERE chưa
            var whereKeyword = upperSql.Contains(" WHERE ") ? "AND" : "WHERE";
            
            // Insert WHERE clause vào đúng vị trí
            var whereClause = $" {whereKeyword} {string.Join(" AND ", _whereConditions)} ";
            finalSql = finalSql.Insert(insertPosition, whereClause);
        }

        // Thay thế tất cả named parameters với giá trị đã escape
        foreach (var param in _parameters)
        {
            var escapedValue = EscapeValue(param.Value);
            var pattern = $@"@{param.Key}\b";

            finalSql = Regex.Replace(
                finalSql,
                pattern,
                escapedValue,
                RegexOptions.IgnoreCase
            );
        }

        return finalSql;
    }

    /// <summary>
    /// Escape giá trị để tránh SQL injection
    /// </summary>
    private static string EscapeValue(object? value)
    {
        if (value == null || value is DBNull)
            return "NULL";

        return value switch
        {
            string str => $"'{str.Replace("'", "''")}'",
            DateTime dt => $"TIMESTAMP '{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"DATE '{d:yyyy-MM-dd}'",
            TimeOnly t => $"TIME '{t:HH:mm:ss}'",
            bool b => b ? "true" : "false",
            decimal or double or float => value.ToString()!.Replace(",", "."),
            byte or short or int or long => value.ToString()!,
            _ => $"'{value.ToString()?.Replace("'", "''") ?? ""}'"
        };
    }

}
