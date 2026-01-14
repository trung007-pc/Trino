using EntityFramework.Trino.Extensions;
using EntityFramework.Trino.Iceberg;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Trino Iceberg API",
        Version = "v1",
        Description = "ASP.NET Core Web API with Trino + Apache Iceberg using EntityFramework.Trino"
    });
});

// Configure Trino DbContext - Simplified!
var trinoConfig = builder.Configuration.GetSection("Trino");
builder.Services.AddTrinoDbContext<IcebergDbContext>(options =>
{
    options.Host = trinoConfig["Host"] ?? "localhost";
    options.Port = trinoConfig.GetValue<int>("Port", 8081);
    options.Catalog = trinoConfig["Catalog"] ?? "iceberg";
    options.Schema = trinoConfig["Schema"] ?? "v4";
    options.User = trinoConfig["User"] ?? "trino";
    options.EnableSqlLogging = trinoConfig.GetValue<bool>("EnableSqlLogging", false);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Trino Iceberg API v1");
        options.RoutePrefix = string.Empty; // Set Swagger UI at root
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();