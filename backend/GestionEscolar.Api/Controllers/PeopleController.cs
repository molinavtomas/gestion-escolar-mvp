using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Personas y sus cuentas.
///  - Ver: Administrador, Director, Secretaría y Preceptor.
///  - Alta/baja/edición: Administrador, Director y Secretaría, pero solo el Administrador
///    puede tocar Directores y Administradores (ver Permissions.CanManagePersonType).
///  - Restablecer clave: según Permissions.CanResetPasswordOf.
///  - Borrado definitivo: solo Administrador y solo si la persona no tiene datos asociados.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.ViewAll)]
[Route("api/people")]
public sealed class PeopleController(AppDbContext db, PeopleService people, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Listado paginado con filtros. Cada fila trae qué acciones puede hacer el usuario
    /// actual sobre esa persona (así el frontend no repite las reglas).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? type, [FromQuery] string? nivel,
        [FromQuery] int? courseId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        page = Math.Max(1, page);

        var query = db.People.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.Type == type);
        if (status == "activos") query = query.Where(x => x.IsActive);
        if (status == "inactivos") query = query.Where(x => !x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Dni.Contains(term));
        }
        // Filtros por curso/nivel: solo tienen sentido para alumnos del año activo.
        if (courseId is not null)
            query = query.Where(x => db.Enrollments.Any(e => e.StudentId == x.Id && e.IsActive && e.CourseId == courseId));
        else if (!string.IsNullOrWhiteSpace(nivel))
            query = query.Where(x => db.Enrollments.Any(e => e.StudentId == x.Id && e.IsActive && e.Course.SchoolYear.IsActive && e.Course.Nivel == nivel));

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(x => x.FullName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.FullName, x.Dni, x.Email, x.Phone, x.Type, x.IsActive, x.FamilyGroupId,
                Course = db.Enrollments.Where(e => e.StudentId == x.Id && e.IsActive && e.Course.SchoolYear.IsActive)
                    .Select(e => new { e.CourseId, e.Course.Nivel, e.Course.Level, e.Course.Division }).FirstOrDefault(),
                UsesDefaultPassword = db.Users.Where(u => u.PersonId == x.Id).Select(u => u.UsesDefaultPassword).FirstOrDefault(),
                Siblings = db.People.Where(s => x.FamilyGroupId != null && s.FamilyGroupId == x.FamilyGroupId && s.Id != x.Id)
                    .Select(s => s.FullName).ToList()
            })
            .ToListAsync();

        var role = User.Role();
        var items = rows.Select(x => new
        {
            x.Id, x.FullName, x.Dni, x.Email, x.Phone, x.Type, x.IsActive, x.UsesDefaultPassword, x.Siblings,
            CourseId = x.Course?.CourseId,
            Course = x.Course is null ? null : Course.LabelOf(x.Course.Nivel, x.Course.Level, x.Course.Division),
            // Acciones permitidas sobre ESTA fila para el usuario actual.
            CanEdit = Permissions.CanManagePersonType(role, x.Type),
            CanReset = Permissions.CanResetPasswordOf(role, x.Type),
            CanDelete = role == Roles.Administrador
        });
        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Alta individual. Crea la cuenta con usuario y clave = DNI.</summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpPost]
    public async Task<IActionResult> Create(PersonRequest request)
    {
        if (!Permissions.CanManagePersonType(User.Role(), request.Type))
            return StatusCode(403, new { message = "Solo un Administrador puede crear Directores o Administradores." });

        var (person, error) = await people.CreateAsync(
            request.FullName, request.Dni, request.Type, request.Email, request.Phone, request.CourseId);
        if (error is not null) return BadRequest(new { message = error });

        audit.Change("Alta de persona", $"{person!.FullName} (DNI {person.Dni}) - {person.Type}");
        await db.SaveChangesAsync();
        return Ok(new { person.Id });
    }

    /// <summary>
    /// Edición de datos. Si cambia el DNI, también cambia el usuario de la cuenta.
    /// Permite cambiar el tipo (rol) entre tipos de personal; un alumno no puede pasar a personal.
    /// </summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PersonRequest request)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();

        var role = User.Role();
        if (!Permissions.CanManagePersonType(role, person.Type) || !Permissions.CanManagePersonType(role, request.Type))
            return StatusCode(403, new { message = "No tenés permiso para editar a esta persona o asignarle ese rol." });
        if ((person.Type == PersonTypes.Alumno) != (request.Type == PersonTypes.Alumno))
            return BadRequest(new { message = "Un alumno no puede pasar a ser personal (ni al revés)." });

        var dni = PeopleService.NormalizeDni(request.Dni);
        if (dni is null) return BadRequest(new { message = "DNI inválido." });
        if (string.IsNullOrWhiteSpace(request.FullName)) return BadRequest(new { message = "El nombre es obligatorio." });
        if (dni != person.Dni && await db.People.AnyAsync(x => x.Dni == dni))
            return BadRequest(new { message = $"Ya existe una persona con DNI {dni}." });

        var changes = new List<string>();
        if (person.Type != request.Type) changes.Add($"rol {person.Type} → {request.Type}");
        if (person.Dni != dni) changes.Add($"DNI {person.Dni} → {dni}");

        person.FullName = request.FullName.Trim();
        person.Dni = dni;
        person.Type = request.Type;
        person.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        person.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        // Mantener la cuenta sincronizada: usuario = DNI, nombre visible y rol.
        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == id);
        if (account is not null)
        {
            var roleChanged = account.Role != PersonTypes.RoleFor(person.Type);
            account.UserName = dni;
            account.NormalizedUserName = dni; // Identity busca por la versión "normalizada"; para dígitos es igual.
            account.DisplayName = person.FullName;
            account.Role = PersonTypes.RoleFor(person.Type);
            // Si cambió el rol, cerramos sus sesiones para que vuelva a entrar con los permisos nuevos.
            if (roleChanged) account.SecurityStamp = Guid.NewGuid().ToString();
        }

        audit.Change("Edición de persona", $"{person.FullName} (DNI {person.Dni}){(changes.Count > 0 ? ": " + string.Join(", ", changes) : "")}");
        await db.SaveChangesAsync();

        // Cambio de curso (solo alumnos).
        if (person.Type == PersonTypes.Alumno && request.CourseId is not null)
        {
            await people.EnrollAsync(person.Id, request.CourseId.Value);
        }
        return NoContent();
    }

    /// <summary>Activa o desactiva a la persona y su cuenta (no se borra nada).</summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpPost("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        if (!Permissions.CanManagePersonType(User.Role(), person.Type))
            return StatusCode(403, new { message = "Solo un Administrador puede dar de baja Directores o Administradores." });
        if (person.Id == User.PersonId())
            return BadRequest(new { message = "No podés desactivar tu propia cuenta." });

        person.IsActive = !person.IsActive;

        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == id);
        if (account is not null)
        {
            account.IsActive = person.IsActive;
            // Cambiar el "security stamp" invalida las sesiones abiertas de esa cuenta
            // (se cierran en el próximo minuto, ver Program.cs).
            account.SecurityStamp = Guid.NewGuid().ToString();
        }

        // Si es alumno, también activamos/desactivamos su inscripción del año activo
        // para que deje de aparecer en las listas del curso.
        if (person.Type == PersonTypes.Alumno)
        {
            var enrollment = await db.Enrollments
                .Where(x => x.StudentId == id && x.Course.SchoolYear.IsActive)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();
            if (enrollment is not null) enrollment.IsActive = person.IsActive;
        }

        audit.Change(person.IsActive ? "Reactivación" : "Baja", $"{person.FullName} (DNI {person.Dni}) - {person.Type}");
        await db.SaveChangesAsync();
        return Ok(new { person.IsActive });
    }

    /// <summary>Vuelve la clave al DNI (y desbloquea la cuenta si estaba bloqueada).</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        if (!Permissions.CanResetPasswordOf(User.Role(), person.Type))
            return StatusCode(403, new { message = "No tenés permiso para restablecer la clave de esta persona." });

        var error = await people.ResetPasswordToDniAsync(id);
        if (error is not null) return BadRequest(new { message = error });

        audit.Change("Clave restablecida al DNI", $"{person.FullName} (DNI {person.Dni})");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Borrado definitivo (solo Administrador, solo si no tiene datos asociados).</summary>
    [Authorize(Roles = Roles.Administrador)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (person is null) return NotFound();
        if (person.Id == User.PersonId()) return BadRequest(new { message = "No podés borrar tu propia cuenta." });

        var error = await people.DeleteAsync(id);
        if (error is not null) return BadRequest(new { message = error });

        audit.Change("Borrado definitivo", $"{person.FullName} (DNI {person.Dni}) - {person.Type}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Vincula a dos alumnos como hermanos (la familia los ve a ambos con un solo ingreso).</summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpPost("{id:int}/siblings")]
    public async Task<IActionResult> LinkSibling(int id, SiblingRequest request)
    {
        var error = await people.LinkSiblingsAsync(id, request.SiblingId);
        if (error is not null) return BadRequest(new { message = error });

        var names = await db.People.Where(x => x.Id == id || x.Id == request.SiblingId).Select(x => x.FullName).ToListAsync();
        audit.Change("Hermanos vinculados", string.Join(" y ", names));
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Quita al alumno de su grupo de hermanos.</summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpDelete("{id:int}/siblings")]
    public async Task<IActionResult> UnlinkSibling(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        await people.UnlinkSiblingAsync(id);
        audit.Change("Hermanos desvinculados", person.FullName);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Importación masiva: se pega una lista copiada de Excel, una persona por línea,
    /// con el formato "Nombre completo [TAB o ; o ,] DNI". Las líneas con error se informan
    /// y no frenan al resto.
    /// </summary>
    [Authorize(Roles = Roles.PeopleManagers)]
    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportRequest request)
    {
        if (!Permissions.CanManagePersonType(User.Role(), request.Type))
            return StatusCode(403, new { message = "No tenés permiso para importar personas de ese tipo." });

        var created = 0;
        var errors = new List<string>();
        var lines = (request.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var (line, index) in lines.Select((line, i) => (line, i + 1)))
        {
            // Excel separa columnas con TAB al copiar; también aceptamos ; y , por comodidad.
            var parts = line.Split(['\t', ';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                errors.Add($"Línea {index}: falta el DNI (\"{line}\").");
                continue;
            }

            // Aceptamos el DNI en la primera o en la segunda columna: detectamos cuál tiene dígitos.
            var (name, dni) = PeopleService.NormalizeDni(parts[1]) is not null ? (parts[0], parts[1]) : (parts[1], parts[0]);
            var (_, error) = await people.CreateAsync(name, dni, request.Type, courseId: request.CourseId);
            if (error is null) created++;
            else errors.Add($"Línea {index}: {error}");
        }

        audit.Change("Importación de personas", $"{created} {request.Type}(s) creados, {errors.Count} con error");
        await db.SaveChangesAsync();
        return Ok(new { created, errors });
    }
}

public sealed record PersonRequest(string FullName, string Dni, string Type, string? Email, string? Phone, int? CourseId);
public sealed record ImportRequest(string Type, int? CourseId, string? Text);
public sealed record SiblingRequest(int SiblingId);
