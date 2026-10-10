using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/preceptor")]
public sealed class PreceptorController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace()
    {
        var user = await CurrentPreceptor();
        if (user is null) return Forbid();
        var courses = await db.PreceptorAssignments.AsNoTracking().Where(x => x.PreceptorId == user.PersonId && x.IsActive)
            .OrderBy(x => x.Course.Name).Select(x => new { x.CourseId, Label = x.Course.Name + " " + x.Course.Division }).ToListAsync();
        var terms = await db.Terms.AsNoTracking().OrderBy(x => x.StartDate).Select(x => new { x.Id, x.Name }).ToListAsync();
        return Ok(new { courses, terms, statuses = AttendanceStatuses.All });
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] int courseId, [FromQuery] DateOnly date, [FromQuery] int? termId = null)
    {
        var user = await CurrentPreceptor();
        if (user is null || !await Assigned(user, courseId)) return Forbid();
        var rows = await db.Enrollments.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                EnrollmentId = x.Id,
                Student = x.Student.FullName,
                Identifier = x.Student.Identifier,
                Attendance = db.Attendances.Where(a => a.EnrollmentId == x.Id && a.Date == date)
                    .Select(a => new { a.Id, a.Status, a.Observation }).SingleOrDefault(),
                PublishedGrades = db.Grades.Count(g => g.EnrollmentId == x.Id && g.IsPublished && (termId == null || g.Evaluation.TermId == termId)),
                Average = db.Grades.Where(g => g.EnrollmentId == x.Id && g.IsPublished && (termId == null || g.Evaluation.TermId == termId))
                    .Select(g => (double?)g.Score).Average()
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("attendance")]
    public async Task<IActionResult> SaveAttendance(AttendanceBatchRequest request)
    {
        var user = await CurrentPreceptor();
        if (user is null || !await Assigned(user, request.CourseId)) return Forbid();
        var validEnrollments = await db.Enrollments.Where(x => x.CourseId == request.CourseId && x.IsActive).Select(x => x.Id).ToListAsync();
        foreach (var entry in request.Entries)
        {
            if (!validEnrollments.Contains(entry.EnrollmentId)) continue;
            if (!AttendanceStatuses.All.Contains(entry.Status)) return BadRequest(new { message = "Estado de asistencia inválido." });
            var attendance = await db.Attendances.SingleOrDefaultAsync(x => x.EnrollmentId == entry.EnrollmentId && x.Date == request.Date);
            if (attendance is null)
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

    [HttpGet("course-summary")]
    public async Task<IActionResult> CourseSummary([FromQuery] int courseId, [FromQuery] int termId)
    {
        var user = await CurrentPreceptor();
        if (user is null || !await Assigned(user, courseId)) return Forbid();
        var rows = await db.Enrollments.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive).OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                Student = x.Student.FullName,
                Present = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == "Presente"),
                Absent = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == "Ausente"),
                Late = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == "Tarde"),
                Justified = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == "Justificado"),
                PublishedGrades = db.Grades.Count(g => g.EnrollmentId == x.Id && g.IsPublished && g.Evaluation.TermId == termId),
                Average = db.Grades.Where(g => g.EnrollmentId == x.Id && g.IsPublished && g.Evaluation.TermId == termId).Select(g => (double?)g.Score).Average()
            }).ToListAsync();
        return Ok(rows);
    }

    private async Task<ApplicationUser?> CurrentPreceptor()
    {
        var user = await userManager.GetUserAsync(User);
        return user is { IsActive: true, Profile: Profiles.Preceptor, PersonId: not null } ? user : null;
    }

    private Task<bool> Assigned(ApplicationUser user, int courseId) => db.PreceptorAssignments.AnyAsync(x => x.PreceptorId == user.PersonId && x.CourseId == courseId && x.IsActive);
}

public sealed record AttendanceEntryRequest(int EnrollmentId, string Status, string? Observation);
public sealed record AttendanceBatchRequest(int CourseId, DateOnly Date, IReadOnlyList<AttendanceEntryRequest> Entries);
