using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GestionEscolar.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive) return Unauthorized(new { message = "Usuario o contraseña incorrectos." });

        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, false);
        if (!result.Succeeded) return Unauthorized(new { message = "Usuario o contraseña incorrectos." });
        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null || !user.IsActive ? Unauthorized() : Ok(ToResponse(user));
    }

    [AllowAnonymous]
    [HttpGet("demo-accounts")]
    public IActionResult DemoAccounts() => Ok(new[]
    {
        new { profile = "Secretaría / Dirección", email = "secretaria@demo.edu" },
        new { profile = "Docente", email = "docente@demo.edu" },
        new { profile = "Preceptor", email = "preceptor@demo.edu" },
        new { profile = "Responsable", email = "familia@demo.edu" }
    });

    private static object ToResponse(ApplicationUser user) => new
    {
        user.Id,
        user.Email,
        user.DisplayName,
        user.Profile,
        user.PersonId
    };
}

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
