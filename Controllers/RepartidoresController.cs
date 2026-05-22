using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RepartidoresController : ControllerBase
    {
        private readonly AppDbContext _context;
        public RepartidoresController(AppDbContext context) => _context = context;

        // GET: api/repartidores
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var lista = await _context.Repartidores
                .AsNoTracking()
                .Include(r => r.Usuario)
                .Select(r => new
                {
                    r.Id,
                    r.IdUsuario,
                    r.Nombre,
                    r.Apellido,
                    r.Activo,
                    r.FechaAlta,
                    r.Disponibilidad,
                    NombreUsuario = r.Usuario != null ? r.Usuario.NombreUsuario : null
                })
                .OrderBy(r => r.Apellido).ThenBy(r => r.Nombre)
                .ToListAsync();

            return Ok(lista);
        }

        // GET: api/repartidores/5
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var r = await _context.Repartidores
                .AsNoTracking()
                .Include(r => r.Usuario)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (r is null) return NotFound();

            return Ok(new
            {
                r.Id, r.IdUsuario, r.Nombre, r.Apellido, r.Activo, r.FechaAlta, r.Disponibilidad,
                NombreUsuario = r.Usuario?.NombreUsuario
            });
        }

        // GET: api/repartidores/disponibles
        [HttpGet("disponibles")]
        public async Task<IActionResult> GetDisponibles()
        {
            var lista = await _context.Repartidores
                .AsNoTracking()
                .Where(r => r.Activo && r.Disponibilidad == "Activo")
                .Include(r => r.Usuario)
                .Select(r => new { r.Id, r.Nombre, r.Apellido, r.Disponibilidad })
                .ToListAsync();

            return Ok(lista);
        }

        // POST: api/repartidores
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] RepartidorRequest req)
        {
            var usuario = await _context.Usuarios.FindAsync(req.IdUsuario);
            if (usuario is null) return NotFound("Usuario no encontrado.");

            if (usuario.Rol != "repartidor")
            {
                usuario.Rol = "repartidor";
                await _context.SaveChangesAsync();
            }

            var yaExiste = await _context.Repartidores.AnyAsync(r => r.IdUsuario == req.IdUsuario);
            if (yaExiste) return Conflict("Ya existe un perfil de repartidor para ese usuario.");

            var repartidor = new Repartidor
            {
                IdUsuario = req.IdUsuario,
                Nombre = req.Nombre?.Trim(),
                Apellido = req.Apellido?.Trim(),
                Activo = true,
                Disponibilidad = req.Disponibilidad ?? "Activo",
                FechaAlta = DateTime.UtcNow
            };

            _context.Repartidores.Add(repartidor);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Repartidor creado correctamente.", id = repartidor.Id });
        }

        // PUT: api/repartidores/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] RepartidorRequest req)
        {
            var repartidor = await _context.Repartidores.FindAsync(id);
            if (repartidor is null) return NotFound("Repartidor no encontrado.");

            repartidor.Nombre = req.Nombre?.Trim();
            repartidor.Apellido = req.Apellido?.Trim();
            repartidor.Activo = req.Activo;
            repartidor.Disponibilidad = req.Disponibilidad ?? repartidor.Disponibilidad;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Repartidor actualizado correctamente." });
        }

        // PUT: api/repartidores/5/disponibilidad
        [HttpPut("{id:int}/disponibilidad")]
        public async Task<IActionResult> CambiarDisponibilidad(int id, [FromBody] DisponibilidadRequest req)
        {
            var repartidor = await _context.Repartidores.FindAsync(id);
            if (repartidor is null) return NotFound("Repartidor no encontrado.");

            repartidor.Disponibilidad = req.Disponibilidad;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Disponibilidad actualizada." });
        }

        // DELETE: api/repartidores/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var repartidor = await _context.Repartidores.Include(r => r.Usuario).FirstOrDefaultAsync(r => r.Id == id);
            if (repartidor is null) return NotFound("Repartidor no encontrado.");

            if (repartidor.Usuario is not null && repartidor.Usuario.Rol == "repartidor")
            {
                repartidor.Usuario.Rol = "cliente";
            }

            _context.Repartidores.Remove(repartidor);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Repartidor eliminado correctamente." });
        }

        public class RepartidorRequest
        {
            public int IdUsuario { get; set; }
            public string? Nombre { get; set; }
            public string? Apellido { get; set; }
            public bool Activo { get; set; } = true;
            public string? Disponibilidad { get; set; }
        }

        public class DisponibilidadRequest
        {
            public string Disponibilidad { get; set; } = "Activo";
        }
    }
}
