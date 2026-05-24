using MercadoPago.Client.Preference;
using MercadoPago.Config;
using MercadoPago.Resource.Preference;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Hubs;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PagosController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<PagosController> _logger;
        private readonly IHubContext<DeliveryHub> _hub;

        public PagosController(AppDbContext context, IConfiguration config, ILogger<PagosController> logger, IHubContext<DeliveryHub> hub)
        {
            _context = context;
            _config = config;
            _logger = logger;
            _hub = hub;
        }

        // POST api/pagos/crear-preferencia
        [HttpPost("crear-preferencia")]
        public async Task<IActionResult> CrearPreferencia([FromBody] CrearPreferenciaRequest req)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.DetallePedidos)
                    .ThenInclude(d => d.IdProductoNavigation)
                .Include(p => p.IdClienteNavigation)
                .FirstOrDefaultAsync(p => p.NumPedido == req.NumPedido);

            if (pedido is null) return NotFound("Pedido no encontrado.");

            if (pedido.EstadoPago == "aprobado")
                return BadRequest("Este pedido ya fue pagado.");

            MercadoPagoConfig.AccessToken = _config["MercadoPago:AccessToken"];
            var baseUrl = (_config["MercadoPago:BaseUrl"] ?? "http://localhost:5224").TrimEnd('/');

            var items = pedido.DetallePedidos.Select(d => new PreferenceItemRequest
            {
                Title = d.IdProductoNavigation?.NombreProducto ?? "Producto",
                Quantity = d.Cantidad,
                UnitPrice = d.PrecioUnitario,
                CurrencyId = "ARS"
            }).ToList();

            var pref = new PreferenceRequest
            {
                Items = items,
                Payer = new PreferencePayerRequest
                {
                    Email = pedido.IdClienteNavigation?.Email ?? "comprador@test.com"
                },
                ExternalReference = pedido.NumPedido.ToString()
            };

            try
            {
                var client = new PreferenceClient();
                var preference = await client.CreateAsync(pref);

                pedido.MpPreferenceId = preference.Id;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Preferencia MP creada: {Id} | SandboxInitPoint: {Url}", preference.Id, preference.SandboxInitPoint);

                return Ok(new { sandboxInitPoint = preference.SandboxInitPoint });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creando preferencia MP para pedido {NumPedido}", req.NumPedido);
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
            }
        }

        // GET api/pagos/retorno   ← MP redirige el navegador aquí después del pago
        [HttpGet("retorno")]
        public IActionResult Retorno([FromQuery] string status, [FromQuery] int np)
        {
            var (titulo, mensaje, color) = status switch
            {
                "aprobado" => ("¡Pago aprobado!", "Tu pedido fue confirmado. Volvé a la app JamBurgers.", "#22c55e"),
                "rechazado" => ("Pago rechazado", "No se pudo procesar el pago. Volvé a la app e intentá de nuevo.", "#ef4444"),
                _ => ("Pago pendiente", "Tu pago está siendo procesado. Volvé a la app JamBurgers.", "#f59e0b")
            };

            var html = $$"""
                <!DOCTYPE html>
                <html lang="es">
                <head>
                  <meta charset="utf-8"/>
                  <meta name="viewport" content="width=device-width, initial-scale=1"/>
                  <title>JamBurgers - Resultado del pago</title>
                  <style>
                    body { font-family: sans-serif; display: flex; align-items: center;
                           justify-content: center; min-height: 100vh; margin: 0;
                           background: #f3f4f6; }
                    .card { background: white; border-radius: 16px; padding: 2rem;
                            text-align: center; max-width: 360px; box-shadow: 0 4px 20px rgba(0,0,0,.1); }
                    h1 { color: {{color}}; }
                    p { color: #374151; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <h1>{{titulo}}</h1>
                    <p>{{mensaje}}</p>
                    <p><small>Pedido N° {{np}}</small></p>
                  </div>
                </body>
                </html>
                """;

            return Content(html, "text/html");
        }

        // POST api/pagos/webhook   ← MP notifica el resultado del pago
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] MpWebhookBody? body)
        {
            try
            {
                var type = body?.Type ?? Request.Query["type"].ToString();
                var idStr = body?.Data?.Id ?? Request.Query["id"].ToString();

                if (type != "payment" || string.IsNullOrEmpty(idStr)) return Ok();

                MercadoPagoConfig.AccessToken = _config["MercadoPago:AccessToken"];
                var payment = await new MercadoPago.Client.Payment.PaymentClient()
                    .GetAsync(long.Parse(idStr));

                if (!int.TryParse(payment.ExternalReference, out var numPedido)) return Ok();

                var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.NumPedido == numPedido);
                if (pedido is null) return Ok();

                pedido.MpPaymentId = payment.Id.ToString();
                pedido.MpStatusDetail = payment.StatusDetail;
                pedido.EstadoPago = payment.Status switch
                {
                    "approved" => "aprobado",
                    "rejected" => "rechazado",
                    _ => "pendiente"
                };

                await _context.SaveChangesAsync();
                _logger.LogInformation("Pedido {NumPedido} → EstadoPago: {Estado}", numPedido, pedido.EstadoPago);

                await _hub.Clients.Group("Admins").SendAsync("PagoActualizado", new
                {
                    numPedido,
                    estadoPago = pedido.EstadoPago
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando webhook MP.");
            }

            return Ok();
        }

        public class CrearPreferenciaRequest
        {
            public int NumPedido { get; set; }
        }

        public class MpWebhookBody
        {
            public string? Type { get; set; }
            public MpWebhookData? Data { get; set; }
        }

        public class MpWebhookData
        {
            public string? Id { get; set; }
        }
    }
}
