using EntityFramework.Trino.Iceberg;
using Microsoft.AspNetCore.Mvc;
using EntityFramework.Trino.Iceberg.Models;
using EntityFramework.Trino.Dapper;

namespace TrinoIceberg.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly IcebergDbContext _context;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(IcebergDbContext context, ILogger<CustomersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all customers with optional filtering using TrinoSqlBuilder and WhereIf
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Customer>>> GetCustomers(
        [FromQuery] string? name = null,
        [FromQuery] string? email = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var sql = new TrinoSqlBuilder($@"
                SELECT id, name, email 
                FROM customers
                ORDER BY name
                LIMIT @limit")
                .WhereIf(!string.IsNullOrEmpty(name), "LOWER(name) LIKE LOWER(@namePattern)")
                .WhereIf(!string.IsNullOrEmpty(email), "email LIKE @emailPattern")
                .WithParams(new 
                { 
                    namePattern = $"%{name}%",
                    emailPattern = $"%{email}%",
                    limit = limit
                })
                .Build();

            var customers = await _context.QueryAsync<Customer>(sql);
            
            return Ok(customers.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get customers with their orders (Include pattern: Customer + List of Orders)
    /// </summary>
    [HttpGet("with-orders")]
    public async Task<ActionResult<List<CustomerWithOrders>>> GetCustomersWithOrders(
        [FromQuery] string? customerName = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] int limit = 50)
    {
        try
        {
            var sql = new TrinoSqlBuilder(@"
                SELECT 
                    c.*,o.*
                FROM customers c
                LEFT JOIN orders o ON c.id = o.customerid
                ORDER BY c.id, o.orderdate
                LIMIT @limit
                ")
                .WhereIf(!string.IsNullOrEmpty(customerName), "LOWER(c.name) LIKE LOWER(@namePattern)")
                .WhereIf(minAmount.HasValue, "o.amount >= @minAmount")
                .WithParams(new 
                { 
                    namePattern = $"%{customerName}%",
                    minAmount = minAmount ?? 0,
                    limit = limit
                })
                .Build();

            var flatResults = await _context.QueryMultiMapAsync<Customer, Order, CustomerWithNavigationProperty>(
                sql,
                (customer, order) => new CustomerWithNavigationProperty
                {
                    Customer = customer,
                    Order = order
                }
            );

            // Group by customer to create Include-like structure
            var customersWithOrders = flatResults
                .GroupBy(r => r.Customer.Id)
                .Select(g => new CustomerWithOrders
                {
                    Customer = g.First().Customer,
                    Orders = g.Where(r => r.Order != null && !string.IsNullOrEmpty(r.Order.Id))
                             .Select(r => r.Order)
                             .ToList()
                })
                .ToList();
            
            return Ok(customersWithOrders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers with orders");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get customer aggregated data (GROUP BY)
    /// </summary>
    [HttpGet("aggregated")]
    public async Task<ActionResult<List<CustomerAggregateDto>>> GetCustomersAggregated(
        [FromQuery] int minOrders = 1,
        [FromQuery] int limit = 10)
    {
        try
        {
            var sql = new TrinoSqlBuilder(@"
                SELECT 
                    c.name as CustomerName,
                    COUNT(o.id) as TotalOrders,
                    SUM(o.amount) as TotalAmount,
                    AVG(o.amount) as AvgAmount
                FROM customers c
                LEFT JOIN orders o ON c.id = o.customerid
                GROUP BY c.name
                HAVING COUNT(o.id) > @minOrders
                ORDER BY TotalAmount DESC
                LIMIT @limit")
                .WithParams(new {minOrders = minOrders, limit = limit })
                .Build();

            var results = await _context.QueryAsync<CustomerAggregateDto>(sql);
            
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting aggregated customer data");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    /// <summary>
    /// Get customer by ID using TrinoSqlBuilder
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(string id)
    {
        try
        {
            var sql = new TrinoSqlBuilder("SELECT id, name, email FROM customers")
                .WhereIf(true, "id = @id")
                .WithParams(new { id })
                .Build();

            var customers = await _context.QueryAsync<Customer>(sql);
            var customer = customers.FirstOrDefault();

            if (customer == null)
                return NotFound(new { message = $"Customer {id} not found" });

            return Ok(customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer {Id}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create new customer using AddAsync
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Customer>> CreateCustomer([FromBody] CreateUpdateCustomerRequest request)
    {
        try
        {
            var customer = new Customer
            {
                Id = $"CUST_{Guid.NewGuid():N}",
                Name = request.Name,
                Email = request.Email
            };
            
            await _context.Customers.AddAsync(customer);
            
            return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Bulk insert customers using AddRangeAsync
    /// </summary>
    [HttpPost("bulk")]
    public async Task<ActionResult> CreateCustomers([FromBody] List<CreateUpdateCustomerRequest> requests)
    {
        try
        {
            var customers = requests.Select(r => new Customer
            {
                Id = $"CUST_{Guid.NewGuid():N}",
                Name = r.Name,
                Email = r.Email
            }).ToList();
            
            await _context.Customers.AddRangeAsync(customers);
            
            return Ok(new { message = $"Created {customers.Count} customers", customerIds = customers.Select(c => c.Id) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customers");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update customer using UpdateAsync
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(string id, [FromBody] CreateUpdateCustomerRequest request)
    {
        try
        {
            // Check if exists first (optional - Iceberg allows update even if not exists)
            var checkSql = new TrinoSqlBuilder("SELECT id FROM customers")
                .WhereIf(true, "id = @id")
                .WithParams(new { id })
                .Build();
            
            var existing = await _context.QueryAsync<Customer>(checkSql);
            if (!existing.Any())
                return NotFound(new { message = $"Customer {id} not found" });

            var customer = new Customer
            {
                Id = id,
                Name = request.Name,
                Email = request.Email
            };
            
            await _context.Customers.UpdateAsync(customer);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating customer {Id}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete customer using TrinoSqlBuilder
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(string id)
    {
        try
        {
            var sql = new TrinoSqlBuilder("DELETE FROM customers")
                .WhereIf(true, "id = @id")
                .WithParams(new { id })
                .Build();
            
            await _context.ExecuteAsync(sql);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting customer {Id}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
