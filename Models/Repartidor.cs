using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using webapiDelivery.Models;

namespace WebApiDelivery.Models
{
    [Table("Repartidores")]
    public class Repartidor
    {
        [Key]
        public int Id { get; set; }

        public int IdUsuario { get; set; }

        public string? Nombre { get; set; }

        public string? Apellido { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaAlta { get; set; } = DateTime.UtcNow;

        public string Disponibilidad { get; set; } = "";

        [ForeignKey("IdUsuario")]
        public Usuario? Usuario { get; set; }
    }
}
