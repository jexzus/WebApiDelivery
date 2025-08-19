using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApiDelivery.Models
{
    [Table("Producto")]  // 👈 Esto es lo que faltaba
    public class Producto
    {
        [Key]
        public int IdProducto { get; set; }

        public string NombreProducto { get; set; }

        public string Descripcion { get; set; }

        public decimal Precio { get; set; }

        public string? Imagen { get; set; }
    }
}
