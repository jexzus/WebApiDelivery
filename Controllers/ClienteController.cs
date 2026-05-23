using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using WebApiDelivery.Data;
using WebApiDelivery.Hubs;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClienteController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<DeliveryHub> _hub;
        private readonly ILogger<ClienteController> _logger;
        private static readonly ConcurrentDictionary<int, List<DetallePedido>> _carritos = new();

        public ClienteController(AppDbContext context, IHubContext<DeliveryHub> hub, ILogger<ClienteController> logger)
        {
            _context = context;
            _hub = hub;
            _logger = logger;
        }

        // 1. GET: api/cliente/catalogo
        [HttpGet("catalogo")]
        public async Task<IActionResult> ObtenerCatalogo()
        {
            var productos = await _context.Productos.ToListAsync();
            return Ok(productos);
        }

        // 2. POST: api/cliente/carrito/agregar
        [HttpPost("carrito/agregar")]
        public IActionResult AgregarAlCarrito([FromBody] AgregarCarritoRequest request)
        {
            var producto = _context.Productos.FirstOrDefault(p => p.IdProducto == request.IdProducto);
            if (producto == null) return NotFound("Producto no encontrado");

            var carrito = _carritos.GetOrAdd(request.IdUsuario, _ => new List<DetallePedido>());

            lock (carrito)
            {
                var existente = carrito.FirstOrDefault(p => p.IdProducto == request.IdProducto);
                if (existente != null)
                    existente.Cantidad += request.Cantidad;
                else
                    carrito.Add(new DetallePedido
                    {
                        IdProducto = request.IdProducto,
                        Cantidad = request.Cantidad,
                        PrecioUnitario = producto.Precio,
                        IdProductoNavigation = producto
                    });

                return Ok(carrito.ToList());
            }
        }

        // 3. POST: api/cliente/carrito/eliminar
        [HttpPost("carrito/eliminar")]
        public IActionResult EliminarDelCarrito([FromBody] EliminarCarritoRequest request)
        {
            if (!_carritos.TryGetValue(request.IdUsuario, out var carrito))
                return BadRequest("Carrito vacío");

            lock (carrito)
            {
                var index = carrito.FindIndex(p => p.IdProducto == request.IdProducto);
                if (index >= 0) carrito.RemoveAt(index);
                return Ok(carrito.ToList());
            }
        }

        // 4. GET: api/cliente/carrito?idUsuario=5
        [HttpGet("carrito")]
        public IActionResult VerCarrito([FromQuery] int idUsuario)
        {
            if (!_carritos.TryGetValue(idUsuario, out var carrito))
                return Ok(new List<DetallePedido>());

            lock (carrito) { return Ok(carrito.ToList()); }
        }

        // 5. POST: api/cliente/confirmar-pedido
        [HttpPost("confirmar-pedido")]
        public async Task<IActionResult> ConfirmarPedido([FromBody] ConfirmarPedidoRequest request)
        {
            if (!_carritos.TryGetValue(request.IdUsuario, out var carrito))
                return BadRequest("Carrito vacío");

            var usuario = await _context.Usuarios.FindAsync(request.IdUsuario);
            if (usuario == null) return NotFound("Usuario no encontrado");

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == request.IdUsuario);
            if (cliente == null) return NotFound("Cliente no encontrado");

            List<DetallePedido> snapshot;
            lock (carrito) { snapshot = carrito.ToList(); }

            var total = snapshot.Sum(c => c.Cantidad * c.PrecioUnitario);

            var pedido = new Pedido
            {
                IdCliente = cliente.IdCliente,
                FechaPedido = DateTime.Now,
                EstadoPedido = "Pendiente",
                Observaciones = request.Observaciones,
                ModoEntrega = request.ModoEntrega,
                MontoTotal = total
            };

            _context.Pedidos.Add(pedido);
            await _context.SaveChangesAsync();

            foreach (var item in snapshot)
            {
                _context.DetallesPedido.Add(new DetallePedido
                {
                    NumPedido = pedido.NumPedido,
                    IdProducto = item.IdProducto,
                    Cantidad = item.Cantidad,
                    PrecioUnitario = item.PrecioUnitario
                });
            }

            await _context.SaveChangesAsync();
            _carritos.TryRemove(request.IdUsuario, out _);

            try
            {
                await _hub.Clients.Group("Admins").SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "Pendiente",
                    estadoAnterior = "",
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor = (int?)null,
                    idCliente = pedido.IdCliente
                });
            }
            catch (Exception ex) { _logger.LogWarning(ex, "SignalR error al notificar nuevo pedido {NumPedido}", pedido.NumPedido); }

            return Ok(new { message = "Pedido confirmado exitosamente." });
        }

        // 6. POST: api/cliente/cancelar-pedido
        [HttpPost("cancelar-pedido")]
        public async Task<IActionResult> CancelarPedido([FromBody] CancelarPedidoClienteRequest request)
        {
            try
            {
                var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == request.IdUsuario);
                if (cliente == null) return NotFound("Cliente no encontrado.");

                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido && p.IdCliente == cliente.IdCliente);
                if (pedido == null) return NotFound("Pedido no encontrado.");

                if (pedido.EstadoPedido != "Pendiente")
                    return BadRequest($"El pedido está en '{pedido.EstadoPedido}' y ya no puede cancelarse.");

                pedido.EstadoPedido = "Cancelado";
                await _context.SaveChangesAsync();

                try
                {
                    await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "Cancelado",
                        estadoAnterior = "Pendiente",
                        modoEntrega = pedido.ModoEntrega ?? "",
                        idRepartidor = (int?)null,
                        idCliente = pedido.IdCliente
                    });
                    await _hub.Clients.Group($"Cliente_{pedido.IdCliente}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "Cancelado",
                        modoEntrega = pedido.ModoEntrega ?? ""
                    });
                }
                catch (Exception ex) { _logger.LogWarning(ex, "SignalR error al notificar cancelación pedido {NumPedido}", pedido.NumPedido); }

                return Ok(new { message = "Pedido cancelado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar pedido {NumPedido}", request.NumPedido);
                return StatusCode(500, "Error al procesar la solicitud.");
            }
        }

        // 7. GET: api/cliente/estado-pedido/5
        [HttpGet("estado-pedido/{idUsuario}")]
        public async Task<IActionResult> EstadoPedido(int idUsuario)
        {
            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == idUsuario);
            if (cliente == null) return NotFound("Cliente no encontrado");

            var pedidos = await _context.Pedidos
                .Include(p => p.DetallePedidos)
                    .ThenInclude(d => d.IdProductoNavigation)
                .Where(p => p.IdCliente == cliente.IdCliente)
                .OrderByDescending(p => p.FechaPedido)
                .ToListAsync();

            return Ok(pedidos);
        }
    }

    // 🔧 Clases auxiliares para requests

    public class AgregarCarritoRequest
    {
        public int IdUsuario { get; set; }
        public int IdProducto { get; set; }
        public int Cantidad { get; set; }
    }

    public class EliminarCarritoRequest
    {
        public int IdUsuario { get; set; }
        public int IdProducto { get; set; }
    }

    public class ConfirmarPedidoRequest
    {
        public int IdUsuario { get; set; }
        public string Observaciones { get; set; } = string.Empty;
        public string? ModoEntrega { get; set; }
    }

    public class CancelarPedidoClienteRequest
    {
        public int NumPedido { get; set; }
        public int IdUsuario { get; set; }
    }
}
