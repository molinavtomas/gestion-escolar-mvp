using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Estructura académica: cursos, materias por año, asignaciones de profesores y preceptores,
/// publicación del boletín y el tablero de avance de carga. Solo Dirección y Secretaría.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Staff)]
[Route("api/academic")]
public sealed class AcademicController(AppDbContext db) : ControllerBase
{
    // ================================================================ Lectura general

    /// <summary>
    /// Todo lo necesario para la pantalla "Cursos y materias" en una sola llamada.
    /// </summary>
    [HttpGet("structure")]
    public async Task<IActionResult> Structure()
    {
        var year = await ActiveYearAsync();

        var courses = (await db.Courses.AsNoTracking()
                .Where(x => x.SchoolYearId == year.Id && x.IsActive)
                .OrderBy(x => x.Level).ThenBy(x => x.Division)
                .Select(x => new
                {
                    x.Id, x.Level, x.Division,
                    Students = db.Enrollments.Count(e => e.CourseId == x.Id && e.IsActive)
                })
                .ToListAsync())
            .Select(x => new { x.Id, x.Level, x.Division, Label = Course.LabelOf(x.Level, x.Division), x.Students });

        var subjects = await db.Subjects.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Level).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Level, x.DisplayOrder })
            .ToListAsync();

        var teachingAssignments = (await db.TeachingAssignments.AsNoTracking()
                .Select(x => new
                {
                    x.Id, x.CourseId, x.Course.Level, x.Course.Division,
                    x.SubjectId, Subject = x.Subject.Name,
                    x.TeacherId, Teacher = x.Teacher.FullName
                })
                .ToListAsync())
            .Select(x => new { x.Id, x.CourseId, Course = Course.LabelOf(x.Level, x.Division), x.SubjectId, x.Subject, x.TeacherId, x.Teacher });

        var preceptorAssignments = (await db.PreceptorAssignments.AsNoTracking()
                .Select(x => new { x.Id, x.CourseId, x.Course.Level, x.Course.Division, x.PreceptorId, Preceptor = x.Preceptor.FullName })
                .ToListAsync())
            .Select(x => new { x.Id, x.CourseId, Course = Course.LabelOf(x.Level, x.Division), x.PreceptorId, x.Preceptor });

        return Ok(new
        {
            year = year.Year,
            courses,
            subjects,
            teachingAssignments,
            preceptorAssignments,
            teachers = await PeopleOfType(PersonTypes.Profesor),
            preceptors = await PeopleOfType(PersonTypes.Preceptor)
        });
    }

    // ================================================================ Cursos

    [HttpPost("courses")]
    public async Task<IActionResult> CreateCourse(CourseRequest request)
    {
        if (request.Level is < 1 or > 6) return BadRequest(new { message = "El año debe estar entre 1 y 6." });
        var division = (request.Division ?? string.Empty).Trim().ToUpperInvariant();
        if (division.Length == 0) return BadRequest(new { message = "La división es obligatoria (ej. A)." });

        var year = await ActiveYearAsync();
        if (await db.Courses.AnyAsync(x => x.SchoolYearId == year.Id && x.Level == request.Level && x.Division == division))
            return BadRequest(new { message = "Ese curso ya existe." });

        db.Courses.Add(new Course { SchoolYearId = year.Id, Level = request.Level, Division = division });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Materias

    /// <summary>Agrega una materia al final del boletín de ese año.</summary>
    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject(SubjectRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim().ToUpperInvariant(); // el boletín las muestra en mayúsculas
        if (name.Length == 0) return BadRequest(new { message = "El nombre de la materia es obligatorio." });
        if (request.Level is < 1 or > 6) return BadRequest(new { message = "El año debe estar entre 1 y 6." });

        var existing = await db.Subjects.SingleOrDefaultAsync(x => x.Level == request.Level && x.Name == name);
        if (existing is { IsActive: true }) return BadRequest(new { message = "Esa materia ya existe en ese año." });
        if (existing is not null)
        {
            existing.IsActive = true; // se había quitado: la reactivamos con sus notas intactas
        }
        else
        {
            var lastOrder = await db.Subjects.Where(x => x.Level == request.Level).MaxAsync(x => (int?)x.DisplayOrder) ?? 0;
            db.Subjects.Add(new Subject { Name = name, Level = request.Level, DisplayOrder = lastOrder + 1 });
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Quita la materia del boletín. No se borra de la base (puede tener notas cargadas):
    /// solo se desactiva, y se puede volver a agregar con el mismo nombre.
    /// </summary>
    [HttpDelete("subjects/{id:int}")]
    public async Task<IActionResult> RemoveSubject(int id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        subject.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sube o baja una materia una posición en el orden del boletín.</summary>
    [HttpPost("subjects/{id:int}/move")]
    public async Task<IActionResult> MoveSubject(int id, MoveRequest request)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();

        var siblings = await db.Subjects
            .Where(x => x.Level == subject.Level && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync();
        var index = siblings.IndexOf(subject);
        var target = index + (request.Up ? -1 : 1);
        if (target < 0 || target >= siblings.Count) return NoContent(); // ya está en el borde

        // Intercambiamos posiciones y renumeramos todo (1, 2, 3...) para que no haya empates.
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++) siblings[i].DisplayOrder = i + 1;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Asignaciones

    /// <summary>Asigna un profesor a una materia de un curso. La materia debe ser del mismo año que el curso.</summary>
    [HttpPost("teaching-assignments")]
    public async Task<IActionResult> AssignTeacher(TeachingAssignmentRequest request)
    {
        var course = await db.Courses.FindAsync(request.CourseId);
        var subject = await db.Subjects.FindAsync(request.SubjectId);
        var teacherOk = await db.People.AnyAsync(x => x.Id == request.TeacherId && x.Type == PersonTypes.Profesor);
        if (course is null || subject is null || !teacherOk) return BadRequest(new { message = "Datos inválidos." });
        if (course.Level != subject.Level) return BadRequest(new { message = "La materia no corresponde al año del curso." });
        if (await db.TeachingAssignments.AnyAsync(x => x.CourseId == request.CourseId && x.SubjectId == request.SubjectId && x.TeacherId == request.TeacherId))
            return BadRequest(new { message = "Esa asignación ya existe." });

        db.TeachingAssignments.Add(new TeachingAssignment
        {
            CourseId = request.CourseId, SubjectId = request.SubjectId, TeacherId = request.TeacherId
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Quita la asignación. Las notas cargadas se conservan (pertenecen al alumno, no al profesor).</summary>
    [HttpDelete("teaching-assignments/{id:int}")]
    public async Task<IActionResult> UnassignTeacher(int id)
    {
        var assignment = await db.TeachingAssignments.FindAsync(id);
        if (assignment is null) return NotFound();
        db.TeachingAssignments.Remove(assignment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("preceptor-assignments")]
    public async Task<IActionResult> AssignPreceptor(PreceptorAssignmentRequest request)
    {
        var courseOk = await db.Courses.AnyAsync(x => x.Id == request.CourseId);
        var preceptorOk = await db.People.AnyAsync(x => x.Id == request.PreceptorId && x.Type == PersonTypes.Preceptor);
        if (!courseOk || !preceptorOk) return BadRequest(new { message = "Datos inválidos." });
        if (await db.PreceptorAssignments.AnyAsync(x => x.CourseId == request.CourseId && x.PreceptorId == request.PreceptorId))
            return BadRequest(new { message = "Esa asignación ya existe." });

        db.PreceptorAssignments.Add(new PreceptorAssignment { CourseId = request.CourseId, PreceptorId = request.PreceptorId });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("preceptor-assignments/{id:int}")]
    public async Task<IActionResult> UnassignPreceptor(int id)
    {
        var assignment = await db.PreceptorAssignments.FindAsync(id);
        if (assignment is null) return NotFound();
        db.PreceptorAssignments.Remove(assignment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Publicación y avance

    /// <summary>
    /// Tablero de Dirección: etapa publicada y, por cada materia de cada curso,
    /// cuántos alumnos tienen nota cargada en cada columna clave. Así se ve rápido
    /// qué profesor todavía no cargó.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var year = await ActiveYearAsync();

        // Todas las combinaciones curso × materia del año (aunque no tengan profesor asignado).
        var courses = await db.Courses.AsNoTracking()
            .Where(x => x.SchoolYearId == year.Id && x.IsActive)
            .OrderBy(x => x.Level).ThenBy(x => x.Division)
            .ToListAsync();
        var subjects = await db.Subjects.AsNoTracking().Where(x => x.IsActive).ToListAsync();

        // Cantidad de alumnos activos por curso.
        var studentsByCourse = await db.Enrollments.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);

        // Notas cargadas agrupadas por curso y materia (solo inscripciones activas).
        var loaded = await db.ReportCardEntries.AsNoTracking()
            .Where(x => x.Enrollment.IsActive)
            .GroupBy(x => new { x.Enrollment.CourseId, x.SubjectId })
            .Select(g => new
            {
                g.Key.CourseId,
                g.Key.SubjectId,
                Valuation1 = g.Count(x => x.Valuation1 != null),
                Grade1 = g.Count(x => x.Grade1 != null),
                Valuation2 = g.Count(x => x.Valuation2 != null),
                Grade2 = g.Count(x => x.Grade2 != null),
                Final = g.Count(x => x.FinalGrade != null)
            })
            .ToListAsync();

        var teachers = await db.TeachingAssignments.AsNoTracking()
            .Select(x => new { x.CourseId, x.SubjectId, x.Teacher.FullName })
            .ToListAsync();

        var rows = courses.SelectMany(course => subjects
            .Where(s => s.Level == course.Level)
            .OrderBy(s => s.DisplayOrder)
            .Select(subject =>
            {
                var l = loaded.FirstOrDefault(x => x.CourseId == course.Id && x.SubjectId == subject.Id);
                return new
                {
                    CourseId = course.Id,
                    Course = Course.LabelOf(course.Level, course.Division),
                    SubjectId = subject.Id,
                    Subject = subject.Name,
                    Teacher = string.Join(", ", teachers.Where(t => t.CourseId == course.Id && t.SubjectId == subject.Id).Select(t => t.FullName)),
                    Students = studentsByCourse.GetValueOrDefault(course.Id),
                    Valuation1 = l?.Valuation1 ?? 0,
                    Grade1 = l?.Grade1 ?? 0,
                    Valuation2 = l?.Valuation2 ?? 0,
                    Grade2 = l?.Grade2 ?? 0,
                    Final = l?.Final ?? 0
                };
            }));

        return Ok(new
        {
            year = year.Year,
            publishedStage = year.PublishedStage,
            stages = PublicationStages.Labels,
            totals = new
            {
                students = await db.People.CountAsync(x => x.IsActive && x.Type == PersonTypes.Alumno),
                teachers = await db.People.CountAsync(x => x.IsActive && x.Type == PersonTypes.Profesor),
                courses = courses.Count
            },
            rows
        });
    }

    /// <summary>Define hasta qué etapa del boletín pueden ver las familias.</summary>
    [HttpPut("publication")]
    public async Task<IActionResult> SetPublication(PublicationRequest request)
    {
        if (request.Stage < PublicationStages.Nada || request.Stage > PublicationStages.Final)
            return BadRequest(new { message = "Etapa inválida." });
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        year.PublishedStage = request.Stage;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Auxiliares

    private Task<SchoolYear> ActiveYearAsync() => db.SchoolYears.AsNoTracking().SingleAsync(x => x.IsActive);

    private Task<List<PersonOption>> PeopleOfType(string type) => db.People.AsNoTracking()
        .Where(x => x.IsActive && x.Type == type)
        .OrderBy(x => x.FullName)
        .Select(x => new PersonOption(x.Id, x.FullName))
        .ToListAsync();
}

public sealed record PersonOption(int Id, string Name);
public sealed record CourseRequest(int Level, string? Division);
public sealed record SubjectRequest(string? Name, int Level);
public sealed record MoveRequest(bool Up);
public sealed record TeachingAssignmentRequest(int CourseId, int SubjectId, int TeacherId);
public sealed record PreceptorAssignmentRequest(int CourseId, int PreceptorId);
public sealed record PublicationRequest(int Stage);
