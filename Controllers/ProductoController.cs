using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;          // AppDbContext
using WebApiDelivery.Models;        // Entidad Producto

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductoController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public ProductoController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // ====== DTOs / VMs ======
        public sealed class ProductoVm
        {
            public int IdProducto { get; set; }
            public string NombreProducto { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public decimal Precio { get; set; }
            public string? Imagen { get; set; }
        }

        // POST multipart (crear)
        public sealed class ProductoCreateWithImageDto
        {
            public string NombreProducto { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public string Precio { get; set; } = "";   // "3100,00" o "3100.00"
        }

        // PUT multipart (editar)
        public sealed class ProductoUpdateWithImageDto
        {
            public int IdProducto { get; set; }
            public string NombreProducto { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public string Precio { get; set; } = "";   // "3100,00" o "3100.00"
        }

        // ====== GET api/Producto ======
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductoVm>>> GetAll()
        {
            var items = await _db.Productos
                .AsNoTracking()
                .Select(p => new ProductoVm
                {
                    IdProducto = p.IdProducto,
                    NombreProducto = p.NombreProducto,
                    Descripcion = p.Descripcion,
                    Precio = p.Precio,
                    Imagen = p.Imagen
                })
                .ToListAsync();

            return Ok(items);
        }

        // ====== GET api/Producto/{id} ======
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductoVm>> GetById(int id)
        {
            var p = await _db.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.IdProducto == id);
            if (p is null) return NotFound();

            return new ProductoVm
            {
                IdProducto = p.IdProducto,
                NombreProducto = p.NombreProducto,
                Descripcion = p.Descripcion,
                Precio = p.Precio,
                Imagen = p.Imagen
            };
        }

        // ====== POST api/Producto (JSON) ======
        [HttpPost]
        [Consumes("application/json")]
        public async Task<ActionResult<ProductoVm>> PostJson([FromBody] ProductoVm dto)
        {
            var nombre = (dto.NombreProducto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre es obligatorio.");
            if (dto.Precio <= 0) return BadRequest("El precio debe ser mayor a 0.");

            var p = new Producto
            {
                NombreProducto = nombre,
                Descripcion = dto.Descripcion?.Trim() ?? "",
                Precio = dto.Precio
            };

            _db.Productos.Add(p);
            await _db.SaveChangesAsync();

            var vm = new ProductoVm
            {
                IdProducto = p.IdProducto,
                NombreProducto = p.NombreProducto,
                Descripcion = p.Descripcion,
                Precio = p.Precio,
                Imagen = p.Imagen
            };

            return CreatedAtAction(nameof(GetById), new { id = p.IdProducto }, vm);
        }

        // ====== POST api/Producto/con-imagen (multipart) ======
        [HttpPost("con-imagen")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<ActionResult<ProductoVm>> PostMultipart(
            [FromForm] ProductoCreateWithImageDto dto,
            IFormFile? ImagenArchivo)
        {
            if (!TryParseDecimalFlexible(dto.Precio, out var precio))
                return BadRequest("Precio inválido.");

            var nombre = (dto.NombreProducto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre es obligatorio.");
            if (precio <= 0) return BadRequest("El precio debe ser mayor a 0.");

            var p = new Producto
            {
                NombreProducto = nombre,
                Descripcion = dto.Descripcion?.Trim() ?? "",
                Precio = precio
            };

            _db.Productos.Add(p);
            await _db.SaveChangesAsync(); // genera Id

            if (ImagenArchivo is not null && ImagenArchivo.Length > 0)
            {
                var savedName = await GuardarImagenAsync(p.IdProducto, ImagenArchivo);
                if (savedName is null) return StatusCode(500, "Error al guardar la imagen.");
                p.Imagen = savedName;
                await _db.SaveChangesAsync();
            }

            var vm = new ProductoVm
            {
                IdProducto = p.IdProducto,
                NombreProducto = p.NombreProducto,
                Descripcion = p.Descripcion,
                Precio = p.Precio,
                Imagen = p.Imagen
            };

            return CreatedAtAction(nameof(GetById), new { id = p.IdProducto }, vm);
        }

        // ====== PUT api/Producto/{id} (JSON) ======
        [HttpPut("{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult<ProductoVm>> PutJson(int id, [FromBody] ProductoVm dto)
        {
            if (id != dto.IdProducto) return BadRequest("Id inconsistente.");

            var p = await _db.Productos.FirstOrDefaultAsync(x => x.IdProducto == id);
            if (p is null) return NotFound();

            var nombre = (dto.NombreProducto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre es obligatorio.");
            if (dto.Precio <= 0) return BadRequest("El precio debe ser mayor a 0.");

            p.NombreProducto = nombre;
            p.Descripcion = dto.Descripcion?.Trim() ?? "";
            p.Precio = dto.Precio;

            await _db.SaveChangesAsync();

            return Ok(new ProductoVm
            {
                IdProducto = p.IdProducto,
                NombreProducto = p.NombreProducto,
                Descripcion = p.Descripcion,
                Precio = p.Precio,
                Imagen = p.Imagen
            });
        }

        // ====== PUT api/Producto/{id}/con-imagen (multipart) ======
        [HttpPut("{id:int}/con-imagen")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<ActionResult<ProductoVm>> PutMultipart(
            int id,
            [FromForm] ProductoUpdateWithImageDto dto,
            IFormFile? ImagenArchivo)
        {
            if (id != dto.IdProducto) return BadRequest("Id inconsistente.");
            if (!TryParseDecimalFlexible(dto.Precio, out var precio))
                return BadRequest("Precio inválido.");

            var p = await _db.Productos.FirstOrDefaultAsync(x => x.IdProducto == id);
            if (p is null) return NotFound();

            var nombre = (dto.NombreProducto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre es obligatorio.");
            if (precio <= 0) return BadRequest("El precio debe ser mayor a 0.");

            p.NombreProducto = nombre;
            p.Descripcion = dto.Descripcion?.Trim() ?? "";
            p.Precio = precio;

            if (ImagenArchivo is not null && ImagenArchivo.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(p.Imagen))
                    BorrarImagenSiExiste(p.Imagen);

                var savedName = await GuardarImagenAsync(id, ImagenArchivo);
                if (savedName is null) return StatusCode(500, "Error al guardar la imagen.");
                p.Imagen = savedName;
            }

            await _db.SaveChangesAsync();

            return Ok(new ProductoVm
            {
                IdProducto = p.IdProducto,
                NombreProducto = p.NombreProducto,
                Descripcion = p.Descripcion,
                Precio = p.Precio,
                Imagen = p.Imagen
            });
        }

        // ====== DELETE api/Producto/{id} ======
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await _db.Productos.FirstOrDefaultAsync(x => x.IdProducto == id);
            if (p is null) return NotFound();

            // borrar imagen si existe
            if (!string.IsNullOrWhiteSpace(p.Imagen))
                BorrarImagenSiExiste(p.Imagen);

            _db.Productos.Remove(p);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Producto eliminado correctamente" });
        }

        // ====== Helpers imagen / decimal ======
        private async Task<string?> GuardarImagenAsync(int idProducto, IFormFile archivo)
        {
            try
            {
                var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
                var permitidas = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (!permitidas.Contains(ext)) return null;

                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var folder = Path.Combine(webRoot, "imagenes");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var safeName = $"prod_{idProducto}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                var path = Path.Combine(folder, safeName);

                await using var fs = System.IO.File.Create(path);
                await archivo.CopyToAsync(fs);

                return safeName;
            }
            catch
            {
                return null;
            }
        }

        private void BorrarImagenSiExiste(string nombreArchivo)
        {
            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var path = Path.Combine(webRoot, "imagenes", nombreArchivo);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
                // no interrumpir si falla el borrado
            }
        }

        /// Acepta "3100,00" (es-AR) o "3100.00" (en-US) e intenta reemplazo manual.
        private static bool TryParseDecimalFlexible(string input, out decimal result)
        {
            result = 0m;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim();
            var culturaComa = new CultureInfo("es-AR");
            var culturaPunto = new CultureInfo("en-US");

            if (decimal.TryParse(input, NumberStyles.Number, culturaComa, out result)) return true;
            if (decimal.TryParse(input, NumberStyles.Number, culturaPunto, out result)) return true;

            var conPunto = input.Replace(',', '.');
            return decimal.TryParse(conPunto, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }
    }
}
