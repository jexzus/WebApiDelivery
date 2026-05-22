using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace webapiDelivery.Models
{
    [Table("Usuario")]
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        public string NombreUsuario { get; set; } = string.Empty;

        public string Contraseña { get; set; } = string.Empty;

        public string Rol { get; set; } = "cliente";
    }
}
