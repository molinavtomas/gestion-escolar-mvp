using System.Security.Claims;
using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GestionEscolar.Api.Auth;

/// <summary>
/// Arma la "identidad" que se guarda dentro de la cookie al iniciar sesión.
/// Agregamos el rol, el id de la persona y el nombre visible como claims, así los
/// controladores los leen de la cookie sin consultar la base en cada request.
/// </summary>
public sealed class AppClaimsFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    public const string PersonIdClaim = "person_id";
    public const string DisplayNameClaim = "display_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        // ClaimTypes.Role es el tipo que entiende [Authorize(Roles = "...")].
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        identity.AddClaim(new Claim(PersonIdClaim, user.PersonId.ToString()));
        identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        return identity;
    }
}

/// <summary>Atajos para leer los datos del usuario actual desde la cookie.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Id de la persona logueada (para Familia es el alumno).</summary>
    public static int PersonId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(AppClaimsFactory.PersonIdClaim)
                  ?? throw new InvalidOperationException("La sesión no tiene persona asociada."));

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimsFactory.DisplayNameClaim) ?? "Usuario";

    /// <summary>Rol de la sesión (uno de <see cref="Roles"/>).</summary>
    public static string Role(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    /// <summary>Administrador o Director: conducción del colegio.</summary>
    public static bool IsManagement(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Administrador) || user.IsInRole(Roles.Director);

    /// <summary>Puede ver todo lo académico: Administrador, Director, Secretaría y Preceptor.</summary>
    public static bool CanViewAll(this ClaimsPrincipal user) =>
        user.IsManagement() || user.IsInRole(Roles.Secretaria) || user.IsInRole(Roles.Preceptor);
}
