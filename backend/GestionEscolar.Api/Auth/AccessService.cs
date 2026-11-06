using System.Security.Claims;
using GestionEscolar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Auth;

/// <summary>
/// Centraliza las reglas de "quién puede ver/editar qué". Tenerlas en un solo lugar evita
/// que un controlador se olvide de un chequeo y que las reglas queden desparramadas.
///
/// Reglas:
///  - Director y Secretaría: todo.
///  - Profesor: solo las materias/cursos que tiene asignados.
///  - Preceptor: solo los cursos que tiene asignados.
///  - Familia: solo el alumno de su propia cuenta.
/// </summary>
public sealed class AccessService(AppDbContext db)
{
    /// <summary>¿Puede cargar notas de esta materia en este curso?</summary>
    public async Task<bool> CanEditGradesAsync(ClaimsPrincipal user, int courseId, int subjectId)
    {
        if (user.IsStaff()) return true;
        if (!user.IsInRole(Roles.Profesor)) return false;
        var personId = user.PersonId();
        return await db.TeachingAssignments.AnyAsync(x =>
            x.TeacherId == personId && x.CourseId == courseId && x.SubjectId == subjectId);
    }

    /// <summary>¿Puede ver la lista y la asistencia de este curso?</summary>
    public async Task<bool> CanSeeCourseAsync(ClaimsPrincipal user, int courseId)
    {
        if (user.IsStaff()) return true;
        var personId = user.PersonId();
        if (user.IsInRole(Roles.Preceptor))
            return await db.PreceptorAssignments.AnyAsync(x => x.PreceptorId == personId && x.CourseId == courseId);
        if (user.IsInRole(Roles.Profesor))
            return await db.TeachingAssignments.AnyAsync(x => x.TeacherId == personId && x.CourseId == courseId);
        return false;
    }

    /// <summary>
    /// ¿Puede ver el boletín completo de este alumno?
    /// Profesores no: ellos ven solo su materia en la grilla de carga.
    /// </summary>
    public async Task<bool> CanSeeReportCardAsync(ClaimsPrincipal user, int studentId)
    {
        if (user.IsStaff()) return true;
        if (user.IsInRole(Roles.Familia)) return user.PersonId() == studentId;
        if (user.IsInRole(Roles.Preceptor))
        {
            var personId = user.PersonId();
            // El alumno tiene que estar inscripto en algún curso del preceptor.
            return await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.IsActive &&
                db.PreceptorAssignments.Any(p => p.PreceptorId == personId && p.CourseId == e.CourseId));
        }
        return false;
    }
}
