using System.Data;
using Dapper;
using Trino.Client.Types;

namespace EntityFramework.Trino.Dapper;

/// <summary>
/// Custom type handler cho Dapper để xử lý DateTime với Trino
/// Trino stores dates as strings, cần convert giữa string và DateTime
/// </summary>
public class TrinoDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.Value = value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public override DateTime Parse(object value)
    {
        if (value is DateTime dt)
            return dt;
        
        if (value is string str)
        {
            if (DateTime.TryParse(str, out var result))
                return result;
        }

        return default;
    }
}

/// <summary>
/// Custom type handler cho Dapper để xử lý nullable DateTime với Trino
/// </summary>
public class TrinoNullableDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
{
    public override void SetValue(IDbDataParameter parameter, DateTime? value)
    {
        parameter.Value = value?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value;
    }

    public override DateTime? Parse(object value)
    {
        if (value == null || value is DBNull)
            return null;

        if (value is DateTime dt)
            return dt;
        
        if (value is string str)
        {
            if (DateTime.TryParse(str, out var result))
                return result;
        }

        return null;
    }
}

/// <summary>
/// Custom type handler cho Dapper để xử lý Decimal với Trino
/// Trino returns TrinoBigDecimal, cần convert sang System.Decimal
/// </summary>
public class TrinoDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    public override void SetValue(IDbDataParameter parameter, decimal value)
    {
        parameter.Value = value;
    }

    public override decimal Parse(object value)
    {
        if (value is decimal dec)
            return dec;
        
        if (value is TrinoBigDecimal trinoDec)
            return decimal.Parse(trinoDec.ToString());
        
        if (value is string str && decimal.TryParse(str, out var result))
            return result;
        
        return default;
    }
}

/// <summary>
/// Custom type handler cho Dapper để xử lý nullable Decimal với Trino
/// </summary>
public class TrinoNullableDecimalHandler : SqlMapper.TypeHandler<decimal?>
{
    public override void SetValue(IDbDataParameter parameter, decimal? value)
    {
        parameter.Value = value ?? (object)DBNull.Value;
    }

    public override decimal? Parse(object value)
    {
        if (value == null || value is DBNull)
            return null;
        
        if (value is decimal dec)
            return dec;
        
        if (value is TrinoBigDecimal trinoDec)
            return decimal.Parse(trinoDec.ToString());
        
        if (value is string str && decimal.TryParse(str, out var result))
            return result;
        
        if (value is double dbl)
            return (decimal)dbl;
        
        if (value is float flt)
            return (decimal)flt;
        
        if (value is int i)
            return i;
        
        if (value is long l)
            return l;

        return null;
    }
}

/// <summary>
/// Helper class để register tất cả Trino type handlers cho Dapper
/// </summary>
public static class TrinoTypeHandlers
{
    private static bool _initialized = false;
    private static readonly object _lock = new object();

    /// <summary>
    /// Register các custom type handlers cho Trino với Dapper
    /// Chỉ cần gọi một lần khi application khởi động
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            SqlMapper.AddTypeHandler(new TrinoDateTimeHandler());
            SqlMapper.AddTypeHandler(new TrinoNullableDateTimeHandler());
            SqlMapper.AddTypeHandler(new TrinoDecimalHandler());
            SqlMapper.AddTypeHandler(new TrinoNullableDecimalHandler());

            _initialized = true;
        }
    }
}
