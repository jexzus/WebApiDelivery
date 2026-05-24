using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Hubs;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AdminController> _logger;
        private readonly IHubContext<DeliveryHub> _hub;

        public AdminController(AppDbContext context, ILogger<AdminController> logger, IHubContext<DeliveryHub> hub)
        {
            _context = context;
            _logger = logger;
            _hub = hub;
        }

        // ── GET api/admin/pedidos ─────────────────────────────────────────
        [HttpGet("pedidos")]
        public async Task<IActionResult> ObtenerPedidos()
        {
            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.RepartidorNavigation)
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

        // ── PUT api/admin/cambiar-estado ──────────────────────────────────
        [HttpPut("cambiar-estado")]
        public async Task<IActionResult> CambiarEstadoPedido([FromBody] CambiarEstadoRequest request)
        {
            try
            {
                if (request == null || request.NumPedido <= 0 || string.IsNullOrWhiteSpace(request.NuevoEstado))
                    return BadRequest("Datos inválidos para actualizar el estado.");

                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == request.NumPedido);
                if (pedido == null) return NotFound("Pedido no encontrado.");

                var estadosValidos = new[] { "Pendiente", "En preparación", "EsperandoRepartidor", "Para retirar", "En reparto", "Entregado", "Cancelado" };
                if (!estadosValidos.Contains(request.NuevoEstado))
                    return BadRequest("Estado no válido.");

                bool esDomicilio = string.Equals(pedido.ModoEntrega, "Domicilio", StringComparison.OrdinalIgnoreCase);
                if (esDomicilio && request.NuevoEstado == "Para retirar")
                    return BadRequest("Un pedido a domicilio no puede pasar a 'Para retirar'.");
                if (!esDomicilio && pedido.ModoEntrega is not null && request.NuevoEstado == "En reparto")
                    return BadRequest("Un pedido de retiro en tienda no puede pasar a 'En reparto'.");

                if (string.Equals(pedido.FormaPago, "mercadopago", StringComparison.OrdinalIgnoreCase)
                    && pedido.EstadoPago != "aprobado"
                    && request.NuevoEstado != "Cancelado")
                    return BadRequest("El pago por MercadoPago aún no fue confirmado. No se puede avanzar el pedido.");

                bool cancelarBusqueda = pedido.EstadoPedido == "EsperandoRepartidor"
                                     && request.NuevoEstado != "EsperandoRepartidor";

                string estadoAnterior = pedido.EstadoPedido;
                int idCliente = pedido.IdCliente;
                int? idRepartidor = pedido.IdRepartidor;

                pedido.EstadoPedido = request.NuevoEstado;

                if (request.NuevoEstado == "Entregado" && idRepartidor != null)
                {
                    var rep = await _context.Repartidores.FirstOrDefaultAsync(r => r.Id == idRepartidor.Value);
                    if (rep != null && rep.Disponibilidad == "Ocupado") rep.Disponibilidad = "Activo";
                }

                await _context.SaveChangesAsync();

                if (cancelarBusqueda)
                {
                    DeliveryHub.RemoverPedidoEnBusqueda(pedido.NumPedido);
                    await _hub.Clients.Group("Repartidores").SendAsync("CerrarAlerta", pedido.NumPedido);
                }

                await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = request.NuevoEstado,
                    estadoAnterior,
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor,
                    idCliente
                });

                if (idCliente > 0)
                {
                    await _hub.Clients.Group($"Cliente_{idCliente}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = request.NuevoEstado,
                        modoEntrega = pedido.ModoEntrega ?? ""
                    });
                }

                return Ok(new { message = "Estado actualizado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del pedido.");
                return StatusCode(500, "Error interno al actualizar el estado.");
            }
        }

        // ── POST api/admin/solicitar-repartidor ───────────────────────────
        [HttpPost("solicitar-repartidor")]
        public async Task<IActionResult> SolicitarRepartidor([FromBody] NumPedidoRequest req)
        {
            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null) return NotFound("Pedido no encontrado.");

                if (pedido.EstadoPedido != "En preparación" && pedido.EstadoPedido != "EsperandoRepartidor")
                    return BadRequest($"El pedido está en '{pedido.EstadoPedido}'. Solo se puede solicitar repartidor desde 'En preparación'.");

                if (!string.Equals(pedido.ModoEntrega, "Domicilio", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Este pedido es de retiro en local. No necesita repartidor.");

                if (pedido.EstadoPedido != "EsperandoRepartidor")
                {
                    pedido.EstadoPedido = "EsperandoRepartidor";
                    await _context.SaveChangesAsync();
                }

                var cliente = pedido.IdClienteNavigation;
                var productos = pedido.DetallePedidos
                    .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                    .ToList();

                var payload = new
                {
                    numPedido = pedido.NumPedido,
                    cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                    domicilio = cliente?.Domicilio ?? "Sin domicilio",
                    telefono = cliente?.NumTelefono ?? "-",
                    montoTotal = (pedido.MontoTotal).ToString("N2"),
                    productos = string.Join(", ", productos),
                    observaciones = pedido.Observaciones ?? ""
                };

                await _hub.Clients.Group("Repartidores").SendAsync("NuevoPedidoParaReparto", payload);

                // Notificar cambio de estado a todos
                await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "EsperandoRepartidor",
                    estadoAnterior = "En preparación",
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor = (int?)null,
                    idCliente = pedido.IdCliente
                });

                DeliveryHub.RegistrarPedidoEnBusqueda(pedido.NumPedido);

                return Ok(new { message = "Notificación enviada a los repartidores." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al solicitar repartidor.");
                return StatusCode(500, "Error interno.");
            }
        }

        // ── POST api/admin/aceptar-pedido ─────────────────────────────────
        [HttpPost("aceptar-pedido")]
        public async Task<IActionResult> AceptarPedido([FromBody] AceptarPedidoRequest req)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null) return NotFound("Pedido no encontrado.");

                if (pedido.IdRepartidor != null)
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { message = "Este pedido ya fue tomado por otro repartidor.", yaFueTomado = true });
                }

                if (pedido.EstadoPedido != "En preparación" && pedido.EstadoPedido != "EsperandoRepartidor")
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = $"El pedido está en '{pedido.EstadoPedido}' y no puede ser aceptado.", yaFueTomado = false });
                }

                var repartidor = await _context.Repartidores.FirstOrDefaultAsync(r => r.Id == req.IdRepartidor);
                if (repartidor == null) return NotFound("Repartidor no encontrado.");

                if (repartidor.Disponibilidad != "Activo")
                    return BadRequest("Solo podés aceptar pedidos si tu disponibilidad es 'Activo'.");

                pedido.IdRepartidor = repartidor.Id;
                pedido.EstadoPedido = "En reparto";
                repartidor.Disponibilidad = "Ocupado";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var nombreRepartidor = $"{repartidor.Apellido} {repartidor.Nombre}".Trim();

                await _hub.Clients.Group("Admins").SendAsync("PedidoAceptado", new
                {
                    numPedido = req.NumPedido,
                    nombreRepartidor,
                    repartidorId = repartidor.Id
                });

                await _hub.Clients.Group("Repartidores").SendAsync("PedidoAsignado", new
                {
                    numPedido = req.NumPedido,
                    repartidorId = repartidor.Id
                });

                await _hub.Clients.Group("Repartidores").SendAsync("CerrarAlerta", req.NumPedido);

                await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "En reparto",
                    estadoAnterior = "EsperandoRepartidor",
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor = repartidor.Id,
                    idCliente = pedido.IdCliente
                });

                if (pedido.IdCliente > 0)
                {
                    await _hub.Clients.Group($"Cliente_{pedido.IdCliente}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "En reparto",
                        modoEntrega = pedido.ModoEntrega ?? ""
                    });
                }

                DeliveryHub.RemoverPedidoEnBusqueda(req.NumPedido);
                DeliveryHub.ClearPendingDelivery(req.NumPedido);
                DeliveryHub.RegistrarAsignacion(req.NumPedido);

                return Ok(new
                {
                    message = $"Pedido #{req.NumPedido} aceptado.",
                    numPedido = req.NumPedido,
                    cliente = $"{pedido.IdClienteNavigation?.Nombre} {pedido.IdClienteNavigation?.Apellido}".Trim(),
                    domicilio = pedido.IdClienteNavigation?.Domicilio ?? "",
                    telefono = pedido.IdClienteNavigation?.NumTelefono ?? ""
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return Conflict(new { message = "Conflicto de concurrencia. Otro repartidor tomó el pedido.", yaFueTomado = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al aceptar pedido.");
                return StatusCode(500, "Error interno.");
            }
        }

        // ── GET api/admin/pedidos-disponibles ─────────────────────────────
        [HttpGet("pedidos-disponibles")]
        public async Task<IActionResult> ObtenerPedidosDisponibles()
        {
            try
            {
                var pedidos = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .Where(p =>
                        p.EstadoPedido == "EsperandoRepartidor" &&
                        p.IdRepartidor == null &&
                        p.ModoEntrega == "Domicilio")
                    .AsNoTracking()
                    .ToListAsync();

                var resultado = pedidos.Select(p => new
                {
                    numPedido = p.NumPedido,
                    cliente = $"{p.IdClienteNavigation?.Nombre} {p.IdClienteNavigation?.Apellido}".Trim(),
                    domicilio = p.IdClienteNavigation?.Domicilio ?? "Sin domicilio",
                    telefono = p.IdClienteNavigation?.NumTelefono ?? "-",
                    montoTotal = p.MontoTotal.ToString("N2"),
                    productos = string.Join(", ", p.DetallePedidos.Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")),
                    observaciones = p.Observaciones ?? ""
                });

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener pedidos disponibles.");
                return Ok(Array.Empty<object>());
            }
        }

        // ── POST api/admin/revertir-a-preparacion ─────────────────────────
        [HttpPost("revertir-a-preparacion")]
        public async Task<IActionResult> RevertirAPreparacion([FromBody] RevertirRequest req)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null) return NotFound("Pedido no encontrado.");

                if (pedido.EstadoPedido != "En reparto" && pedido.EstadoPedido != "EsperandoRepartidor")
                    return BadRequest($"Solo se puede revertir desde 'En reparto' o 'EsperandoRepartidor'. Estado actual: '{pedido.EstadoPedido}'.");

                int? idRepartidorAnterior = pedido.IdRepartidor;
                if (idRepartidorAnterior != null)
                {
                    var rep = await _context.Repartidores.FirstOrDefaultAsync(r => r.Id == idRepartidorAnterior.Value);
                    if (rep != null && rep.Disponibilidad == "Ocupado") rep.Disponibilidad = "Activo";
                }

                pedido.IdRepartidor = null;
                pedido.EstadoPedido = "En preparación";
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "En preparación",
                    estadoAnterior = "En reparto",
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor = (int?)null,
                    idCliente = pedido.IdCliente
                });

                await _hub.Clients.All.SendAsync("PedidoRevertido", new
                {
                    numPedido = pedido.NumPedido,
                    idRepartidorAnterior
                });

                if (pedido.IdCliente > 0)
                {
                    await _hub.Clients.Group($"Cliente_{pedido.IdCliente}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "En preparación",
                        modoEntrega = pedido.ModoEntrega ?? ""
                    });
                }

                if (req.ReemitirAlerta)
                {
                    var cliente = pedido.IdClienteNavigation;
                    var productos = pedido.DetallePedidos
                        .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                        .ToList();

                    await _hub.Clients.Group("Repartidores").SendAsync("NuevoPedidoParaReparto", new
                    {
                        numPedido = pedido.NumPedido,
                        cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                        domicilio = cliente?.Domicilio ?? "Sin domicilio",
                        telefono = cliente?.NumTelefono ?? "-",
                        montoTotal = pedido.MontoTotal.ToString("N2"),
                        productos = string.Join(", ", productos),
                        observaciones = pedido.Observaciones ?? ""
                    });
                }

                DeliveryHub.RemoverPedidoEnBusqueda(req.NumPedido);

                return Ok(new { message = $"Pedido #{req.NumPedido} revertido a 'En preparación'." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al revertir pedido.");
                return StatusCode(500, "Error interno.");
            }
        }

        // ── POST api/admin/cancelar-reparto ───────────────────────────────
        [HttpPost("cancelar-reparto")]
        public async Task<IActionResult> CancelarReparto([FromBody] CancelarRepartoRequest req)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var pedido = await _context.Pedidos
                    .Include(p => p.IdClienteNavigation)
                    .Include(p => p.DetallePedidos).ThenInclude(d => d.IdProductoNavigation)
                    .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

                if (pedido == null) return NotFound("Pedido no encontrado.");
                if (pedido.EstadoPedido != "En reparto") return BadRequest("El pedido no está en reparto.");
                if (pedido.IdRepartidor != req.IdRepartidor) return StatusCode(403, "No sos el repartidor asignado a este pedido.");

                var asignacion = DeliveryHub.GetAsignacion(req.NumPedido);
                if (asignacion.HasValue && (DateTime.UtcNow - asignacion.Value).TotalMinutes >= 5)
                    return StatusCode(409, "Han pasado más de 5 minutos desde que aceptaste el pedido. Ya no podés cancelarlo.");

                var repartidor = await _context.Repartidores.FirstOrDefaultAsync(r => r.Id == req.IdRepartidor);
                if (repartidor != null && repartidor.Disponibilidad == "Ocupado")
                    repartidor.Disponibilidad = "Activo";

                pedido.IdRepartidor = null;
                pedido.EstadoPedido = "EsperandoRepartidor";
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                DeliveryHub.RemoverAsignacion(req.NumPedido);
                DeliveryHub.ResetRechazos(req.NumPedido);
                DeliveryHub.RegistrarPedidoEnBusqueda(req.NumPedido);

                var cliente = pedido.IdClienteNavigation;
                var productos = pedido.DetallePedidos
                    .Select(d => $"{d.Cantidad}x {d.IdProductoNavigation?.NombreProducto ?? "Producto"}")
                    .ToList();

                await _hub.Clients.Group("Repartidores").SendAsync("NuevoPedidoParaReparto", new
                {
                    numPedido = pedido.NumPedido,
                    cliente = $"{cliente?.Nombre} {cliente?.Apellido}".Trim(),
                    domicilio = cliente?.Domicilio ?? "Sin domicilio",
                    telefono = cliente?.NumTelefono ?? "-",
                    montoTotal = pedido.MontoTotal.ToString("N2"),
                    productos = string.Join(", ", productos),
                    observaciones = pedido.Observaciones ?? ""
                });

                await _hub.Clients.All.SendAsync("EstadoCambiadoGlobal", new
                {
                    numPedido = pedido.NumPedido,
                    nuevoEstado = "EsperandoRepartidor",
                    estadoAnterior = "En reparto",
                    modoEntrega = pedido.ModoEntrega ?? "",
                    idRepartidor = (int?)null,
                    idCliente = pedido.IdCliente
                });

                if (pedido.IdCliente > 0)
                    await _hub.Clients.Group($"Cliente_{pedido.IdCliente}").SendAsync("MiPedidoActualizado", new
                    {
                        numPedido = pedido.NumPedido,
                        nuevoEstado = "EsperandoRepartidor",
                        modoEntrega = pedido.ModoEntrega ?? ""
                    });

                return Ok(new { message = $"Pedido #{req.NumPedido} devuelto a búsqueda de repartidor." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al cancelar reparto.");
                return StatusCode(500, "Error interno.");
            }
        }

        // ── DTOs ──────────────────────────────────────────────────────────
        public class CambiarEstadoRequest
        {
            public int NumPedido { get; set; }
            public string? NuevoEstado { get; set; }
        }

        public class NumPedidoRequest
        {
            public int NumPedido { get; set; }
        }

        public class AceptarPedidoRequest
        {
            public int NumPedido { get; set; }
            public int IdRepartidor { get; set; }
        }

        public class RevertirRequest
        {
            public int NumPedido { get; set; }
            public bool ReemitirAlerta { get; set; } = true;
        }

        public class CancelarRepartoRequest
        {
            public int NumPedido { get; set; }
            public int IdRepartidor { get; set; }
        }
    }
}
