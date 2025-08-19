using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClienteController : ControllerBase
    {
        private readonly AppDbContext _context;
        private static Dictionary<int, List<DetallePedido>> _carritos = new(); // clave: idUsuario

        public ClienteController(AppDbContext context)
        {
            _context = context;
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

            if (!_carritos.ContainsKey(request.IdUsuario))
                _carritos[request.IdUsuario] = new List<DetallePedido>();

            var carrito = _carritos[request.IdUsuario];
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

            return Ok(carrito);
        }

        // 3. POST: api/cliente/carrito/eliminar
        [HttpPost("carrito/eliminar")]
        public IActionResult EliminarDelCarrito([FromBody] EliminarCarritoRequest request)
        {
            if (!_carritos.ContainsKey(request.IdUsuario)) return BadRequest("Carrito vacío");

            var carrito = _carritos[request.IdUsuario];
            var index = carrito.FindIndex(p => p.IdProducto == request.IdProducto);

            if (index >= 0)
                carrito.RemoveAt(index);

            return Ok(carrito);
        }

        // 4. GET: api/cliente/carrito?idUsuario=5
        [HttpGet("carrito")]
        public IActionResult VerCarrito([FromQuery] int idUsuario)
        {
            if (!_carritos.ContainsKey(idUsuario))
                return Ok(new List<DetallePedido>());

            return Ok(_carritos[idUsuario]);
        }

        // 5. POST: api/cliente/confirmar-pedido
        [HttpPost("confirmar-pedido")]
        public async Task<IActionResult> ConfirmarPedido([FromBody] ConfirmarPedidoRequest request)
        {
            if (!_carritos.ContainsKey(request.IdUsuario))
                return BadRequest("Carrito vacío");

            var usuario = await _context.Usuarios.FindAsync(request.IdUsuario);
            if (usuario == null) return NotFound("Usuario no encontrado");

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.IdUsuario == request.IdUsuario);
            if (cliente == null) return NotFound("Cliente no encontrado");

            var carrito = _carritos[request.IdUsuario];
            var total = carrito.Sum(c => c.Cantidad * c.PrecioUnitario);

            var pedido = new Pedido
            {
                IdCliente = cliente.IdCliente,
                FechaPedido = DateTime.Now,
                EstadoPedido = "Pendiente",
                Observaciones = request.Observaciones,
                MontoTotal = total
            };

            _context.Pedidos.Add(pedido);
            await _context.SaveChangesAsync();

            foreach (var item in carrito)
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
            _carritos.Remove(request.IdUsuario);

            return Ok(new { message = "Pedido confirmado exitosamente." });
        }

        // 6. GET: api/cliente/estado-pedido/5
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
    }
}
