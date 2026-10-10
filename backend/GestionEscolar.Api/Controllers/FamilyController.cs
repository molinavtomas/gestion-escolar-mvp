using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/family")]
public sealed class FamilyController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace()
    {
        var user = await CurrentFamily();
        if (user is null) return Forbid();
        var children = await db.FamilyLinks.AsNoTracking().Where(x => x.ResponsibleId == user.PersonId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                x.StudentId,
                Name = x.Student.FullName,
                x.Relationship,
                Enrollment = db.Enrollments.Where(e => e.StudentId == x.StudentId && e.IsActive)
                    .Select(e => new { e.Id, e.CourseId, Course = e.Course.Name + " " + e.Course.Division }).FirstOrDefault()
            }).ToListAsync();
        var terms = await db.Terms.AsNoTracking().OrderBy(x => x.StartDate).Select(x => new { x.Id, x.Name, x.AreAveragesPublished, x.IsReportCardPublished }).ToListAsync();
        return Ok(new { children, terms });
    }

    [HttpGet("children/{studentId:int}/overview")]
    public async Task<IActionResult> ChildOverview(int studentId, [FromQuery] int termId)
    {
        var user = await CurrentFamily();
        if (user is null || !await db.FamilyLinks.AnyAsync(x => x.ResponsibleId == user.PersonId && x.StudentId == studentId && x.IsActive)) return Forbid();
        var enrollment = await db.Enrollments.AsNoTracking().Where(x => x.StudentId == studentId && x.IsActive)
            .Select(x => new { x.Id, x.CourseId, Student = x.Student.FullName, Course = x.Course.Name + " " + x.Course.Division }).FirstOrDefaultAsync();
        if (enrollment is null) return NotFound(new { message = "El alumno no tiene inscripción activa." });
        var term = await db.Terms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == termId);
        if (term is null) return NotFound();

        var grades = await db.Grades.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id && x.IsPublished && x.Evaluation.TermId == termId)
            .OrderBy(x => x.Evaluation.TeachingAssignment.Subject.Name).ThenBy(x => x.Evaluation.Date)
            .Select(x => new
            {
                Subject = x.Evaluation.TeachingAssignment.Subject.Name,
                Evaluation = x.Evaluation.Title,
                x.Evaluation.Date,
                x.Score,
                x.Observation
            }).ToListAsync();
        var attendance = await db.Attendances.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id && x.Date >= term.StartDate && x.Date <= term.EndDate)
            .OrderByDescending(x => x.Date).Select(x => new { x.Date, x.Status, x.Observation }).ToListAsync();
        var subjectAverages = grades.GroupBy(x => x.Subject).Select(x => new { subject = x.Key, average = Math.Round(x.Average(g => g.Score), 2), grades = x.Count() }).ToList();
        double? average = term.AreAveragesPublished && grades.Count > 0 ? Math.Round(grades.Average(x => x.Score), 2) : null;
        var attendanceSummary = AttendanceStatuses.All.ToDictionary(status => status, status => attendance.Count(x => x.Status == status));

        return Ok(new
        {
            enrollment.Student,
            enrollment.Course,
            term = term.Name,
            term.AreAveragesPublished,
            term.IsReportCardPublished,
            grades,
            attendance,
            average,
            subjectAverages = term.IsReportCardPublished ? subjectAverages : [],
            attendanceSummary
        });
    }

    private async Task<ApplicationUser?> CurrentFamily()
    {
        var user = await userManager.GetUserAsync(User);
        return user is { IsActive: true, Profile: Profiles.Family, PersonId: not null } ? user : null;
    }
}
