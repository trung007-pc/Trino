using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EntityFramework.Trino.Context;

namespace EntityFramework.Trino.Extensions;

/// <summary>
/// Extension methods for setting up Trino services - giống AddDbContext() trong EF
/// </summary>
public static class TrinoServiceCollectionExtensions
{
    /// <summary>
    /// Registers a TrinoDbContext as a service in the IServiceCollection
    /// </summary>
    public static IServiceCollection AddTrinoDbContext<TContext>(
        this IServiceCollection services,
        Action<TrinoDbContextOptions> optionsAction)
        where TContext : TrinoDbContext
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (optionsAction == null)
            throw new ArgumentNullException(nameof(optionsAction));

        // Create and configure options
        var options = new TrinoDbContextOptions<TContext>();
        optionsAction(options);

        // Register options as singleton
        services.TryAddSingleton(options);

        // Register context as scoped (giống EF)
        services.TryAddScoped<TContext>(provider =>
        {
            var contextOptions = provider.GetRequiredService<TrinoDbContextOptions<TContext>>();
            
            // Build connection properties
            var optionsBuilder = new TrinoDbContextOptionsBuilder();
            var properties = optionsBuilder
                .UseTrino(
                    contextOptions.Host,
                    contextOptions.Port,
                    contextOptions.Catalog,
                    contextOptions.Schema,
                    contextOptions.User
                )
                .WithTestConnection(false)
                .Build();
            
            // Set EnableSqlLogging from options
            //properties.EnableSqlLogging = contextOptions.EnableSqlLogging;
            
            // Create context with properties
            return (TContext)Activator.CreateInstance(typeof(TContext), properties)!;
        });

        return services;
    }

    /// <summary>
    /// Registers a TrinoDbContext with connection string
    /// </summary>
    public static IServiceCollection AddTrinoDbContext<TContext>(
        this IServiceCollection services,
        string host,
        int port,
        string catalog,
        string schema,
        string user = "admin")
        where TContext : TrinoDbContext
    {
        return services.AddTrinoDbContext<TContext>(options =>
        {
            options.Host = host;
            options.Port = port;
            options.Catalog = catalog;
            options.Schema = schema;
            options.User = user;
        });
    }
}
