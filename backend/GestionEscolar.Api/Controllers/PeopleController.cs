using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Gestión de personas y sus cuentas. Solo Dirección y Secretaría.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Staff)]
[Route("api/people")]
public sealed class PeopleController(AppDbContext db, PeopleService people) : ControllerBase
{
    /// <summary>Listado con búsqueda por nombre o DNI y filtro opcional por tipo.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? type)
    {
        var query = db.People.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Dni.Contains(term));
        }

        // Traemos los datos crudos y armamos la etiqueta del curso en memoria (más simple y portable).
        var rows = await query
            .OrderBy(x => x.FullName)
            .Take(500) // tope de seguridad para no traer miles de filas de golpe
            .Select(x => new
            {
                x.Id, x.FullName, x.Dni, x.Email, x.Phone, x.Type, x.IsActive,
                Course = db.Enrollments.Where(e => e.StudentId == x.Id && e.IsActive)
                    .Select(e => new { e.CourseId, e.Course.Level, e.Course.Division }).FirstOrDefault(),
                UsesDefaultPassword = db.Users.Where(u => u.PersonId == x.Id).Select(u => u.UsesDefaultPassword).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(rows.Select(x => new
        {
            x.Id, x.FullName, x.Dni, x.Email, x.Phone, x.Type, x.IsActive, x.UsesDefaultPassword,
            CourseId = x.Course?.CourseId,
            Course = x.Course is null ? null : Course.LabelOf(x.Course.Level, x.Course.Division)
        }));
    }

    /// <summary>Alta individual. Crea la cuenta con usuario y clave = DNI.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(PersonRequest request)
    {
        var (person, error) = await people.CreateAsync(
            request.FullName, request.Dni, request.Type, request.Email, request.Phone, request.CourseId);
        return error is null ? Ok(new { person!.Id }) : BadRequest(new { message = error });
    }

    /// <summary>Edición de datos. Si cambia el DNI, también cambia el usuario de la cuenta.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PersonRequest request)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();

        var dni = PeopleService.NormalizeDni(request.Dni);
        if (dni is null) return BadRequest(new { message = "DNI inválido." });
        if (string.IsNullOrWhiteSpace(request.FullName)) return BadRequest(new { message = "El nombre es obligatorio." });
        if (dni != person.Dni && await db.People.AnyAsync(x => x.Dni == dni))
            return BadRequest(new { message = $"Ya existe una persona con DNI {dni}." });

        person.FullName = request.FullName.Trim();
        person.Dni = dni;
        person.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        person.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        // Mantener la cuenta sincronizada: el usuario es el DNI y el nombre visible es el de la persona.
        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == id);
        if (account is not null)
        {
            account.UserName = dni;
            account.NormalizedUserName = dni; // Identity busca por la versión "normalizada"; para dígitos es igual.
            account.DisplayName = person.FullName;
        }

        // Cambio de curso (solo alumnos).
        await db.SaveChangesAsync();
        if (person.Type == PersonTypes.Alumno && request.CourseId is not null)
        {
            await people.EnrollAsync(person.Id, request.CourseId.Value);
        }
        return NoContent();
    }

    /// <summary>Activa o desactiva a la persona y su cuenta (no se borra nada).</summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        person.IsActive = !person.IsActive;

        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == id);
        if (account is not null)
        {
            account.IsActive = person.IsActive;
            // Cambiar el "security stamp" invalida las sesiones abiertas de esa cuenta
            // (se cierran en el próximo minuto, ver Program.cs).
            account.SecurityStamp = Guid.NewGuid().ToString();
        }

        // Si es alumno, también activamos/desactivamos su inscripción actual
        // para que deje de aparecer en las listas del curso.
        if (person.Type == PersonTypes.Alumno)
        {
            var enrollment = await db.Enrollments
                .Where(x => x.StudentId == id)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();
            if (enrollment is not null) enrollment.IsActive = person.IsActive;
        }

        await db.SaveChangesAsync();
        return Ok(new { person.IsActive });
    }

    /// <summary>Vuelve la clave al DNI (y desbloquea la cuenta si estaba bloqueada).</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var error = await people.ResetPasswordToDniAsync(id);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    /// <summary>
    /// Importación masiva: se pega una lista copiada de Excel, una persona por línea,
    /// con el formato "Nombre completo [TAB o ; o ,] DNI". Las líneas con error se informan
    /// y no frenan al resto.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportRequest request)
    {
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

        return Ok(new { created, errors });
    }
}

public sealed record PersonRequest(string FullName, string Dni, string Type, string? Email, string? Phone, int? CourseId);
public sealed record ImportRequest(string Type, int? CourseId, string? Text);
