using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using webapiDelivery.Models;

namespace WebApiDelivery.Models
{
    [Table("Cliente")] // 👈 IMPORTANTE
    public class Cliente
    {
        [Key]
        public int IdCliente { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string Apellido { get; set; } = string.Empty;

        public string NumTelefono { get; set; } = string.Empty;

        public string Domicilio { get; set; } = string.Empty;

        public int IdUsuario { get; set; }

        [ForeignKey("IdUsuario")]
        public Usuario? UsuarioNavigation { get; set; }
    }
}
