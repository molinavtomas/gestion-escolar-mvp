using System.Xml.Linq;
using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace GestionEscolar.Api.Auth;

/// <summary>
/// Guarda en la base de datos las llaves con las que ASP.NET firma las cookies de sesión.
///
/// ¿Por qué? Por defecto esas llaves viven en el disco del servidor. En Render el disco se borra
/// en cada reinicio o despliegue, se generan llaves nuevas y TODAS las sesiones abiertas dejan de
/// ser válidas ("Tu sesión expiró"). Guardándolas en PostgreSQL sobreviven a los reinicios.
///
/// Es la misma idea que el paquete Microsoft.AspNetCore.DataProtection.EntityFrameworkCore,
/// escrita a mano en pocas líneas para no sumar dependencias.
/// </summary>
public sealed class DbXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        // La protección de datos es un servicio "singleton" y el DbContext es "scoped":
        // por eso abrimos un scope propio cada vez que necesitamos la base.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.DataProtectionKeys
            .Select(x => x.Xml)
            .AsEnumerable()
            .Select(XElement.Parse)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });
        db.SaveChanges();
    }
}
