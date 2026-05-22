using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApiDelivery.Data;   

namespace WebApiDelivery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _db;   

        public HealthController(AppDbContext db)   
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync("SELECT 1");
                return Ok(new { ok = true, db = "up" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, db = "down", error = ex.Message });
            }
        }
    }
}
