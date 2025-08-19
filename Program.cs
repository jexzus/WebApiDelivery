using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using WebApiDelivery.Data;

var builder = WebApplication.CreateBuilder(args);

// 1) EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2) Controllers + JSON
builder.Services.AddControllers()
    .AddJsonOptions(x =>
    {
        x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // x.JsonSerializerOptions.PropertyNamingPolicy = null; // opcional
    });

// 3) Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WebApiDelivery",
        Version = "v1"
    });
});

// 4) CORS (útil cuando MAUI corre en otro host o dispositivo)
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()
));

var app = builder.Build();

// 5) Crear carpeta wwwroot/imagenes si no existe (evita 404 por carpeta faltante)
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var imagesDir = Path.Combine(webRoot, "imagenes");
if (!Directory.Exists(imagesDir))
{
    Directory.CreateDirectory(imagesDir);
}

// 6) Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ?? NECESARIO para servir /imagenes/archivo.jpg
app.UseStaticFiles();

app.UseRouting();

// ?? Permite consumir la API desde el WebView/otro host
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();
