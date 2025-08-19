using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using WebApiDelivery.Models;

namespace WebApiDelivery.Models
{
    [Table("DetallePedido")] // ✅ Nombre real de la tabla
    public class DetallePedido
    {
        [Key]
        public int IdDetalle { get; set; }

        public int NumPedido { get; set; }

        public int IdProducto { get; set; }

        public int Cantidad { get; set; }

        public decimal PrecioUnitario { get; set; }

        public decimal Subtotal => Cantidad * PrecioUnitario;

        [JsonIgnore] // IMPORTANTE

        [ForeignKey("NumPedido")]
        public Pedido? PedidoNavigation { get; set; }

        [ForeignKey("IdProducto")]
        public Producto? IdProductoNavigation { get; set; }
    }
}
