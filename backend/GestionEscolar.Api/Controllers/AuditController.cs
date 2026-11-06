using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Auditoría (solo Administrador): ingresos al sistema, cambios administrativos e historial
/// global de cambios de notas. Todo con filtros y paginación.
/// El Director NO tiene acceso: la auditoría sirve justamente para controlar a todos.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Administrador)]
[Route("api/audit")]
public sealed class AuditController(AppDbContext db) : ControllerBase
{
    /// <summary>Ingresos (kind=Acceso) o cambios administrativos (kind=Cambio).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string kind = AuditKinds.Acceso, [FromQuery] string? search = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 10, 100);
        page = Math.Max(1, page);

        var query = db.AuditEntries.AsNoTracking().Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                (x.UserDni != null && x.UserDni.Contains(term)) ||
                (x.UserName != null && x.UserName.ToLower().Contains(term)) ||
                x.Action.ToLower().Contains(term) ||
                (x.Detail != null && x.Detail.ToLower().Contains(term)));
        }
        // Las fechas del filtro son días completos (hora argentina aprox.: se toma UTC-3).
        if (from is not null) query = query.Where(x => x.AtUtc >= from.Value.ToDateTime(TimeOnly.MinValue).AddHours(3));
        if (to is not null) query = query.Where(x => x.AtUtc < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(3));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.AtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.AtUtc, x.UserDni, x.UserName, x.Role, x.Action, x.Detail, x.Ip })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Historial global de cambios de notas con filtros por curso, materia y alumno (ADM-08).</summary>
    [HttpGet("grades")]
    public async Task<IActionResult> Grades(
        [FromQuery] int? courseId = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 10, 100);
        page = Math.Max(1, page);

        var query = db.ReportCardChanges.AsNoTracking();
        if (courseId is not null) query = query.Where(x => x.ReportCardEntry.Enrollment.CourseId == courseId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.ReportCardEntry.Enrollment.Student.FullName.ToLower().Contains(term) ||
                x.ReportCardEntry.Enrollment.Student.Dni.Contains(term) ||
                x.ReportCardEntry.Subject.Name.ToLower().Contains(term) ||
                x.ChangedBy.ToLower().Contains(term));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.ChangedAtUtc, x.ChangedBy, x.Field, x.OldValue, x.NewValue,
                Student = x.ReportCardEntry.Enrollment.Student.FullName,
                Subject = x.ReportCardEntry.Subject.Name,
                x.ReportCardEntry.Enrollment.Course.Nivel,
                x.ReportCardEntry.Enrollment.Course.Level,
                x.ReportCardEntry.Enrollment.Course.Division
            })
            .ToListAsync();

        return Ok(new
        {
            total, page, pageSize,
            items = rows.Select(x => new
            {
                x.ChangedAtUtc, x.ChangedBy, x.Field, x.OldValue, x.NewValue, x.Student, x.Subject,
                Course = Course.LabelOf(x.Nivel, x.Level, x.Division)
            })
        });
    }
}
