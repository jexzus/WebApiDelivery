using System.Text.Json.Serialization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using WebApiDelivery.Data;
using WebApiDelivery.Hubs;
using WebApiDelivery.Services;

var builder = WebApplication.CreateBuilder(args);

// Local: HTTPS para Windows + HTTP para Android físico
// Railway: solo HTTP en el puerto que asigna la plataforma
if (builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls("https://localhost:7189", "http://0.0.0.0:5224");
else
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// -------------------------
// Services
// -------------------------

// Enrutamiento: URLs en min�sculas
builder.Services.AddRouting(o =>
{
    o.LowercaseUrls = true;
    o.LowercaseQueryStrings = false;
});

// Email sender
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// DbContext (usa "DefaultConnection" de appsettings.json)
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

// Controllers + JSON (evitar ciclos y respetar casing por defecto)
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Si quer�s camelCase expl�cito:
        // o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// ProblemDetails para respuestas de error consistentes
builder.Services.AddProblemDetails();

// CORS amplio (�til para MAUI Blazor Hybrid y pruebas)
const string PublicCors = "Public";
builder.Services.AddCors(options =>
{
    options.AddPolicy(PublicCors, policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)   // si m�s adelante necesit�s restringir, cambi� a .WithOrigins(...)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();

// Swagger SIEMPRE (produce /swagger y /swagger/v1/swagger.json)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WebApiDelivery",
        Version = "v1"
    });
});

var app = builder.Build();

// Aplicar migraciones automáticamente al arrancar
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// -------------------------
// Middleware pipeline
// -------------------------

// Manejo global de excepciones con ProblemDetails
app.UseExceptionHandler();

// (Opcional) HSTS s�lo en prod
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Solo redirigir a HTTPS en producción (en dev el Android no puede seguir el redirect)
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// CORS
app.UseCors(PublicCors);

// Archivos est�ticos (wwwroot) + mapeo de tipos extra (.webp/.heic)
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".webp"] = "image/webp";
provider.Mappings[".heic"] = "image/heic";

// Sirve todo lo que est� en wwwroot (incluye /imagenes/*)
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// (Si agreg�s auth m�s adelante)
// app.UseAuthentication();
app.UseAuthorization();

// Swagger SIEMPRE
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebApiDelivery v1");
    c.RoutePrefix = "swagger"; // UI en /swagger
});

// Ra�z -> redirige a Swagger (evita 403/404 en "/")
app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();

// Endpoints de controllers
app.MapControllers();
app.MapHub<DeliveryHub>("/deliveryHub");

// Health-check p�blico
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .WithName("HealthCheck")
   .WithTags("System")
   .AllowAnonymous();

app.Run();
