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
    /// Inscribe (o cambia de curso) a un alumno dentro del año lectivo del curso destino.
    /// Desactiva la inscripción anterior DE ESE MISMO AÑO en vez de borrarla, para no perder las
    /// notas ya cargadas. Las inscripciones de otros años no se tocan (son el historial).
    /// </summary>
    public async Task EnrollAsync(int studentId, int courseId)
    {
        var yearId = await db.Courses.Where(x => x.Id == courseId).Select(x => x.SchoolYearId).SingleAsync();
        var enrollments = await db.Enrollments
            .Where(x => x.StudentId == studentId && x.Course.SchoolYearId == yearId)
            .ToListAsync();
        foreach (var other in enrollments) other.IsActive = other.CourseId == courseId;

        if (enrollments.All(x => x.CourseId != courseId))
        {
            db.Enrollments.Add(new Enrollment { StudentId = studentId, CourseId = courseId, IsActive = true });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Vincula dos alumnos como hermanos: quedan en el mismo grupo familiar. Si alguno ya tenía
    /// grupo, el otro se suma a ese grupo (y si ambos tenían, se unen los dos grupos).
    /// </summary>
    public async Task<string?> LinkSiblingsAsync(int studentId, int siblingId)
    {
        if (studentId == siblingId) return "Elegí otro alumno.";
        var students = await db.People
            .Where(x => (x.Id == studentId || x.Id == siblingId) && x.Type == PersonTypes.Alumno)
            .ToListAsync();
        if (students.Count != 2) return "Los dos tienen que ser alumnos.";

        var a = students.Single(x => x.Id == studentId);
        var b = students.Single(x => x.Id == siblingId);
        var groupId = a.FamilyGroupId ?? b.FamilyGroupId
            ?? (await db.People.MaxAsync(x => (int?)x.FamilyGroupId) ?? 0) + 1;

        // Si los dos ya tenían grupos distintos, todo el grupo de B pasa al de A.
        if (b.FamilyGroupId is not null && b.FamilyGroupId != groupId)
        {
            var oldGroup = b.FamilyGroupId;
            await db.People.Where(x => x.FamilyGroupId == oldGroup)
                .ForEachAsync(x => x.FamilyGroupId = groupId);
        }
        a.FamilyGroupId = groupId;
        b.FamilyGroupId = groupId;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Quita a un alumno de su grupo familiar (si queda uno solo en el grupo, también se libera).</summary>
    public async Task UnlinkSiblingAsync(int studentId)
    {
        var student = await db.People.SingleAsync(x => x.Id == studentId);
        var groupId = student.FamilyGroupId;
        if (groupId is null) return;
        student.FamilyGroupId = null;

        var remaining = await db.People.Where(x => x.FamilyGroupId == groupId && x.Id != studentId).ToListAsync();
        if (remaining.Count == 1) remaining[0].FamilyGroupId = null;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Borrado definitivo, solo para personas cargadas por error: si tiene notas, asistencia
    /// o asignaciones, se rechaza (en ese caso hay que desactivarla).
    /// </summary>
    public async Task<string?> DeleteAsync(int personId)
    {
        var person = await db.People.SingleOrDefaultAsync(x => x.Id == personId);
        if (person is null) return "La persona no existe.";

        var hasData =
            await db.ReportCardEntries.AnyAsync(x => x.Enrollment.StudentId == personId) ||
            await db.Attendances.AnyAsync(x => x.Enrollment.StudentId == personId) ||
            await db.TeachingAssignments.AnyAsync(x => x.TeacherId == personId) ||
            await db.PreceptorAssignments.AnyAsync(x => x.PreceptorId == personId);
        if (hasData) return "Tiene notas, asistencia o asignaciones: no se puede borrar. Desactivala en su lugar.";

        db.Enrollments.RemoveRange(db.Enrollments.Where(x => x.StudentId == personId));
        db.People.Remove(person); // la cuenta se borra en cascada
        await db.SaveChangesAsync();
        return null;
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
