using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Carga de calificaciones en forma de grilla (alumnos × columnas del boletín) para UNA materia
/// de UN curso del año activo. La usan los profesores (solo sus materias) y Administración/Dirección
/// (todas, para corregir; también pueden modificar períodos cerrados).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Management + "," + Roles.Profesor)]
[Route("api/grades")]
public sealed class GradesController(AppDbContext db, AccessService access, ReportCardService reportCards) : ControllerBase
{
    /// <summary>
    /// Materias que el usuario puede cargar en el año activo.
    /// Profesor: las asignadas. Administración/Dirección: todas (curso × materia del mismo nivel y año).
    /// </summary>
    [HttpGet("my-subjects")]
    public async Task<IActionResult> MySubjects()
    {
        List<SubjectOption> options;
        if (User.IsManagement())
        {
            options = await (from c in db.Courses.AsNoTracking()
                             join s in db.Subjects.AsNoTracking() on new { c.Nivel, c.Level } equals new { s.Nivel, s.Level }
                             where c.IsActive && s.IsActive && c.SchoolYear.IsActive
                             select new SubjectOption(c.Id, c.Nivel, c.Level, c.Division, s.Id, s.Name, s.DisplayOrder))
                .ToListAsync();
        }
        else
        {
            var personId = User.PersonId();
            options = await db.TeachingAssignments.AsNoTracking()
                .Where(x => x.TeacherId == personId && x.Course.IsActive && x.Subject.IsActive && x.Course.SchoolYear.IsActive)
                .Select(x => new SubjectOption(x.CourseId, x.Course.Nivel, x.Course.Level, x.Course.Division, x.SubjectId, x.Subject.Name, x.Subject.DisplayOrder))
                .ToListAsync();
        }

        return Ok(options
            .OrderBy(x => AcademicController.NivelOrder(x.Nivel)).ThenBy(x => x.Level).ThenBy(x => x.Division).ThenBy(x => x.Order)
            .Select(x => new
            {
                x.CourseId, x.SubjectId, x.Subject, x.Nivel, x.Level, x.Division,
                Course = Course.LabelOf(x.Nivel, x.Level, x.Division)
            }));
    }

    /// <summary>
    /// Grilla con todos los alumnos del curso y sus notas en la materia, más qué períodos están
    /// cerrados (para que la pantalla bloquee esas columnas).
    /// </summary>
    [HttpGet("grid")]
    public async Task<IActionResult> Grid([FromQuery] int courseId, [FromQuery] int subjectId)
    {
        if (!await access.CanEditGradesAsync(User, courseId, subjectId)) return Forbid();
        var year = await db.Courses.Where(x => x.Id == courseId).Select(x => x.SchoolYear).SingleAsync();

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

        return Ok(new
        {
            closed = new { term1 = year.IsTerm1Closed, term2 = year.IsTerm2Closed, final = year.IsFinalClosed },
            // Conducción puede corregir aunque el período esté cerrado.
            canEditClosed = User.IsManagement(),
            rows = rows.Select(x => new
            {
                x.EnrollmentId, x.StudentId, x.Student, x.Dni,
                Values = x.Entry is null ? GradeValues.Empty : GradeValues.From(x.Entry),
                x.Entry?.Observation,
                x.Entry?.UpdatedAtUtc,
                x.Entry?.UpdatedBy
            })
        });
    }

    /// <summary>
    /// Guarda la grilla completa. Valida TODAS las filas antes de guardar: si una tiene error
    /// (nota inválida o período cerrado), no se guarda nada y se indica qué alumno tiene el problema.
    /// </summary>
    [HttpPost("grid")]
    public async Task<IActionResult> SaveGrid(SaveGridRequest request)
    {
        if (!await access.CanEditGradesAsync(User, request.CourseId, request.SubjectId)) return Forbid();
        var year = await db.Courses.Where(x => x.Id == request.CourseId).Select(x => x.SchoolYear).SingleAsync();
        if (!year.IsActive) return BadRequest(new { message = "Ese año lectivo ya terminó: sus notas no se pueden modificar." });

        // Solo se aceptan inscripciones activas de ESE curso (evita que se manipule el request
        // para cargar notas a alumnos de otro curso). Primero proyectamos (Select) para que EF traiga
        // el nombre del alumno en la misma consulta.
        var students = await db.Enrollments
            .Where(x => x.CourseId == request.CourseId && x.IsActive)
            .Select(x => new { x.Id, x.Student.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName);

        // 1) Validar formato de todas las filas.
        var validated = new List<(int EnrollmentId, string Name, GradeValues Values, string? Observation)>();
        foreach (var row in request.Rows)
        {
            if (!students.TryGetValue(row.EnrollmentId, out var name)) continue;
            var (values, error) = ReportCardService.Validate(row.Values);
            if (error is not null) return BadRequest(new { message = $"{name}: {error}" });
            validated.Add((row.EnrollmentId, name, values!, row.Observation));
        }

        // 2) Guardar (cada fila puede rechazar por período cerrado). Si alguna falla, no se
        //    llega a SaveChanges y no se guarda nada.
        foreach (var (enrollmentId, name, values, observation) in validated)
        {
            var error = await reportCards.SaveAsync(enrollmentId, request.SubjectId, values, observation,
                User.DisplayName(), year, canEditClosed: User.IsManagement());
            if (error is not null) return BadRequest(new { message = $"{name}: {error}" });
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

    private sealed record SubjectOption(int CourseId, string Nivel, int Level, string Division, int SubjectId, string Subject, int Order);
}

public sealed record GridRowRequest(int EnrollmentId, GradeValues Values, string? Observation);
public sealed record SaveGridRequest(int CourseId, int SubjectId, IReadOnlyList<GridRowRequest> Rows);
