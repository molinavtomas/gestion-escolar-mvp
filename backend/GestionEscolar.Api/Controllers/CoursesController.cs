using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Cursos (para los filtros) y asistencia.
///  - Ver: Administración, Dirección, Secretaría, Preceptor (todos los cursos) y Profesor (los suyos).
///  - Tomar asistencia: Administración/Dirección (todos) y Preceptor (sus cursos).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.ViewAll + "," + Roles.Profesor)]
[Route("api/courses")]
public sealed class CoursesController(AppDbContext db, AccessService access) : ControllerBase
{
    /// <summary>
    /// Datos para los filtros: años lectivos y cursos visibles para el usuario.
    /// Cada curso indica si el usuario puede tomar asistencia ahí.
    /// </summary>
    [HttpGet("filters")]
    public async Task<IActionResult> Filters()
    {
        var query = db.Courses.AsNoTracking().Where(x => x.IsActive);
        if (!User.CanViewAll())
        {
            // Profesor: solo los cursos donde dicta alguna materia.
            var personId = User.PersonId();
            query = query.Where(c => db.TeachingAssignments.Any(t => t.TeacherId == personId && t.CourseId == c.Id));
        }

        var courses = await query
            .Select(x => new { x.Id, x.Nivel, x.Level, x.Division, x.SchoolYearId })
            .ToListAsync();

        // Cursos donde puede tomar asistencia (conducción: todos; preceptor: los asignados).
        HashSet<int> attendanceCourses;
        if (User.IsManagement()) attendanceCourses = courses.Select(x => x.Id).ToHashSet();
        else if (User.IsInRole(Roles.Preceptor))
        {
            var personId = User.PersonId();
            attendanceCourses = (await db.PreceptorAssignments.Where(x => x.PreceptorId == personId).Select(x => x.CourseId).ToListAsync()).ToHashSet();
        }
        else attendanceCourses = [];

        var years = await db.SchoolYears.AsNoTracking().OrderByDescending(x => x.Year)
            .Select(x => new { x.Id, x.Year, x.IsActive }).ToListAsync();

        return Ok(new
        {
            years,
            niveles = Niveles.All.Select(n => new { nivel = n, levels = Niveles.LevelsOf(n) }),
            courses = courses
                .OrderBy(x => AcademicController.NivelOrder(x.Nivel)).ThenBy(x => x.Level).ThenBy(x => x.Division)
                .Select(x => new
                {
                    x.Id, x.Nivel, x.Level, x.Division, x.SchoolYearId,
                    Label = Course.LabelOf(x.Nivel, x.Level, x.Division),
                    CanTakeAttendance = attendanceCourses.Contains(x.Id)
                })
        });
    }

    // ================================================================ Asistencia

    /// <summary>Lista del curso con el estado de asistencia de la fecha indicada (si ya se cargó).</summary>
    [HttpGet("{courseId:int}/attendance")]
    public async Task<IActionResult> Attendance(int courseId, [FromQuery] DateOnly date)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        var rows = await db.Enrollments.AsNoTracking()
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
            .ToListAsync();
        return Ok(new { canEdit = await access.CanTakeAttendanceAsync(User, courseId), rows });
    }

    /// <summary>
    /// Guarda (o corrige) la asistencia de todo el curso para una fecha.
    /// A cada alumno que pasa a "Ausente" se le genera un aviso para su familia.
    /// </summary>
    [HttpPost("{courseId:int}/attendance")]
    public async Task<IActionResult> SaveAttendance(int courseId, AttendanceRequest request)
    {
        if (!await access.CanTakeAttendanceAsync(User, courseId)) return Forbid();
        if (request.Entries.Any(x => !AttendanceStatuses.All.Contains(x.Status)))
            return BadRequest(new { message = "Estado de asistencia inválido." });
        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest(new { message = "No se puede cargar asistencia de días futuros." });

        var enrollments = await db.Enrollments
            .Where(x => x.CourseId == courseId && x.IsActive)
            .Select(x => new { x.Id, x.StudentId })
            .ToDictionaryAsync(x => x.Id, x => x.StudentId);
        var validIds = enrollments.Keys.ToList();

        // Traemos de una vez lo ya cargado ese día para actualizar en lugar de duplicar.
        var existing = await db.Attendances
            .Where(x => x.Date == request.Date && validIds.Contains(x.EnrollmentId))
            .ToDictionaryAsync(x => x.EnrollmentId);

        foreach (var entry in request.Entries.Where(x => enrollments.ContainsKey(x.EnrollmentId)))
        {
            var isNew = !existing.TryGetValue(entry.EnrollmentId, out var attendance);
            if (isNew)
            {
                attendance = new Attendance { EnrollmentId = entry.EnrollmentId, Date = request.Date };
                db.Attendances.Add(attendance);
            }
            var becameAbsent = entry.Status == AttendanceStatuses.Ausente && (isNew || attendance!.Status != AttendanceStatuses.Ausente);

            attendance!.Status = entry.Status;
            attendance.Observation = string.IsNullOrWhiteSpace(entry.Observation) ? null : entry.Observation.Trim();
            attendance.UpdatedAtUtc = DateTime.UtcNow;

            // Aviso a la familia (FAM-16): se ve en "Avisos" del alumno.
            if (becameAbsent)
            {
                db.Notifications.Add(new Notification
                {
                    Kind = NotificationKinds.Inasistencia,
                    Title = "Inasistencia registrada",
                    Body = $"Se registró una inasistencia el {request.Date:dd/MM/yyyy}.",
                    StudentId = enrollments[entry.EnrollmentId],
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = User.DisplayName()
                });
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Totales de asistencia por alumno en el año, con alerta si supera el límite configurado.</summary>
    [HttpGet("{courseId:int}/attendance-summary")]
    public async Task<IActionResult> AttendanceSummary(int courseId)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        var threshold = await db.SchoolSettings.Select(x => x.AbsenceAlertThreshold).FirstOrDefaultAsync();
        var rows = await db.Enrollments.AsNoTracking()
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
            .ToListAsync();
        return Ok(new { threshold, rows });
    }
}

public sealed record AttendanceEntry(int EnrollmentId, string Status, string? Observation);
public sealed record AttendanceRequest(DateOnly Date, IReadOnlyList<AttendanceEntry> Entries);
