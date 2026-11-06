// =====================================================================================
// Punto de entrada de la API. Acá se configuran los servicios (inyección de dependencias)
// y el "pipeline" HTTP (qué pasa con cada request, en orden).
// =====================================================================================
using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---- Puerto ----
// Render (y otros hostings) indican el puerto por la variable de entorno PORT.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ---- Base de datos ----
// Con cadena de conexión → PostgreSQL (Neon). Sin cadena → base en memoria con datos demo,
// útil para probar localmente sin instalar nada (los datos se pierden al cerrar).
var connectionString = NormalizePostgresConnectionString(
    builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(connectionString)) options.UseInMemoryDatabase("GestionEscolarDemo");
    else options.UseNpgsql(connectionString);
});

// ---- Identidad (usuarios y claves) ----
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        // La clave inicial es el DNI (solo números), así que no exigimos mayúsculas ni símbolos.
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;

        // Bloqueo temporal tras 5 intentos fallidos: el DNI no es secreto, así que esto
        // frena a alguien que intente adivinar la clave de otro.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddSignInManager()
    .AddDefaultTokenProviders() // necesario para "restablecer clave al DNI"
    .AddClaimsPrincipalFactory<AppClaimsFactory>() // mete rol y persona en la cookie
    .AddEntityFrameworkStores<AppDbContext>();

// ---- Cookie de sesión ----
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "colegio-session";
    options.Cookie.HttpOnly = true;            // inaccesible desde JavaScript
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;          // se renueva mientras se use
    // Es una API: en vez de redirigir a una página de login, respondemos 401/403
    // y el frontend decide qué mostrar.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Si se desactiva una cuenta o se cambia la clave, las sesiones abiertas se invalidan
// en el próximo minuto (por defecto Identity lo revisa cada 30 minutos).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// ---- Servicios propios ----
// Scoped = una instancia por request (igual que el DbContext que usan).
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<PeopleService>();
builder.Services.AddScoped<ReportCardService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor(); // AuditService lee quién hace cada acción

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddProblemDetails(); // errores no controlados → JSON estándar en vez de HTML
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy("LocalAngular", policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// =====================================================================================
// Pipeline HTTP (el orden importa)
// =====================================================================================

// Render pone un proxy delante: estos headers le dicen a la app que el usuario entró por HTTPS.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Por defecto solo se confía en proxies locales; el proxy de Render tiene otra IP.
// Sin esto, la auditoría registraría la IP del proxy en vez de la del usuario.
forwarded.KnownProxies.Clear();
forwarded.KnownNetworks.Clear();
app.UseForwardedHeaders(forwarded);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();          // documentación de la API en /openapi/v1.json
    app.UseCors("LocalAngular"); // permite que `ng serve` (puerto 4200) llame a la API
}

// Archivos del frontend Angular compilado (wwwroot).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Endpoint de salud: lo usa Render para saber si la app está viva.
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapControllers();
// Cualquier otra ruta (ej. /boletin) la resuelve Angular en el navegador.
app.MapFallbackToFile("index.html");

// Crear/actualizar tablas y cargar datos iniciales antes de aceptar requests.
await using (var scope = app.Services.CreateAsyncScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration);
}

app.Run();

// -------------------------------------------------------------------------------------
// Neon entrega la conexión como URL (postgresql://usuario:clave@host/base).
// Npgsql necesita el formato "Host=...;Username=...", así que la convertimos.
// -------------------------------------------------------------------------------------
static string? NormalizePostgresConnectionString(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString) ||
        (!connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
         !connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)))
    {
        return connectionString;
    }

    var uri = new Uri(connectionString);
    var credentials = uri.UserInfo.Split(':', 2);
    if (credentials.Length != 2)
    {
        throw new InvalidOperationException("La URL de PostgreSQL no contiene usuario y contraseña.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1]),
        SslMode = SslMode.Require
    }.ConnectionString;
}
