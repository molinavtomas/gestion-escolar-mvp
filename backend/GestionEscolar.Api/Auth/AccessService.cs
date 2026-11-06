using System.Security.Claims;
using GestionEscolar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Auth;

/// <summary>
/// Reglas de "quién puede ver/editar QUÉ dato concreto" (un curso, una materia, un alumno).
/// Complementa a <see cref="Permissions"/>, que dice qué puede hacer cada rol en general.
/// Tenerlas en un solo lugar evita que un controlador se olvide de un chequeo.
/// </summary>
public sealed class AccessService(AppDbContext db)
{
    /// <summary>
    /// ¿Puede cargar notas de esta materia en este curso?
    /// Conducción: todas. Profesor: solo las asignadas. Resto: ninguna.
    /// </summary>
    public async Task<bool> CanEditGradesAsync(ClaimsPrincipal user, int courseId, int subjectId)
    {
        if (user.IsManagement()) return true;
        if (!user.IsInRole(Roles.Profesor)) return false;
        var personId = user.PersonId();
        return await db.TeachingAssignments.AnyAsync(x =>
            x.TeacherId == personId && x.CourseId == courseId && x.SubjectId == subjectId);
    }

    /// <summary>¿Puede ver la lista y la asistencia de este curso?</summary>
    public async Task<bool> CanSeeCourseAsync(ClaimsPrincipal user, int courseId)
    {
        if (user.CanViewAll()) return true;
        if (!user.IsInRole(Roles.Profesor)) return false;
        var personId = user.PersonId();
        return await db.TeachingAssignments.AnyAsync(x => x.TeacherId == personId && x.CourseId == courseId);
    }

    /// <summary>¿Puede tomar o corregir asistencia en este curso? Conducción: todos. Preceptor: los suyos.</summary>
    public async Task<bool> CanTakeAttendanceAsync(ClaimsPrincipal user, int courseId)
    {
        if (user.IsManagement()) return true;
        if (!user.IsInRole(Roles.Preceptor)) return false;
        var personId = user.PersonId();
        return await db.PreceptorAssignments.AnyAsync(x => x.PreceptorId == personId && x.CourseId == courseId);
    }

    /// <summary>
    /// ¿Puede ver el boletín completo de este alumno?
    /// Profesores no: ellos ven solo su materia en la grilla de carga.
    /// </summary>
    public async Task<bool> CanSeeReportCardAsync(ClaimsPrincipal user, int studentId)
    {
        if (user.CanViewAll()) return true;
        if (user.IsInRole(Roles.Familia)) return (await FamilyStudentIdsAsync(user)).Contains(studentId);
        return false;
    }

    /// <summary>
    /// Alumnos que puede ver una cuenta familiar: el alumno de la cuenta y sus hermanos
    /// (los que comparten grupo familiar). Solo alumnos activos.
    /// </summary>
    public async Task<List<int>> FamilyStudentIdsAsync(ClaimsPrincipal user)
    {
        var ownId = user.PersonId();
        var groupId = await db.People.Where(x => x.Id == ownId).Select(x => x.FamilyGroupId).SingleOrDefaultAsync();
        if (groupId is null) return [ownId];

        return await db.People
            .Where(x => x.FamilyGroupId == groupId && x.IsActive && x.Type == PersonTypes.Alumno)
            .Select(x => x.Id)
            .ToListAsync();
    }
}
