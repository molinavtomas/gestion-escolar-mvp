using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;

namespace GestionEscolar.Api.Services;

/// <summary>
/// Escribe registros de auditoría. Lee quién hace la acción desde la sesión actual
/// (por eso usa IHttpContextAccessor) para que los controladores solo digan QUÉ pasó.
/// </summary>
public sealed class AuditService(AppDbContext db, IHttpContextAccessor http)
{
    /// <summary>
    /// Registra un cambio administrativo (alta, baja, publicación, etc.).
    /// No guarda en la base por sí mismo: se guarda junto con el cambio en el SaveChanges
    /// del controlador, así nunca queda un registro de algo que no pasó.
    /// </summary>
    public void Change(string action, string? detail = null)
    {
        var user = http.HttpContext?.User;
        db.AuditEntries.Add(new AuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Kind = AuditKinds.Cambio,
            UserDni = user?.Identity?.Name,
            UserName = user is null ? null : user.DisplayName(),
            Role = user?.Role(),
            Action = action,
            Detail = Truncate(detail, 500),
            Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
    }

    /// <summary>Registra un intento de ingreso y lo guarda inmediatamente.</summary>
    public async Task AccessAsync(string? dni, string? name, string? role, string result)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Kind = AuditKinds.Acceso,
            UserDni = Truncate(dni, 20),
            UserName = name,
            Role = role,
            Action = result,
            Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync();
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
