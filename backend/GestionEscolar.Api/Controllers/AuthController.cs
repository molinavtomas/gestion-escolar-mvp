using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Inicio y cierre de sesión. La sesión se guarda en una cookie HttpOnly
/// (el JavaScript del navegador no la puede leer, lo que la protege de robos por XSS).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Ingreso con DNI y clave. Las familias ingresan con el DNI del alumno.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        const string invalid = "DNI o clave incorrectos.";
        var dni = PeopleService.NormalizeDni(request.Dni);
        if (dni is null) return Unauthorized(new { message = invalid });

        var user = await userManager.Users.SingleOrDefaultAsync(x => x.UserName == dni);
        if (user is null || !user.IsActive) return Unauthorized(new { message = invalid });

        // lockoutOnFailure: true → tras varios intentos fallidos la cuenta se bloquea unos minutos.
        // Importante porque el DNI no es secreto: frena a quien intente adivinar claves.
        var result = await signInManager.PasswordSignInAsync(user, request.Password ?? string.Empty,
            isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return Unauthorized(new { message = "Demasiados intentos fallidos. Esperá 5 minutos o pedí a Secretaría que restablezca tu clave." });
        if (!result.Succeeded)
            return Unauthorized(new { message = invalid });

        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>Datos de la sesión actual. El frontend lo llama al abrir la página.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null || !user.IsActive ? Unauthorized() : Ok(ToResponse(user));
    }

    /// <summary>Cambio de clave voluntario (recomendado, ya que el DNI no es secreto).</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { message = "La nueva clave debe tener al menos 6 caracteres." });

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { message = "La clave actual no es correcta." });

        user.UsesDefaultPassword = false;
        await userManager.UpdateAsync(user);
        // Renueva la cookie (cambiar la clave invalida el "security stamp" de la sesión anterior).
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    /// <summary>
    /// Cuentas de prueba para la pantalla de login. Solo se devuelven si la app está en modo demo
    /// (Seed:DemoData = true); con datos reales devuelve una lista vacía.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("demo-accounts")]
    public IActionResult DemoAccounts()
    {
        if (!configuration.GetValue("Seed:DemoData", true)) return Ok(Array.Empty<object>());
        return Ok(SeedData.DemoAccounts.Select(x => new { label = x.Label, dni = x.Dni }));
    }

    private static object ToResponse(ApplicationUser user) => new
    {
        user.DisplayName,
        user.Role,
        user.PersonId,
        Dni = user.UserName,
        user.UsesDefaultPassword
    };
}

public sealed record LoginRequest(string? Dni, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
