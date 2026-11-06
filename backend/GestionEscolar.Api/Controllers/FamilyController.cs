using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Datos para la cuenta familiar: sus hijos (el de la cuenta + hermanos vinculados) y la
/// asistencia día por día. Solo rol Familia y siempre limitado a sus propios hijos.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Familia)]
[Route("api/family")]
public sealed class FamilyController(AppDbContext db, AccessService access) : ControllerBase
{
    /// <summary>Hijos que puede ver esta cuenta, con su curso actual.</summary>
    [HttpGet("children")]
    public async Task<IActionResult> Children()
    {
        var ids = await access.FamilyStudentIdsAsync(User);
        var children = await db.People.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.FullName)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                Course = db.Enrollments
                    .Where(e => e.StudentId == x.Id && e.IsActive)
                    .OrderByDescending(e => e.Course.SchoolYear.IsActive).ThenByDescending(e => e.Course.SchoolYear.Year)
                    .Select(e => new { e.Course.Nivel, e.Course.Level, e.Course.Division })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var ownId = User.PersonId();
        return Ok(children.Select(x => new
        {
            StudentId = x.Id,
            x.FullName,
            Course = x.Course is null ? null : Course.LabelOf(x.Course.Nivel, x.Course.Level, x.Course.Division),
            IsAccountOwner = x.Id == ownId
        }));
    }

    /// <summary>Asistencia día por día del hijo en el año activo (FAM-06).</summary>
    [HttpGet("attendance/{studentId:int}")]
    public async Task<IActionResult> Attendance(int studentId)
    {
        if (!(await access.FamilyStudentIdsAsync(User)).Contains(studentId)) return Forbid();
        return Ok(await db.Attendances.AsNoTracking()
            .Where(x => x.Enrollment.StudentId == studentId && x.Enrollment.IsActive && x.Enrollment.Course.SchoolYear.IsActive)
            .OrderByDescending(x => x.Date)
            .Select(x => new { x.Date, x.Status, x.Observation })
            .ToListAsync());
    }
}
