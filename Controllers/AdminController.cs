using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(AppDbContext context, ILogger<AdminController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // =========================================
        // GET: api/admin/pedidos
        // =========================================
        [HttpGet("pedidos")]
        public async Task<IActionResult> ObtenerPedidos()
        {
            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.DetallePedidos)
                        .ThenInclude(d => d.IdProductoNavigation)
                    .Include(p => p.IdClienteNavigation)
                    .OrderByDescending(p => p.FechaPedido)
                    .ToListAsync();

                return Ok(pedidos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener los pedidos.");
                return StatusCode(500, "Error interno al obtener los pedidos.");
            }
        }

        // =========================================
        // PUT: api/admin/cambiar-estado
        // =========================================
        [HttpPut("cambiar-estado")]
        public async Task<IActionResult> CambiarEstadoPedido([FromBody] CambiarEstadoRequest request)
        {
            try
            {
                if (request == null || request.NumPedido <= 0 || string.IsNullOrWhiteSpace(request.NuevoEstado))
                    return BadRequest("Datos inválidos para actualizar el estado.");

                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null)
                    return NotFound("Pedido no encontrado.");

                var estadosValidos = new[] { "Pendiente", "En preparación", "En reparto", "Entregado" };
                if (!estadosValidos.Contains(request.NuevoEstado))
                    return BadRequest("Estado no válido.");

                pedido.EstadoPedido = request.NuevoEstado;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Estado actualizado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del pedido.");
                return StatusCode(500, "Error interno al actualizar el estado.");
            }
        }

        // =========================================
        // CLASE AUXILIAR
        // =========================================
        public class CambiarEstadoRequest
        {
            public int NumPedido { get; set; }
            public string? NuevoEstado { get; set; }
        }
    }
}
