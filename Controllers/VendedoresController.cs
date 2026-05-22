using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Models;
using webapiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VendedoresController : ControllerBase
    {
        private readonly AppDbContext _context;
        public VendedoresController(AppDbContext context) => _context = context;

        // GET: api/vendedores
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var lista = await _context.Vendedores
                .AsNoTracking()
                .Include(v => v.Usuario)
                .Select(v => new
                {
                    v.Id,
                    v.IdUsuario,
                    v.Nombre,
                    v.Apellido,
                    v.Activo,
                    v.FechaAlta,
                    NombreUsuario = v.Usuario != null ? v.Usuario.NombreUsuario : null
                })
                .OrderBy(v => v.Apellido).ThenBy(v => v.Nombre)
                .ToListAsync();

            return Ok(lista);
        }

        // GET: api/vendedores/5
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var v = await _context.Vendedores
                .AsNoTracking()
                .Include(v => v.Usuario)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (v is null) return NotFound();

            return Ok(new
            {
                v.Id, v.IdUsuario, v.Nombre, v.Apellido, v.Activo, v.FechaAlta,
                NombreUsuario = v.Usuario?.NombreUsuario
            });
        }

        // POST: api/vendedores
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] VendedorRequest req)
        {
            var usuario = await _context.Usuarios.FindAsync(req.IdUsuario);
            if (usuario is null) return NotFound("Usuario no encontrado.");

            if (usuario.Rol != "vendedor")
            {
                usuario.Rol = "vendedor";
                await _context.SaveChangesAsync();
            }

            var yaExiste = await _context.Vendedores.AnyAsync(v => v.IdUsuario == req.IdUsuario);
            if (yaExiste) return Conflict("Ya existe un perfil de vendedor para ese usuario.");

            var vendedor = new Vendedor
            {
                IdUsuario = req.IdUsuario,
                Nombre = req.Nombre?.Trim(),
                Apellido = req.Apellido?.Trim(),
                Activo = true,
                FechaAlta = DateTime.UtcNow
            };

            _context.Vendedores.Add(vendedor);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Vendedor creado correctamente.", id = vendedor.Id });
        }

        // PUT: api/vendedores/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] VendedorRequest req)
        {
            var vendedor = await _context.Vendedores.FindAsync(id);
            if (vendedor is null) return NotFound("Vendedor no encontrado.");

            vendedor.Nombre = req.Nombre?.Trim();
            vendedor.Apellido = req.Apellido?.Trim();
            vendedor.Activo = req.Activo;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Vendedor actualizado correctamente." });
        }

        // DELETE: api/vendedores/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var vendedor = await _context.Vendedores.Include(v => v.Usuario).FirstOrDefaultAsync(v => v.Id == id);
            if (vendedor is null) return NotFound("Vendedor no encontrado.");

            if (vendedor.Usuario is not null && vendedor.Usuario.Rol == "vendedor")
            {
                vendedor.Usuario.Rol = "cliente";
            }

            _context.Vendedores.Remove(vendedor);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Vendedor eliminado correctamente." });
        }

        public class VendedorRequest
        {
            public int IdUsuario { get; set; }
            public string? Nombre { get; set; }
            public string? Apellido { get; set; }
            public bool Activo { get; set; } = true;
        }
    }
}
