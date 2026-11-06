using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Avisos dentro del sistema: comunicados de Dirección, publicación del boletín e inasistencias.
///  - Familias: ven los generales, los de los cursos de sus hijos y los de sus hijos.
///  - Personal: ve los generales y los de curso (no las inasistencias individuales).
///  - Crear/borrar comunicados: Administración y Dirección.
/// Enviar emails o WhatsApp requiere contratar un servicio externo: queda para más adelante.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notices")]
public sealed class NoticesController(
    AppDbContext db, AccessService access, UserManager<ApplicationUser> userManager, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var query = await VisibleAsync();
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Id, x.Kind, x.Title, x.Body, x.CreatedAtUtc, x.CreatedBy,
                Student = x.StudentId == null ? null : db.People.Where(p => p.Id == x.StudentId).Select(p => p.FullName).FirstOrDefault(),
                Course = x.CourseId == null ? null : db.Courses.Where(c => c.Id == x.CourseId)
                    .Select(c => new { c.Nivel, c.Level, c.Division }).FirstOrDefault()
            })
            .ToListAsync();

        var user = await userManager.GetUserAsync(User);
        return Ok(new
        {
            canSend = User.IsManagement(),
            seenAtUtc = user?.NotificationsSeenAtUtc,
            items = items.Select(x => new
            {
                x.Id, x.Kind, x.Title, x.Body, x.CreatedAtUtc, x.CreatedBy, x.Student,
                Course = x.Course is null ? null : Course.LabelOf(x.Course.Nivel, x.Course.Level, x.Course.Division)
            })
        });
    }

    /// <summary>Cantidad de avisos nuevos desde la última vez que abrió "Avisos" (para el globito del menú).</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var user = await userManager.GetUserAsync(User);
        var since = user?.NotificationsSeenAtUtc ?? DateTime.MinValue;
        var query = await VisibleAsync();
        return Ok(new { count = await query.CountAsync(x => x.CreatedAtUtc > since) });
    }

    /// <summary>Marca todos los avisos como vistos.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> Seen()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        user.NotificationsSeenAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        return NoContent();
    }

    /// <summary>Envía un comunicado a todo el colegio (sin curso) o a las familias de un curso (DIR-24).</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPost]
    public async Task<IActionResult> Create(NoticeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { message = "Título y mensaje son obligatorios." });
        if (request.Title.Length > 150 || request.Body.Length > 2000)
            return BadRequest(new { message = "El título admite 150 caracteres y el mensaje 2000." });
        if (request.CourseId is not null && !await db.Courses.AnyAsync(x => x.Id == request.CourseId))
            return BadRequest(new { message = "El curso no existe." });

        db.Notifications.Add(new Notification
        {
            Kind = NotificationKinds.Comunicado,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            CourseId = request.CourseId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = User.DisplayName()
        });
        audit.Change("Comunicado enviado", request.Title.Trim());
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Management)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var notice = await db.Notifications.FindAsync(id);
        if (notice is null) return NotFound();
        db.Notifications.Remove(notice);
        audit.Change("Aviso eliminado", notice.Title);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Consulta base con los avisos que el usuario actual puede ver.</summary>
    private async Task<IQueryable<Notification>> VisibleAsync()
    {
        var query = db.Notifications.AsNoTracking();
        if (!User.IsInRole(Roles.Familia))
        {
            // Personal: generales y de curso; las inasistencias individuales son para las familias.
            return query.Where(x => x.StudentId == null);
        }

        var studentIds = await access.FamilyStudentIdsAsync(User);
        var courseIds = await db.Enrollments
            .Where(x => studentIds.Contains(x.StudentId) && x.IsActive && x.Course.SchoolYear.IsActive)
            .Select(x => x.CourseId)
            .ToListAsync();

        return query.Where(x =>
            (x.StudentId == null && x.CourseId == null) ||
            (x.StudentId != null && studentIds.Contains(x.StudentId.Value)) ||
            (x.CourseId != null && courseIds.Contains(x.CourseId.Value)));
    }
}

public sealed record NoticeRequest(string? Title, string? Body, int? CourseId);
