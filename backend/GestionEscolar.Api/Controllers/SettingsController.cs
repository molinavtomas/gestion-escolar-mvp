using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Datos institucionales públicos (sin iniciar sesión): el nombre y el logo se muestran
/// también en la pantalla de ingreso.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/settings")]
public sealed class SettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("public")]
    public async Task<IActionResult> Public()
    {
        var name = await db.SchoolSettings.Select(x => x.SchoolName).FirstOrDefaultAsync();
        return Ok(new { schoolName = name ?? "Colegio" });
    }

    /// <summary>
    /// Logo del colegio. Si el Administrador no subió uno, redirige al escudo incluido en la
    /// aplicación (/logo.png).
    /// </summary>
    [HttpGet("logo")]
    public async Task<IActionResult> Logo()
    {
        var logo = await db.SchoolSettings.Select(x => new { x.LogoData, x.LogoContentType }).FirstOrDefaultAsync();
        if (logo?.LogoData is null) return Redirect("/logo.png");
        // Cache corto: si se cambia el logo, se ve en unos minutos sin recargar a la fuerza.
        Response.Headers.CacheControl = "public, max-age=300";
        return File(logo.LogoData, logo.LogoContentType ?? "image/png");
    }
}
