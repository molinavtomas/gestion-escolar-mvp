using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Boletines. La familia ve el de su hijo (solo lo publicado); Dirección, Secretaría y
/// preceptores ven los de sus alumnos completos.
/// </summary>
[ApiController]
[Authorize]
[Route("api/report-cards")]
public sealed class ReportCardsController(AppDbContext db, AccessService access, ReportCardService reportCards) : ControllerBase
{
    /// <summary>Boletín del alumno de la cuenta familiar logueada.</summary>
    [Authorize(Roles = Roles.Familia)]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var card = await reportCards.BuildAsync(User.PersonId(), familyView: true);
        return card is null ? NotFound(new { message = "El alumno no está inscripto en ningún curso." }) : Ok(card);
    }

    /// <summary>Boletín de cualquier alumno, si el usuario tiene permiso para verlo.</summary>
    [HttpGet("{studentId:int}")]
    public async Task<IActionResult> Get(int studentId)
    {
        if (!await access.CanSeeReportCardAsync(User, studentId)) return Forbid();
        // Si una familia llega por esta ruta, igualmente se aplica el filtro de publicación.
        var card = await reportCards.BuildAsync(studentId, familyView: User.IsInRole(Roles.Familia));
        return card is null ? NotFound(new { message = "El alumno no está inscripto en ningún curso." }) : Ok(card);
    }

    /// <summary>
    /// Boletines de todo un curso (para imprimirlos juntos al final del cuatrimestre).
    /// </summary>
    [Authorize(Roles = Roles.Staff + "," + Roles.Preceptor)]
    [HttpGet("course/{courseId:int}")]
    public async Task<IActionResult> ForCourse(int courseId)
    {
        if (!await access.CanSeeCourseAsync(User, courseId)) return Forbid();
        var studentIds = await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => x.StudentId)
            .ToListAsync();

        var cards = new List<ReportCard>();
        foreach (var id in studentIds)
        {
            var card = await reportCards.BuildAsync(id, familyView: false);
            if (card is not null) cards.Add(card);
        }
        return Ok(cards);
    }
}
