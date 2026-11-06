using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Boletines. La familia ve los de sus hijos (solo lo publicado); Administración, Dirección,
/// Secretaría y Preceptoría ven los de cualquier alumno completos.
/// </summary>
[ApiController]
[Authorize]
[Route("api/report-cards")]
public sealed class ReportCardsController(AppDbContext db, AccessService access, ReportCardService reportCards, AuditService audit) : ControllerBase
{
    /// <summary>Boletín del alumno de la cuenta familiar logueada.</summary>
    [Authorize(Roles = Roles.Familia)]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var card = await reportCards.BuildAsync(User.PersonId(), familyView: true);
        return card is null ? NotFound(new { message = "El alumno no está inscripto en ningún curso." }) : Ok(card);
    }

    /// <summary>Boletín de un alumno (opcionalmente de otro año lectivo), si el usuario puede verlo.</summary>
    [HttpGet("{studentId:int}")]
    public async Task<IActionResult> Get(int studentId, [FromQuery] int? yearId)
    {
        if (!await access.CanSeeReportCardAsync(User, studentId)) return Forbid();
        // Si es una familia, siempre se aplica el filtro de publicación.
        var card = await reportCards.BuildAsync(studentId, familyView: User.IsInRole(Roles.Familia), yearId);
        return card is null ? NotFound(new { message = "El alumno no está inscripto en ese año." }) : Ok(card);
    }

    /// <summary>Años lectivos en los que el alumno tuvo inscripción (para elegir boletines anteriores).</summary>
    [HttpGet("{studentId:int}/years")]
    public async Task<IActionResult> Years(int studentId)
    {
        if (!await access.CanSeeReportCardAsync(User, studentId)) return Forbid();
        return Ok(await db.Enrollments.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.IsActive)
            .OrderByDescending(x => x.Course.SchoolYear.Year)
            .Select(x => new { x.Course.SchoolYear.Id, x.Course.SchoolYear.Year, x.Course.SchoolYear.IsActive })
            .Distinct()
            .ToListAsync());
    }

    /// <summary>Boletines de todo un curso (para imprimirlos juntos al final del cuatrimestre).</summary>
    [Authorize(Roles = Roles.ViewAll)]
    [HttpGet("course/{courseId:int}")]
    public async Task<IActionResult> ForCourse(int courseId)
    {
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return NotFound();
        var studentIds = await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => x.StudentId)
            .ToListAsync();

        var cards = new List<ReportCard>();
        foreach (var id in studentIds)
        {
            var card = await reportCards.BuildAsync(id, familyView: false, course.SchoolYearId);
            if (card is not null) cards.Add(card);
        }
        return Ok(cards);
    }

    /// <summary>Observaciones generales de Dirección que se imprimen en el boletín (DIR-23).</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPut("{studentId:int}/comment")]
    public async Task<IActionResult> SetComment(int studentId, CommentRequest request)
    {
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > 1000 }) return BadRequest(new { message = "Máximo 1000 caracteres." });

        var enrollment = await db.Enrollments.Include(x => x.Student)
            .Where(x => x.StudentId == studentId && x.IsActive)
            .Where(x => request.YearId == null ? x.Course.SchoolYear.IsActive : x.Course.SchoolYearId == request.YearId)
            .FirstOrDefaultAsync();
        if (enrollment is null) return NotFound(new { message = "El alumno no está inscripto en ese año." });

        enrollment.ReportCardComment = comment;
        audit.Change("Observación en boletín", enrollment.Student.FullName);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// La familia confirma que leyó el boletín publicado (reemplaza la firma en papel, FAM-17).
    /// Queda registrado por alumno, año y etapa publicada.
    /// </summary>
    [Authorize(Roles = Roles.Familia)]
    [HttpPost("{studentId:int}/ack")]
    public async Task<IActionResult> Acknowledge(int studentId)
    {
        if (!await access.CanSeeReportCardAsync(User, studentId)) return Forbid();
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        if (year.PublishedStage == PublicationStages.Nada) return BadRequest(new { message = "Todavía no hay nada publicado." });

        var exists = await db.ReportCardAcknowledgements.AnyAsync(x =>
            x.StudentId == studentId && x.SchoolYearId == year.Id && x.Stage == year.PublishedStage);
        if (!exists)
        {
            db.ReportCardAcknowledgements.Add(new ReportCardAcknowledgement
            {
                StudentId = studentId,
                SchoolYearId = year.Id,
                Stage = year.PublishedStage,
                AcknowledgedBy = User.DisplayName(),
                AtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}

public sealed record CommentRequest(string? Comment, int? YearId);
