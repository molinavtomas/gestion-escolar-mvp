using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Services;

/// <summary>
/// Alta de personas con su cuenta e inscripción. Lo usan el alta individual,
/// la importación masiva y la carga de datos demo, así la regla vive en un solo lugar:
///   usuario = DNI, clave inicial = DNI.
/// </summary>
public sealed class PeopleService(AppDbContext db, UserManager<ApplicationUser> userManager)
{
    /// <summary>
    /// Limpia un DNI tal como lo escribe la gente ("30.123.456", "30 123 456")
    /// y devuelve solo los dígitos, o null si no parece un DNI válido.
    /// </summary>
    public static string? NormalizeDni(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        // Los DNI argentinos tienen 7 u 8 dígitos; dejamos margen de 6 a 9.
        return digits.Length is >= 6 and <= 9 ? digits : null;
    }

    /// <summary>
    /// Crea una persona y su cuenta. Si es alumno y se indica curso, también lo inscribe.
    /// Devuelve la persona creada o un mensaje de error legible para mostrar en pantalla.
    /// </summary>
    public async Task<(Person? Person, string? Error)> CreateAsync(
        string fullName, string? rawDni, string type, string? email = null, string? phone = null, int? courseId = null)
    {
        // ---- Validaciones (antes de tocar la base) ----
        fullName = (fullName ?? string.Empty).Trim();
        if (fullName.Length < 3) return (null, "El nombre es obligatorio.");
        var dni = NormalizeDni(rawDni);
        if (dni is null) return (null, $"DNI inválido para {fullName}.");
        if (!PersonTypes.All.Contains(type)) return (null, "Tipo de persona inválido.");
        if (await db.People.AnyAsync(x => x.Dni == dni)) return (null, $"Ya existe una persona con DNI {dni}.");
        if (courseId is not null && !await db.Courses.AnyAsync(x => x.Id == courseId)) return (null, "El curso no existe.");

        // ---- Persona ----
        var person = new Person
        {
            FullName = fullName,
            Dni = dni,
            Type = type,
            Email = Clean(email),
            Phone = Clean(phone)
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        // ---- Cuenta: usuario y clave = DNI ----
        var account = new ApplicationUser
        {
            UserName = dni,
            DisplayName = fullName,
            Role = PersonTypes.RoleFor(type),
            PersonId = person.Id,
            UsesDefaultPassword = true
        };
        var result = await userManager.CreateAsync(account, dni);
        if (!result.Succeeded)
        {
            // Sin cuenta la persona no sirve: deshacemos el alta para no dejar datos a medias.
            db.People.Remove(person);
            await db.SaveChangesAsync();
            return (null, string.Join(" ", result.Errors.Select(x => x.Description)));
        }

        // ---- Inscripción (solo alumnos) ----
        if (type == PersonTypes.Alumno && courseId is not null)
        {
            await EnrollAsync(person.Id, courseId.Value);
        }

        return (person, null);
    }

    /// <summary>
    /// Inscribe (o cambia de curso) a un alumno. Desactiva la inscripción anterior en vez de
    /// borrarla, para no perder las notas ya cargadas en el curso anterior.
    /// </summary>
    public async Task EnrollAsync(int studentId, int courseId)
    {
        var enrollments = await db.Enrollments.Where(x => x.StudentId == studentId).ToListAsync();
        foreach (var other in enrollments) other.IsActive = other.CourseId == courseId;

        if (enrollments.All(x => x.CourseId != courseId))
        {
            db.Enrollments.Add(new Enrollment { StudentId = studentId, CourseId = courseId, IsActive = true });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Vuelve la clave al DNI (para cuando alguien se la olvida).</summary>
    public async Task<string?> ResetPasswordToDniAsync(int personId)
    {
        var account = await db.Users.Include(x => x.Person).SingleOrDefaultAsync(x => x.PersonId == personId);
        if (account is null) return "La persona no tiene cuenta.";

        var token = await userManager.GeneratePasswordResetTokenAsync(account);
        var result = await userManager.ResetPasswordAsync(account, token, account.Person.Dni);
        if (!result.Succeeded) return string.Join(" ", result.Errors.Select(x => x.Description));

        account.UsesDefaultPassword = true;
        // Si estaba bloqueada por intentos fallidos, la desbloqueamos.
        await userManager.SetLockoutEndDateAsync(account, null);
        await userManager.ResetAccessFailedCountAsync(account);
        await userManager.UpdateAsync(account);
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
