using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/teacher")]
public sealed class TeacherController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace()
    {
        var user = await CurrentTeacher();
        if (user is null) return Forbid();
        var assignments = await db.TeachingAssignments.AsNoTracking()
            .Where(x => x.IsActive && x.TeacherId == user.PersonId)
            .OrderBy(x => x.Course.Name).ThenBy(x => x.Subject.Name)
            .Select(x => new { x.Id, x.CourseId, Course = x.Course.Name + " " + x.Course.Division, x.SubjectId, Subject = x.Subject.Name })
            .ToListAsync();
        var terms = await db.Terms.AsNoTracking().OrderBy(x => x.StartDate).Select(x => new { x.Id, x.Name, x.StartDate, x.EndDate }).ToListAsync();
        return Ok(new { assignments, terms });
    }

    [HttpGet("gradebook")]
    public async Task<IActionResult> Gradebook([FromQuery] int assignmentId, [FromQuery] int termId, [FromQuery] int? evaluationId = null)
    {
        var user = await CurrentTeacher();
        if (user is null) return Forbid();
        var assignment = await db.TeachingAssignments.AsNoTracking()
            .Where(x => x.Id == assignmentId && x.TeacherId == user.PersonId && x.IsActive)
            .Select(x => new { x.Id, x.CourseId, Course = x.Course.Name + " " + x.Course.Division, Subject = x.Subject.Name })
            .SingleOrDefaultAsync();
        if (assignment is null) return Forbid();

        var evaluations = await db.Evaluations.AsNoTracking().Where(x => x.TeachingAssignmentId == assignmentId && x.TermId == termId)
            .OrderBy(x => x.Date).Select(x => new { x.Id, x.Title, x.Date }).ToListAsync();
        var selectedId = evaluationId ?? evaluations.LastOrDefault()?.Id;

        var students = await db.Enrollments.AsNoTracking().Where(x => x.CourseId == assignment.CourseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new { EnrollmentId = x.Id, StudentId = x.StudentId, Student = x.Student.FullName })
            .ToListAsync();

        var grades = selectedId is null
            ? new List<GradebookGrade>()
            : await db.Grades.AsNoTracking().Where(x => x.EvaluationId == selectedId)
                .Select(x => new GradebookGrade(x.Id, x.EnrollmentId, x.Score, x.Observation, x.IsPublished, x.UpdatedAtUtc)).ToListAsync();

        return Ok(new { assignment, evaluations, selectedEvaluationId = selectedId, students, grades });
    }

    [HttpGet("course-overview")]
    public async Task<IActionResult> CourseOverview([FromQuery] int courseId, [FromQuery] int termId)
    {
        var user = await CurrentTeacher();
        if (user is null || !await db.TeachingAssignments.AnyAsync(x => x.TeacherId == user.PersonId && x.CourseId == courseId && x.IsActive)) return Forbid();

        var rows = await db.Grades.AsNoTracking()
            .Where(x => x.Enrollment.CourseId == courseId && x.Evaluation.TermId == termId)
            .OrderBy(x => x.Enrollment.Student.FullName).ThenBy(x => x.Evaluation.TeachingAssignment.Subject.Name)
            .Select(x => new
            {
                Student = x.Enrollment.Student.FullName,
                Subject = x.Evaluation.TeachingAssignment.Subject.Name,
                Evaluation = x.Evaluation.Title,
                x.Score,
                x.Observation,
                x.IsPublished,
                CanEdit = x.Evaluation.TeachingAssignment.TeacherId == user.PersonId
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("evaluations")]
    public async Task<IActionResult> CreateEvaluation(EvaluationRequest request)
    {
        var user = await CurrentTeacher();
        if (user is null || !await db.TeachingAssignments.AnyAsync(x => x.Id == request.AssignmentId && x.TeacherId == user.PersonId && x.IsActive)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest(new { message = "El título es obligatorio." });
        var evaluation = new Evaluation { TeachingAssignmentId = request.AssignmentId, TermId = request.TermId, Title = request.Title.Trim(), Date = request.Date };
        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync();
        return Ok(new { evaluation.Id });
    }

    [HttpPost("grades")]
    public async Task<IActionResult> SaveGrades(GradeBatchRequest request)
    {
        var user = await CurrentTeacher();
        if (user is null) return Forbid();
        var evaluation = await db.Evaluations.Include(x => x.TeachingAssignment).SingleOrDefaultAsync(x => x.Id == request.EvaluationId);
        if (evaluation is null) return NotFound();
        if (evaluation.TeachingAssignment.TeacherId != user.PersonId) return Forbid();

        var validEnrollments = await db.Enrollments.Where(x => x.CourseId == evaluation.TeachingAssignment.CourseId && x.IsActive).Select(x => x.Id).ToListAsync();
        foreach (var entry in request.Grades)
        {
            if (!validEnrollments.Contains(entry.EnrollmentId)) continue;
            if (entry.Score is < 1 or > 10) return BadRequest(new { message = "Las notas deben estar entre 1 y 10." });

            var grade = await db.Grades.SingleOrDefaultAsync(x => x.EvaluationId == request.EvaluationId && x.EnrollmentId == entry.EnrollmentId);
            var oldScore = grade?.Score;
            var oldObservation = grade?.Observation;
            var oldPublished = grade?.IsPublished ?? false;
            if (grade is null)
            {
                grade = new Grade { EvaluationId = request.EvaluationId, EnrollmentId = entry.EnrollmentId };
                db.Grades.Add(grade);
            }

            grade.Score = entry.Score;
            grade.Observation = string.IsNullOrWhiteSpace(entry.Observation) ? null : entry.Observation.Trim();
            grade.IsPublished = request.Publish;
            grade.UpdatedAtUtc = DateTime.UtcNow;

            if (oldScore != grade.Score || oldObservation != grade.Observation || oldPublished != grade.IsPublished)
            {
                db.GradeHistory.Add(new GradeHistory
                {
                    Grade = grade,
                    PreviousScore = oldScore,
                    NewScore = grade.Score,
                    PreviousObservation = oldObservation,
                    NewObservation = grade.Observation,
                    PreviousPublished = oldPublished,
                    NewPublished = grade.IsPublished,
                    ChangedBy = user.DisplayName,
                    ChangedAtUtc = grade.UpdatedAtUtc
                });
            }
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("grades/{gradeId:int}/history")]
    public async Task<IActionResult> GradeHistory(int gradeId)
    {
        var user = await CurrentTeacher();
        if (user is null) return Forbid();
        var allowed = await db.Grades.AnyAsync(x => x.Id == gradeId && x.Evaluation.TeachingAssignment.TeacherId == user.PersonId);
        if (!allowed) return Forbid();
        return Ok(await db.GradeHistory.AsNoTracking().Where(x => x.GradeId == gradeId).OrderByDescending(x => x.ChangedAtUtc)
            .Select(x => new { x.PreviousScore, x.NewScore, x.PreviousObservation, x.NewObservation, x.PreviousPublished, x.NewPublished, x.ChangedBy, x.ChangedAtUtc }).ToListAsync());
    }

    private async Task<ApplicationUser?> CurrentTeacher()
    {
        var user = await userManager.GetUserAsync(User);
        return user is { IsActive: true, Profile: Profiles.Teacher, PersonId: not null } ? user : null;
    }
}

public sealed record EvaluationRequest(int AssignmentId, int TermId, string Title, DateOnly Date);
public sealed record GradeEntryRequest(int EnrollmentId, int Score, string? Observation);
public sealed record GradeBatchRequest(int EvaluationId, bool Publish, IReadOnlyList<GradeEntryRequest> Grades);
public sealed record GradebookGrade(int Id, int EnrollmentId, int Score, string? Observation, bool IsPublished, DateTime UpdatedAtUtc);
