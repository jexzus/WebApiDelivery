using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApiDelivery.Models
{
    [Table("PreRegistros")]
    public class PreRegistroToken
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Email { get; set; } = "";

        [Required, MaxLength(200)]
        public string TokenHash { get; set; } = "";

        public DateTime ExpiresAt { get; set; }

        public bool Usado { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(20)]
        public string Tipo { get; set; } = "registro";
    }
}
