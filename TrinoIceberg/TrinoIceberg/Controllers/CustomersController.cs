using EntityFramework.Trino.Iceberg;
using Microsoft.AspNetCore.Mvc;
using EntityFramework.Trino.Iceberg.Models;

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
    /// Get all customers with optional filtering using WhereIf
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Customer>>> GetCustomers(
        [FromQuery] string? name = null,
        [FromQuery] string? email = null,
        [FromQuery] int? limit = null)
    {
        try
        {
            // Note: Trino SQL is case-sensitive by default
            // Use exact match or implement case-insensitive search server-side
            var query = _context.Customers
                .WhereIf(!string.IsNullOrEmpty(name), c => c.Name.ToLower().Contains(name!.ToLower()))
                .WhereIf(!string.IsNullOrEmpty(email), c => c.Email.Contains(email!));

            if (limit.HasValue)
                query = query.Take(limit.Value);

            var customers = await query.ToListAsync();
            
            return Ok(customers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get all customers with optional filtering using WhereIf
    /// </summary>
    [HttpGet("detail")]
    public async Task<ActionResult<List<Customer>>> GetCustomers(
        [FromQuery] string? name = null)
    {
        try
        {
            var result = await _context.Query("FROM customers c JOIN orders o ON c.Id = o.CustomerId")
                .WhereIf(!string.IsNullOrEmpty(name), "c.Name LIKE @name")
                .WithParams(new { name = $"%{name}%"})
                .Select((Customer c, Order o) => new CustomerWithNavigationProperty()
                {
                    Customer = c,
                    Order = o
                })
                .ExecuteAsync();
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    
    
    /// <summary>
    /// Get customer by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(string id)
    {
        try
        {
            var customer = await _context.Customers
                .Where(c => c.Id == id)
                .FirstOrDefaultAsync();

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
    /// Create new customer
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Customer>> CreateCustomer([FromBody] CreateUpdateCustomerRequest request)
    {
        try
        {
            var customer = new Customer()
            {
                Name = request.Name,
                Email = request.Email
            };
            customer.Id = $"CUST_{Guid.NewGuid():N}";
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
    /// Update customer
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(string id, [FromBody] CreateUpdateCustomerRequest request)
    {
        try
        {
            var customer = new Customer()
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
    /// Delete customer
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(string id)
    {
        try
        {
            await _context.Customers
                .Where(c => c.Id == id)
                .DeleteAsync();
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting customer {Id}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
