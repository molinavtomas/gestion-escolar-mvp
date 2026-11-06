using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Carga de calificaciones en forma de grilla (alumnos × columnas del boletín) para UNA materia
/// de UN curso. La usan los profesores (solo sus materias) y Dirección/Secretaría (todas, para corregir).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Staff + "," + Roles.Profesor)]
[Route("api/grades")]
public sealed class GradesController(AppDbContext db, AccessService access, ReportCardService reportCards) : ControllerBase
{
    /// <summary>
    /// Materias que el usuario puede cargar. Profesor: las asignadas. Dirección/Secretaría: todas.
    /// </summary>
    [HttpGet("my-subjects")]
    public async Task<IActionResult> MySubjects()
    {
        List<SubjectOption> options;
        if (User.IsStaff())
        {
            // Staff: todas las combinaciones curso × materia del año del curso.
            options = await (from c in db.Courses.AsNoTracking()
                             join s in db.Subjects.AsNoTracking() on c.Level equals s.Level
                             where c.IsActive && s.IsActive && c.SchoolYear.IsActive
                             orderby c.Level, c.Division, s.DisplayOrder
                             select new SubjectOption(c.Id, c.Level, c.Division, s.Id, s.Name))
                .ToListAsync();
        }
        else
        {
            var personId = User.PersonId();
            options = await db.TeachingAssignments.AsNoTracking()
                .Where(x => x.TeacherId == personId && x.Course.IsActive && x.Subject.IsActive && x.Course.SchoolYear.IsActive)
                .OrderBy(x => x.Course.Level).ThenBy(x => x.Course.Division).ThenBy(x => x.Subject.DisplayOrder)
                .Select(x => new SubjectOption(x.CourseId, x.Course.Level, x.Course.Division, x.SubjectId, x.Subject.Name))
                .ToListAsync();
        }

        return Ok(options.Select(x => new
        {
            x.CourseId, x.SubjectId, x.Subject,
            Course = Course.LabelOf(x.Level, x.Division)
        }));
    }

    /// <summary>Grilla con todos los alumnos del curso y sus notas en la materia.</summary>
    [HttpGet("grid")]
    public async Task<IActionResult> Grid([FromQuery] int courseId, [FromQuery] int subjectId)
    {
        if (!await access.CanEditGradesAsync(User, courseId, subjectId)) return Forbid();

        // Left join: alumnos sin notas también aparecen (con la fila vacía).
        var rows = await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                EnrollmentId = x.Id,
                x.StudentId,
                Student = x.Student.FullName,
                x.Student.Dni,
                Entry = db.ReportCardEntries.FirstOrDefault(r => r.EnrollmentId == x.Id && r.SubjectId == subjectId)
            })
            .ToListAsync();

        return Ok(rows.Select(x => new
        {
            x.EnrollmentId, x.StudentId, x.Student, x.Dni,
            Values = x.Entry is null ? GradeValues.Empty : GradeValues.From(x.Entry),
            x.Entry?.UpdatedAtUtc,
            x.Entry?.UpdatedBy
        }));
    }

    /// <summary>
    /// Guarda la grilla completa. Valida TODAS las filas antes de guardar: si una tiene error,
    /// no se guarda nada y se indica qué alumno tiene el problema.
    /// </summary>
    [HttpPost("grid")]
    public async Task<IActionResult> SaveGrid(SaveGridRequest request)
    {
        if (!await access.CanEditGradesAsync(User, request.CourseId, request.SubjectId)) return Forbid();

        // Solo se aceptan inscripciones activas de ESE curso (evita que se manipule el request
        // para cargar notas a alumnos de otro curso).
        var students = await db.Enrollments
            .Where(x => x.CourseId == request.CourseId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, x => x.Student.FullName);

        // 1) Validar todo.
        var validated = new List<(int EnrollmentId, GradeValues Values)>();
        foreach (var row in request.Rows)
        {
            if (!students.TryGetValue(row.EnrollmentId, out var name)) continue;
            var (values, error) = ReportCardService.Validate(row.Values);
            if (error is not null) return BadRequest(new { message = $"{name}: {error}" });
            validated.Add((row.EnrollmentId, values!));
        }

        // 2) Guardar todo junto.
        foreach (var (enrollmentId, values) in validated)
        {
            await reportCards.SaveAsync(enrollmentId, request.SubjectId, values, User.DisplayName());
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Historial de cambios de un alumno en una materia.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int enrollmentId, [FromQuery] int subjectId)
    {
        var courseId = await db.Enrollments.Where(x => x.Id == enrollmentId).Select(x => (int?)x.CourseId).SingleOrDefaultAsync();
        if (courseId is null) return NotFound();
        if (!await access.CanEditGradesAsync(User, courseId.Value, subjectId)) return Forbid();

        var changes = await db.ReportCardChanges.AsNoTracking()
            .Where(x => x.ReportCardEntry.EnrollmentId == enrollmentId && x.ReportCardEntry.SubjectId == subjectId)
            .OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new { x.Field, x.OldValue, x.NewValue, x.ChangedBy, x.ChangedAtUtc })
            .ToListAsync();
        return Ok(changes);
    }

    private sealed record SubjectOption(int CourseId, int Level, string Division, int SubjectId, string Subject);
}

public sealed record GridRowRequest(int EnrollmentId, GradeValues Values);
public sealed record SaveGridRequest(int CourseId, int SubjectId, IReadOnlyList<GridRowRequest> Rows);
