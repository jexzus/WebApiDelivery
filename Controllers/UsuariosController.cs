using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using webapiDelivery.Models;
using WebApiDelivery.Data;
using WebApiDelivery.Models;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // api/usuarios
    public class UsuariosController : ControllerBase
    {
        private readonly AppDbContext _context;
        public UsuariosController(AppDbContext context) => _context = context;

        // ==========================
        // POST: api/usuarios/login
        // ==========================
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest model)
        {
            if (string.IsNullOrWhiteSpace(model.NombreUsuario) || string.IsNullOrWhiteSpace(model.Contraseña))
                return BadRequest("Usuario y contraseña requeridos.");

            var nombre = model.NombreUsuario.Trim().ToLower();
            var pass = model.Contraseña.Trim();

            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.NombreUsuario.Trim().ToLower() == nombre &&
                    u.Contraseña.Trim() == pass);

            if (usuario is null)
                return Unauthorized("Credenciales incorrectas.");

            // 🔴 DEVOLVER idUsuario (camelCase)
            return Ok(new
            {
                idUsuario = usuario.Id,
                nombreUsuario = usuario.NombreUsuario,
                rol = (usuario.Rol ?? "").Trim().ToLower()
            });
        }

        // ==========================
        // POST: api/usuarios/registrar
        // ==========================
        [HttpPost("registrar")]
        public async Task<IActionResult> RegistrarCliente([FromBody] RegistroClienteRequest model)
        {
            if (string.IsNullOrWhiteSpace(model.NombreUsuario) || string.IsNullOrWhiteSpace(model.Contraseña))
                return BadRequest("Usuario y contraseña son obligatorios.");
            if (string.IsNullOrWhiteSpace(model.Nombre) || string.IsNullOrWhiteSpace(model.Apellido))
                return BadRequest("Nombre y Apellido son obligatorios.");

            var existe = await _context.Usuarios
                .AnyAsync(u => u.NombreUsuario.Trim().ToLower() == model.NombreUsuario.Trim().ToLower());
            if (existe) return Conflict("El nombre de usuario ya existe.");

            var usuario = new Usuario
            {
                NombreUsuario = model.NombreUsuario.Trim(),
                Contraseña = model.Contraseña.Trim(),
                Rol = "cliente"
            };
            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            var cliente = new Cliente
            {
                Nombre = model.Nombre.Trim(),
                Apellido = model.Apellido.Trim(),
                NumTelefono = (model.NumTelefono ?? "").Trim(),
                Domicilio = (model.Domicilio ?? "").Trim(),
                IdUsuario = usuario.Id
            };
            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Cliente registrado exitosamente." });
        }

        // ==========================
        // POST: api/usuarios/crear-admin
        // ==========================
        [HttpPost("crear-admin")]
        public async Task<IActionResult> CrearAdmin([FromBody] Usuario admin)
        {
            var existe = await _context.Usuarios
                .AnyAsync(u => u.NombreUsuario.Trim().ToLower() == admin.NombreUsuario.Trim().ToLower());
            if (existe) return Conflict("El nombre de usuario ya existe.");

            admin.Rol = "admin";
            admin.NombreUsuario = admin.NombreUsuario.Trim();
            admin.Contraseña = admin.Contraseña.Trim();

            _context.Usuarios.Add(admin);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Administrador creado correctamente." });
        }

        // ==========================
        // PUT: api/usuarios/actualizar-admin
        // ==========================
        [HttpPut("actualizar-admin")]
        public async Task<IActionResult> ActualizarAdmin([FromBody] Usuario admin)
        {
            var existente = await _context.Usuarios.FindAsync(admin.Id);
            if (existente is null || (existente.Rol ?? "").Trim().ToLower() != "admin")
                return NotFound("Administrador no encontrado.");

            existente.NombreUsuario = admin.NombreUsuario.Trim();
            existente.Contraseña = admin.Contraseña.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { message = "Administrador actualizado correctamente." });
        }

        // ==========================
        // DELETE: api/usuarios/eliminar-admin/{id}
        // ==========================
        [HttpDelete("eliminar-admin/{id:int}")]
        public async Task<IActionResult> EliminarAdmin(int id)
        {
            var admin = await _context.Usuarios.FindAsync(id);
            if (admin is null || (admin.Rol ?? "").Trim().ToLower() != "admin")
                return NotFound("Administrador no encontrado.");

            _context.Usuarios.Remove(admin);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Administrador eliminado correctamente." });
        }

        // ==========================
        // GET: api/usuarios/admins
        // ==========================
        [HttpGet("admins")]
        public async Task<IActionResult> ListarAdmins()
        {
            var admins = await _context.Usuarios
                .AsNoTracking()
                .Where(u => (u.Rol ?? "").Trim().ToLower() == "admin")
                .OrderBy(u => u.NombreUsuario)
                .Select(u => new { u.Id, u.NombreUsuario, Contraseña = u.Contraseña })
                .ToListAsync();

            return Ok(admins);
        }

        // ==========================
        // GET: api/usuarios/clientes   (opcional)
        // ==========================
        [HttpGet("clientes")]
        public async Task<IActionResult> ListarClientes()
        {
            var clientes = await _context.Clientes
                .AsNoTracking()
                .OrderBy(c => c.Apellido).ThenBy(c => c.Nombre)
                .Select(c => new
                {
                    c.IdCliente,
                    c.Nombre,
                    c.Apellido,
                    c.NumTelefono,
                    c.Domicilio,
                    c.IdUsuario
                })
                .ToListAsync();

            return Ok(clientes);
        }

        // ==========================
        // GET: api/usuarios/cliente-por-usuario/{idUsuario} (opcional)
        // ==========================
        [HttpGet("cliente-por-usuario/{idUsuario:int}")]
        public async Task<IActionResult> GetClientePorUsuario(int idUsuario)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdUsuario == idUsuario);

            if (cliente is null) return NotFound("Cliente no encontrado.");

            return Ok(new
            {
                cliente.IdCliente,
                cliente.Nombre,
                cliente.Apellido,
                cliente.NumTelefono,
                cliente.Domicilio,
                cliente.IdUsuario
            });
        }

        // ===== modelos auxiliares =====
        public sealed class LoginRequest
        {
            public string NombreUsuario { get; set; } = string.Empty;
            public string Contraseña { get; set; } = string.Empty;
        }

        public sealed class RegistroClienteRequest
        {
            public string Nombre { get; set; } = string.Empty;
            public string Apellido { get; set; } = string.Empty;
            public string NumTelefono { get; set; } = string.Empty;
            public string Domicilio { get; set; } = string.Empty;
            public string NombreUsuario { get; set; } = string.Empty;
            public string Contraseña { get; set; } = string.Empty;
        }
    }
}
