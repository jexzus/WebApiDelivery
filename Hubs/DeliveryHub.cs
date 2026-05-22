using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace WebApiDelivery.Hubs
{
    public class DeliveryHub : Hub
    {
        private static readonly ConcurrentDictionary<int, HashSet<string>> _pendingDeliveries = new();
        private static readonly ConcurrentDictionary<string, (int repartidorId, string disponibilidad)> _repartidorConnections = new();
        private static readonly ConcurrentDictionary<int, DateTime> _pedidosEnBusqueda = new();

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            var rol = http?.Request.Query["rol"].ToString()?.ToLowerInvariant() ?? "";
            var disponibilidad = http?.Request.Query["disponibilidad"].ToString() ?? "Inactivo";
            var repartidorIdStr = http?.Request.Query["repartidorId"].ToString() ?? "0";
            int.TryParse(repartidorIdStr, out int repartidorId);

            switch (rol)
            {
                case "admin":
                case "superadmin":
                case "vendedor":
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
                    break;

                case "repartidor":
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Repartidores");
                    if (repartidorId > 0)
                        _repartidorConnections[Context.ConnectionId] = (repartidorId, disponibilidad);
                    break;

                case "cliente":
                    var clienteIdStr = http?.Request.Query["clienteId"].ToString() ?? "0";
                    if (!string.IsNullOrEmpty(clienteIdStr))
                        await Groups.AddToGroupAsync(Context.ConnectionId, $"Cliente_{clienteIdStr}");
                    break;
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _repartidorConnections.TryRemove(Context.ConnectionId, out _);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task RechazarPedido(int numPedido)
        {
            var rechazados = _pendingDeliveries.GetOrAdd(numPedido, _ => new HashSet<string>());
            lock (rechazados) { rechazados.Add(Context.ConnectionId); }

            var totalActivos = _repartidorConnections.Count(kvp => kvp.Value.disponibilidad == "Activo");
            bool todosRechazaron;
            lock (rechazados) { todosRechazaron = totalActivos > 0 && rechazados.Count >= totalActivos; }

            if (todosRechazaron)
            {
                _pendingDeliveries.TryRemove(numPedido, out _);
                await Clients.Group("Admins").SendAsync("AlertaPedidoIgnorada", new
                {
                    numPedido,
                    mensaje = $"⚠️ Ningún repartidor aceptó el pedido #{numPedido}.",
                    tipo = "todos_rechazaron"
                });
            }
        }

        public async Task MarcarAlertaVista(int numPedido)
        {
            _pendingDeliveries.TryRemove(numPedido, out _);
            await Clients.Group("Admins").SendAsync("AlertaAdminVista", new { numPedido });
        }

        public static void ResetRechazos(int numPedido) => _pendingDeliveries.TryRemove(numPedido, out _);
        public static void ClearPendingDelivery(int numPedido) => _pendingDeliveries.TryRemove(numPedido, out _);
        public static void RegistrarPedidoEnBusqueda(int numPedido) => _pedidosEnBusqueda[numPedido] = DateTime.UtcNow;
        public static void RemoverPedidoEnBusqueda(int numPedido) => _pedidosEnBusqueda.TryRemove(numPedido, out _);
        public static HashSet<int> GetPedidosEnBusqueda() => new(_pedidosEnBusqueda.Keys);

        // Registro de cuándo se asignó cada pedido a un repartidor (para cancelación en ventana de 5 min)
        private static readonly ConcurrentDictionary<int, DateTime> _asignaciones = new();
        public static void RegistrarAsignacion(int numPedido) => _asignaciones[numPedido] = DateTime.UtcNow;
        public static void RemoverAsignacion(int numPedido) => _asignaciones.TryRemove(numPedido, out _);
        public static DateTime? GetAsignacion(int numPedido) =>
            _asignaciones.TryGetValue(numPedido, out var t) ? t : null;
    }
}
