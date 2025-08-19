using System.ComponentModel.DataAnnotations;

namespace webapiDelivery.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        public string NombreUsuario { get; set; } = string.Empty;

        public string Contraseña { get; set; } = string.Empty;

        public string Rol { get; set; } = "cliente";
    }
}
