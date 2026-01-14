// using EntityFramework.Trino.Iceberg;
// using EntityFramework.Trino.Iceberg.Models;
// using Microsoft.AspNetCore.Mvc;
//
// namespace TrinoIceberg.Controllers;
//
// [ApiController]
// [Route("api/[controller]")]
// public class OrdersController : ControllerBase
// {
//     private readonly IcebergDbContext _context;
//     private readonly ILogger<OrdersController> _logger;
//
//     public OrdersController(IcebergDbContext context, ILogger<OrdersController> logger)
//     {
//         _context = context;
//         _logger = logger;
//     }
//
//     /// <summary>
//     /// Get orders with dynamic filtering using WhereIf
//     /// </summary>
//     [HttpGet]
//     public async Task<ActionResult<List<Order>>> GetOrders(
//         [FromQuery] string? customerId = null,
//         [FromQuery] string? status = null,
//         [FromQuery] decimal? minAmount = null,
//         [FromQuery] DateTime? fromDate = null,
//         [FromQuery] DateTime? toDate = null,
//         [FromQuery] int? limit = null)
//     {
//         try
//         {
//             var query = _context.Orders
//                 .WhereIf(!string.IsNullOrEmpty(customerId), o => o.CustomerId == customerId!)
//                 .WhereIf(!string.IsNullOrEmpty(status), o => o.Status == status!)
//                 .WhereIf(minAmount.HasValue, o => o.Amount >= minAmount!.Value)
//                 .WhereIf(fromDate.HasValue, o => o.OrderDate >= fromDate!.Value)
//                 .WhereIf(toDate.HasValue, o => o.OrderDate <= toDate!.Value);
//
//             if (limit.HasValue)
//                 query = query.Take(limit.Value);
//
//             var orders = await query.ToListAsync();
//             return Ok(orders);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error getting orders");
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Get order by ID
//     /// </summary>
//     [HttpGet("{id}")]
//     public async Task<ActionResult<Order>> GetOrder(string id)
//     {
//         try
//         {
//             var order = await _context.Orders
//                 .Where(o => o.Id == id)
//                 .FirstOrDefaultAsync();
//
//             if (order == null)
//                 return NotFound(new { message = $"Order {id} not found" });
//
//             return Ok(order);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error getting order {Id}", id);
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Get customer orders with JOIN - demonstrates Raw SQL Query Builder
//     /// </summary>
//     [HttpGet("customer-orders")]
//     public async Task<ActionResult<List<CustomerOrderDto>>> GetCustomerOrders(
//         [FromQuery] string? customerName = null,
//         [FromQuery] decimal? minAmount = null,
//         [FromQuery] string? status = null)
//     {
//         try
//         {
//             var results = await _context.Query<CustomerOrderDto>(
//                 @"SELECT c.Name AS CustomerName, o.Id AS OrderId, o.Amount, o.Status
//                   FROM customers c
//                   INNER JOIN orders o ON c.Id = o.CustomerId")
//                 .WithParams(new
//                 {
//                     customerName = customerName != null ? $"%{customerName}%" : null,
//                     minAmount = minAmount,
//                     status = status
//                 })
//                 .WhereIf(!string.IsNullOrEmpty(customerName), "c.Name LIKE @customerName")
//                 .WhereIf(minAmount.HasValue, "o.Amount >= @minAmount")
//                 .WhereIf(!string.IsNullOrEmpty(status), "o.Status = @status")
//                 .OrderBy("o.Amount DESC")
//                 .ExecuteAsync();
//
//             return Ok(results);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error getting customer orders");
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Get detailed order items (3-table JOIN)
//     /// </summary>
//     [HttpGet("order-details")]
//     public async Task<ActionResult<List<CustomerOrderItemDto>>> GetOrderDetails(
//         [FromQuery] string? customerName = null,
//         [FromQuery] string? productName = null)
//     {
//         try
//         {
//             var results = await _context.Query<CustomerOrderItemDto>(
//                 @"SELECT c.Name AS CustomerName, o.Id AS OrderId, 
//                          oi.ProductName, oi.Price, oi.Quantity
//                   FROM customers c
//                   INNER JOIN orders o ON c.Id = o.CustomerId
//                   INNER JOIN order_items oi ON o.Id = oi.OrderId")
//                 .WithParams(new
//                 {
//                     customerName = customerName != null ? $"%{customerName}%" : null,
//                     productName = productName != null ? $"%{productName}%" : null
//                 })
//                 .WhereIf(!string.IsNullOrEmpty(customerName), "c.Name LIKE @customerName")
//                 .WhereIf(!string.IsNullOrEmpty(productName), "oi.ProductName LIKE @productName")
//                 .ExecuteAsync();
//
//             return Ok(results);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error getting order details");
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Create new order
//     /// </summary>
//     [HttpPost]
//     public async Task<ActionResult<Order>> CreateOrder([FromBody] Order order)
//     {
//         try
//         {
//             order.Id = $"ORD_{Guid.NewGuid():N}";
//             order.OrderDate = DateTime.UtcNow;
//             await _context.Orders.AddAsync(order);
//             
//             return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error creating order");
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Update order
//     /// </summary>
//     [HttpPut("{id}")]
//     public async Task<IActionResult> UpdateOrder(string id, [FromBody] Order order)
//     {
//         try
//         {
//             order.Id = id;
//             await _context.Orders.UpdateAsync(order);
//             return NoContent();
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error updating order {Id}", id);
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
//
//     /// <summary>
//     /// Delete order
//     /// </summary>
//     [HttpDelete("{id}")]
//     public async Task<IActionResult> DeleteOrder(string id)
//     {
//         try
//         {
//             await _context.Orders
//                 .Where(o => o.Id == id)
//                 .DeleteAsync();
//             
//             return NoContent();
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error deleting order {Id}", id);
//             return StatusCode(500, new { error = ex.Message });
//         }
//     }
// }
