using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using webapiDelivery.Models;
using WebApiDelivery.Data;
using WebApiDelivery.Models;
using WebApiDelivery.Services;

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // api/usuarios
    public class UsuariosController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailSender _email;
        private readonly ILogger<UsuariosController> _logger;

        public UsuariosController(AppDbContext context, IEmailSender email, ILogger<UsuariosController> logger)
        {
            _context = context;
            _email = email;
            _logger = logger;
        }

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
                .FirstOrDefaultAsync(u => u.NombreUsuario.Trim().ToLower() == nombre);

            if (usuario is null || !BCrypt.Net.BCrypt.Verify(pass, usuario.Contraseña))
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
                Contraseña = BCrypt.Net.BCrypt.HashPassword(model.Contraseña.Trim()),
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
                Email = (model.Email ?? "").Trim(),
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
            admin.Contraseña = BCrypt.Net.BCrypt.HashPassword(admin.Contraseña.Trim());

            _context.Usuarios.Add(admin);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Administrador creado correctamente." });
        }

        // ==========================
        // PUT: api/usuarios/actualizar-admin
        // ==========================
        [HttpPut("actualizar-admin")]
        public async Task<IActionResult> ActualizarAdmin([FromBody] ActualizarAdminRequest req)
        {
            var existente = await _context.Usuarios.FindAsync(req.Id);
            if (existente is null || existente.Rol != "admin")
                return NotFound("Administrador no encontrado.");

            existente.NombreUsuario = req.NombreUsuario.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { message = "Administrador actualizado correctamente." });
        }

        // ==========================
        // PUT: api/usuarios/cambiar-contrasena-por-id
        // ==========================
        [HttpPut("cambiar-contrasena-por-id")]
        public async Task<IActionResult> CambiarContrasenaPorId([FromBody] CambiarContrasenaIdRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.NuevaContrasena))
                return BadRequest("La contraseña no puede estar vacía.");

            var usuario = await _context.Usuarios.FindAsync(req.Id);
            if (usuario is null) return NotFound("Usuario no encontrado.");

            usuario.Contraseña = BCrypt.Net.BCrypt.HashPassword(req.NuevaContrasena.Trim());
            await _context.SaveChangesAsync();

            return Ok(new { message = "Contraseña actualizada correctamente." });
        }

        // ==========================
        // DELETE: api/usuarios/eliminar-admin/{id}
        // ==========================
        [HttpDelete("eliminar-admin/{id:int}")]
        public async Task<IActionResult> EliminarAdmin(int id)
        {
            var admin = await _context.Usuarios.FindAsync(id);
            if (admin is null || admin.Rol != "admin")
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
            try
            {
                var admins = await _context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.Rol == "admin" || u.Rol == "superadmin")
                    .OrderBy(u => u.NombreUsuario)
                    .Select(u => new { u.Id, u.NombreUsuario })
                    .ToListAsync();

                return Ok(admins);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar administradores");
                return StatusCode(500, "Error al procesar la solicitud.");
            }
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

        // ==========================
        // GET: api/usuarios/todos
        // ==========================
        [HttpGet("todos")]
        public async Task<IActionResult> ListarTodos()
        {
            var usuarios = await _context.Usuarios
                .AsNoTracking()
                .OrderBy(u => u.NombreUsuario)
                .Select(u => new { u.Id, u.NombreUsuario, u.Rol })
                .ToListAsync();

            return Ok(usuarios);
        }

        // ==========================
        // POST: api/usuarios/pre-registro
        // ==========================
        [HttpPost("pre-registro")]
        public async Task<IActionResult> PreRegistro([FromBody] PreRegistroRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return BadRequest("El email es obligatorio.");

            var email = req.Email.Trim().ToLower();

            var emailEnUso = await _context.Clientes.AnyAsync(c => c.Email.ToLower() == email);
            if (emailEnUso) return Conflict("El email ya está registrado.");

            // Throttle: esperar 30s entre solicitudes
            var reciente = await _context.PreRegistros
                .Where(p => p.Email.ToLower() == email && !p.Usado && p.Tipo == "registro")
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (reciente is not null && reciente.CreatedAt > DateTime.UtcNow.AddSeconds(-30))
                return BadRequest("Espera 30 segundos antes de solicitar otro código.");

            var codigo = new Random().Next(100000, 999999).ToString();
            var hash = BCrypt.Net.BCrypt.HashPassword(codigo, workFactor: 10);
            var expira = DateTime.UtcNow.AddMinutes(10);

            _context.PreRegistros.Add(new PreRegistroToken
            {
                Email = email,
                TokenHash = hash,
                ExpiresAt = expira,
                Usado = false,
                Tipo = "registro"
            });
            await _context.SaveChangesAsync();

            try
            {
                await _email.SendAsync(email,
                    "Tu código de verificación - Delivery App",
                    $"<h2>Tu código es: <strong>{codigo}</strong></h2><p>Válido por 10 minutos.</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email de pre-registro a {Email}", email);
                return StatusCode(500, "No se pudo enviar el email. Verificá tu conexión e intentá de nuevo.");
            }

            return Ok(new { message = "Código enviado al email." });
        }

        // ==========================
        // POST: api/usuarios/confirmar-pre-registro
        // ==========================
        [HttpPost("confirmar-pre-registro")]
        public async Task<IActionResult> ConfirmarPreRegistro([FromBody] ConfirmarPreRegistroRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Codigo))
                return BadRequest("Email y código son requeridos.");

            var email = req.Email.Trim().ToLower();

            var token = await _context.PreRegistros
                .Where(p => p.Email.ToLower() == email && !p.Usado && p.Tipo == "registro")
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (token is null) return BadRequest("No hay código activo para ese email.");
            if (token.ExpiresAt < DateTime.UtcNow) return BadRequest("El código expiró.");
            if (!BCrypt.Net.BCrypt.Verify(req.Codigo.Trim(), token.TokenHash))
                return BadRequest("Código incorrecto.");

            token.Usado = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Código verificado. Podés completar el registro." });
        }

        // ==========================
        // POST: api/usuarios/solicitar-recuperacion
        // ==========================
        [HttpPost("solicitar-recuperacion")]
        public async Task<IActionResult> SolicitarRecuperacion([FromBody] SolicitarRecuperacionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return BadRequest("El email es obligatorio.");

            var email = req.Email.Trim().ToLower();

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.Email.ToLower() == email);

            // Siempre 200 para no revelar si el email existe
            if (cliente is null)
                return Ok(new { message = "Si el email está registrado, recibirás un código." });

            // Throttle: 30s entre solicitudes
            var reciente = await _context.PreRegistros
                .Where(p => p.Email.ToLower() == email && !p.Usado && p.Tipo == "recuperacion")
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (reciente is not null && reciente.CreatedAt > DateTime.UtcNow.AddSeconds(-30))
                return BadRequest("Espera 30 segundos antes de solicitar otro código.");

            var codigo = new Random().Next(100000, 999999).ToString();
            var hash = BCrypt.Net.BCrypt.HashPassword(codigo, workFactor: 10);

            _context.PreRegistros.Add(new PreRegistroToken
            {
                Email = email,
                TokenHash = hash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Usado = false,
                Tipo = "recuperacion"
            });
            await _context.SaveChangesAsync();

            try
            {
                await _email.SendAsync(email,
                    "Recuperación de contraseña - Delivery App",
                    $"<h2>Tu código de recuperación es: <strong>{codigo}</strong></h2>" +
                    $"<p>Válido por 10 minutos.</p>" +
                    $"<p>Si no solicitaste esto, ignorá este mensaje.</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email de recuperación a {Email}", email);
                return StatusCode(500, "No se pudo enviar el email. Verificá tu conexión e intentá de nuevo.");
            }

            return Ok(new { message = "Si el email está registrado, recibirás un código." });
        }

        // ==========================
        // POST: api/usuarios/cambiar-contrasena
        // ==========================
        [HttpPost("cambiar-contrasena")]
        public async Task<IActionResult> CambiarContrasena([FromBody] CambiarContrasenaRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Codigo) ||
                string.IsNullOrWhiteSpace(req.NuevaContrasena))
                return BadRequest("Email, código y nueva contraseña son requeridos.");

            var email = req.Email.Trim().ToLower();

            var token = await _context.PreRegistros
                .Where(p => p.Email.ToLower() == email && !p.Usado && p.Tipo == "recuperacion")
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (token is null || token.ExpiresAt < DateTime.UtcNow ||
                !BCrypt.Net.BCrypt.Verify(req.Codigo.Trim(), token.TokenHash))
                return BadRequest("Código inválido o expirado.");

            var cliente = await _context.Clientes
                .Include(c => c.UsuarioNavigation)
                .FirstOrDefaultAsync(c => c.Email.ToLower() == email);

            if (cliente?.UsuarioNavigation is null)
                return BadRequest("Usuario no encontrado.");

            cliente.UsuarioNavigation.Contraseña = BCrypt.Net.BCrypt.HashPassword(req.NuevaContrasena.Trim());
            token.Usado = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Contraseña actualizada correctamente." });
        }

        // ===== modelos auxiliares =====
        public sealed class LoginRequest
        {
            public string NombreUsuario { get; set; } = string.Empty;
            public string Contraseña { get; set; } = string.Empty;
        }

        public sealed class PreRegistroRequest
        {
            public string Email { get; set; } = string.Empty;
        }

        public sealed class ConfirmarPreRegistroRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Codigo { get; set; } = string.Empty;
        }

        public sealed class RegistroClienteRequest
        {
            public string Nombre { get; set; } = string.Empty;
            public string Apellido { get; set; } = string.Empty;
            public string NumTelefono { get; set; } = string.Empty;
            public string Domicilio { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string NombreUsuario { get; set; } = string.Empty;
            public string Contraseña { get; set; } = string.Empty;
        }

        public sealed class SolicitarRecuperacionRequest
        {
            public string Email { get; set; } = string.Empty;
        }

        public sealed class CambiarContrasenaRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Codigo { get; set; } = string.Empty;
            public string NuevaContrasena { get; set; } = string.Empty;
        }

        public sealed class ActualizarAdminRequest
        {
            public int Id { get; set; }
            public string NombreUsuario { get; set; } = string.Empty;
        }

        public sealed class CambiarContrasenaIdRequest
        {
            public int Id { get; set; }
            public string NuevaContrasena { get; set; } = string.Empty;
        }
    }
}
