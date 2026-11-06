using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Cursos y asistencia. Preceptores ven y cargan sus cursos; Dirección y Secretaría, todos.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Staff + "," + Roles.Preceptor)]
[Route("api/courses")]
public sealed class CoursesController(AppDbContext db, AccessService access) : ControllerBase
{
    /// <summary>Cursos visibles para el usuario (del año lectivo activo).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var query = db.Courses.AsNoTracking().Where(x => x.IsActive && x.SchoolYear.IsActive);
        if (!User.IsStaff())
        {
            var personId = User.PersonId();
            query = query.Where(c => db.PreceptorAssignments.Any(p => p.PreceptorId == personId && p.CourseId == c.Id));
        }

        var courses = await query.OrderBy(x => x.Level).ThenBy(x => x.Division)
            .Select(x => new { x.Id, x.Level, x.Division })
            .ToListAsync();
        return Ok(courses.Select(x => new { x.Id, Label = Course.LabelOf(x.Level, x.Division) }));
    }

    /// <summary>Alumnos de un curso, para listar y abrir boletines.</summary>
    [HttpGet("{courseId:int}/students")]
    public async Task<IActionResult> Students(int courseId)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        return Ok(await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new { x.StudentId, x.Student.FullName, x.Student.Dni })
            .ToListAsync());
    }

    // ================================================================ Asistencia

    /// <summary>Lista del curso con el estado de asistencia de la fecha indicada (si ya se cargó).</summary>
    [HttpGet("{courseId:int}/attendance")]
    public async Task<IActionResult> Attendance(int courseId, [FromQuery] DateOnly date)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        return Ok(await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                EnrollmentId = x.Id,
                Student = x.Student.FullName,
                x.Student.Dni,
                // null = todavía no se tomó asistencia ese día
                Status = db.Attendances.Where(a => a.EnrollmentId == x.Id && a.Date == date).Select(a => a.Status).FirstOrDefault(),
                Observation = db.Attendances.Where(a => a.EnrollmentId == x.Id && a.Date == date).Select(a => a.Observation).FirstOrDefault()
            })
            .ToListAsync());
    }

    /// <summary>Guarda (o corrige) la asistencia de todo el curso para una fecha.</summary>
    [HttpPost("{courseId:int}/attendance")]
    public async Task<IActionResult> SaveAttendance(int courseId, AttendanceRequest request)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        if (request.Entries.Any(x => !AttendanceStatuses.All.Contains(x.Status)))
            return BadRequest(new { message = "Estado de asistencia inválido." });

        var validEnrollments = await db.Enrollments
            .Where(x => x.CourseId == courseId && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync();

        // Traemos de una vez lo ya cargado ese día para actualizar en lugar de duplicar.
        var existing = await db.Attendances
            .Where(x => x.Date == request.Date && validEnrollments.Contains(x.EnrollmentId))
            .ToDictionaryAsync(x => x.EnrollmentId);

        foreach (var entry in request.Entries.Where(x => validEnrollments.Contains(x.EnrollmentId)))
        {
            if (!existing.TryGetValue(entry.EnrollmentId, out var attendance))
            {
                attendance = new Attendance { EnrollmentId = entry.EnrollmentId, Date = request.Date };
                db.Attendances.Add(attendance);
            }
            attendance.Status = entry.Status;
            attendance.Observation = string.IsNullOrWhiteSpace(entry.Observation) ? null : entry.Observation.Trim();
            attendance.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Totales de asistencia por alumno en todo el año.</summary>
    [HttpGet("{courseId:int}/attendance-summary")]
    public async Task<IActionResult> AttendanceSummary(int courseId)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        return Ok(await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                Student = x.Student.FullName,
                Present = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Presente),
                Absent = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Ausente),
                Late = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Tarde),
                Justified = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Justificado)
            })
            .ToListAsync());
    }
}

public sealed record AttendanceEntry(int EnrollmentId, string Status, string? Observation);
public sealed record AttendanceRequest(DateOnly Date, IReadOnlyList<AttendanceEntry> Entries);
