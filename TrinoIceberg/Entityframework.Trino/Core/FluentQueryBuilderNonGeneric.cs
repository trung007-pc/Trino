using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Trino.Data.ADO.Server;

namespace EntityFramework.Trino.Core;

/// <summary>
/// Non-generic fluent query builder - type được infer từ Select()
/// </summary>
public class FluentQueryBuilder
{
    private readonly TrinoConnection _connection;
    private readonly string _fromClause;
    private readonly bool _enableSqlLogging;
    private List<string> _whereClauses = new();
    private Dictionary<string, object?>? _parameters;

    public FluentQueryBuilder(TrinoConnection connection, string fromClause, bool enableSqlLogging)
    {
        _connection = connection;
        _fromClause = fromClause;
        _enableSqlLogging = enableSqlLogging;
    }

    /// <summary>
    /// Add parameters để chống SQL injection
    /// </summary>
    public FluentQueryBuilder WithParams(object parameters)
    {
        _parameters = new Dictionary<string, object?>();
        
        foreach (var prop in parameters.GetType().GetProperties())
        {
            _parameters[prop.Name] = prop.GetValue(parameters);
        }
        
        return this;
    }

    public FluentQueryBuilder WhereIf(bool condition, string whereClause)
    {
        if (condition && !string.IsNullOrWhiteSpace(whereClause))
        {
            _whereClauses.Add(whereClause);
        }
        return this;
    }

    /// <summary>
    /// Select với 1 table - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, TResult>(Expression<Func<T1, TResult>> selector)
        where T1 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 2 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, TResult>(Expression<Func<T1, T2, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 3 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, TResult>(Expression<Func<T1, T2, T3, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 4 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, TResult>(Expression<Func<T1, T2, T3, T4, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 5 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5, TResult>(Expression<Func<T1, T2, T3, T4, T5, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 6 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5, T6, TResult>(Expression<Func<T1, T2, T3, T4, T5, T6, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        where T6 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Select với 7 tables - Type được infer tự động
    /// </summary>
    public FluentQueryBuilder<TResult> Select<T1, T2, T3, T4, T5, T6, T7, TResult>(Expression<Func<T1, T2, T3, T4, T5, T6, T7, TResult>> selector)
        where T1 : class, new()
        where T2 : class, new()
        where T3 : class, new()
        where T4 : class, new()
        where T5 : class, new()
        where T6 : class, new()
        where T7 : class, new()
        where TResult : class
    {
        var builder = new FluentQueryBuilder<TResult>(_connection, _fromClause, _enableSqlLogging);
        if (_parameters != null) builder.WithParams(_parameters);
        if (_whereClauses.Count > 0) builder.WithWhereClauses(_whereClauses);
        return builder.Select(selector);
    }

    /// <summary>
    /// Internal method để pass parameters sang generic builder
    /// </summary>
    internal void CopyParametersTo(FluentQueryBuilder<object> builder)
    {
        if (_parameters != null)
        {
            builder.WithParams(_parameters);
        }
    }
}
