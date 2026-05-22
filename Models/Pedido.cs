using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApiDelivery.Models
{
    [Table("Pedido")]
    public class Pedido
    {
        [Key]
        public int NumPedido { get; set; }

        [Required]
        public int IdCliente { get; set; }

        [Required]
        public DateTime FechaPedido { get; set; } = DateTime.Now;

        [Required]
        public string EstadoPedido { get; set; } = "Pendiente";

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoTotal { get; set; } = 0;

        public string? Observaciones { get; set; }

        public string EstadoPago { get; set; } = "pendiente";

        public string? MpPaymentId { get; set; }

        public string? MpPreferenceId { get; set; }

        public string? MpStatusDetail { get; set; }

        public int? IdRepartidor { get; set; }

        public string? ModoEntrega { get; set; }

        [ForeignKey("IdCliente")]
        public Cliente? IdClienteNavigation { get; set; }

        [ForeignKey("IdRepartidor")]
        public Repartidor? RepartidorNavigation { get; set; }

        public List<DetallePedido> DetallePedidos { get; set; } = new();
    }
}
