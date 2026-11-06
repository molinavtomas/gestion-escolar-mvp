using GestionEscolar.Api.Auth;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Estructura académica del año activo: cursos, materias por nivel y año, asignaciones,
/// publicación del boletín, cierre de períodos y el panel de avance.
///  - Ver: Administrador, Director, Secretaría y Preceptor (lectura).
///  - Modificar: Administrador y Director.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.ViewAll)]
[Route("api/academic")]
public sealed class AcademicController(AppDbContext db, AuditService audit) : ControllerBase
{
    // ================================================================ Lectura general

    /// <summary>Todo lo necesario para la pantalla "Cursos y materias" en una sola llamada.</summary>
    [HttpGet("structure")]
    public async Task<IActionResult> Structure()
    {
        var year = await ActiveYearAsync();

        var courses = (await db.Courses.AsNoTracking()
                .Where(x => x.SchoolYearId == year.Id && x.IsActive)
                .Select(x => new
                {
                    x.Id, x.Nivel, x.Level, x.Division,
                    Students = db.Enrollments.Count(e => e.CourseId == x.Id && e.IsActive)
                })
                .ToListAsync())
            .OrderBy(x => NivelOrder(x.Nivel)).ThenBy(x => x.Level).ThenBy(x => x.Division)
            .Select(x => new { x.Id, x.Nivel, x.Level, x.Division, Label = Course.LabelOf(x.Nivel, x.Level, x.Division), x.Students });

        var subjects = await db.Subjects.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Level).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Nivel, x.Level, x.DisplayOrder })
            .ToListAsync();

        var teachingAssignments = await db.TeachingAssignments.AsNoTracking()
            .Where(x => x.Course.SchoolYearId == year.Id)
            .Select(x => new { x.Id, x.CourseId, x.SubjectId, x.TeacherId, Teacher = x.Teacher.FullName })
            .ToListAsync();

        var preceptorAssignments = await db.PreceptorAssignments.AsNoTracking()
            .Where(x => x.Course.SchoolYearId == year.Id)
            .Select(x => new { x.Id, x.CourseId, x.PreceptorId, Preceptor = x.Preceptor.FullName })
            .ToListAsync();

        return Ok(new
        {
            year = year.Year,
            niveles = Niveles.All.Select(n => new { nivel = n, levels = Niveles.LevelsOf(n) }),
            courses,
            subjects,
            teachingAssignments,
            preceptorAssignments,
            teachers = await PeopleOfType(PersonTypes.Profesor),
            preceptors = await PeopleOfType(PersonTypes.Preceptor),
            canEdit = User.IsManagement()
        });
    }

    // ================================================================ Cursos

    [Authorize(Roles = Roles.Management)]
    [HttpPost("courses")]
    public async Task<IActionResult> CreateCourse(CourseRequest request)
    {
        if (!Niveles.All.Contains(request.Nivel)) return BadRequest(new { message = "Nivel inválido." });
        if (!Niveles.LevelsOf(request.Nivel).Contains(request.Level)) return BadRequest(new { message = "Año (o sala) inválido para ese nivel." });
        var division = (request.Division ?? string.Empty).Trim().ToUpperInvariant();
        if (division.Length == 0) return BadRequest(new { message = "La división es obligatoria (ej. A)." });

        var year = await ActiveYearAsync();
        if (await db.Courses.AnyAsync(x => x.SchoolYearId == year.Id && x.Nivel == request.Nivel && x.Level == request.Level && x.Division == division))
            return BadRequest(new { message = "Ese curso ya existe." });

        db.Courses.Add(new Course { SchoolYearId = year.Id, Nivel = request.Nivel, Level = request.Level, Division = division });
        audit.Change("Alta de curso", Course.LabelOf(request.Nivel, request.Level, division));
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Materias

    /// <summary>Agrega una materia al final del boletín de ese nivel y año.</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject(SubjectRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim().ToUpperInvariant(); // el boletín las muestra en mayúsculas
        if (name.Length == 0) return BadRequest(new { message = "El nombre de la materia es obligatorio." });
        if (!Niveles.All.Contains(request.Nivel) || !Niveles.LevelsOf(request.Nivel).Contains(request.Level))
            return BadRequest(new { message = "Nivel o año inválido." });

        var existing = await db.Subjects.SingleOrDefaultAsync(x => x.Nivel == request.Nivel && x.Level == request.Level && x.Name == name);
        if (existing is { IsActive: true }) return BadRequest(new { message = "Esa materia ya existe en ese año." });
        if (existing is not null)
        {
            existing.IsActive = true; // se había quitado: la reactivamos con sus notas intactas
        }
        else
        {
            var lastOrder = await db.Subjects.Where(x => x.Nivel == request.Nivel && x.Level == request.Level)
                .MaxAsync(x => (int?)x.DisplayOrder) ?? 0;
            db.Subjects.Add(new Subject { Name = name, Nivel = request.Nivel, Level = request.Level, DisplayOrder = lastOrder + 1 });
        }
        audit.Change("Alta de materia", $"{name} ({request.Nivel} {request.Level}°)");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Quita la materia del boletín. No se borra de la base (puede tener notas cargadas):
    /// solo se desactiva, y se puede volver a agregar con el mismo nombre.
    /// </summary>
    [Authorize(Roles = Roles.Management)]
    [HttpDelete("subjects/{id:int}")]
    public async Task<IActionResult> RemoveSubject(int id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        subject.IsActive = false;
        audit.Change("Baja de materia", $"{subject.Name} ({subject.Nivel} {subject.Level}°)");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sube o baja una materia una posición en el orden del boletín.</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPost("subjects/{id:int}/move")]
    public async Task<IActionResult> MoveSubject(int id, MoveRequest request)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();

        var siblings = await db.Subjects
            .Where(x => x.Nivel == subject.Nivel && x.Level == subject.Level && x.IsActive)
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

    /// <summary>Asigna un profesor a una materia de un curso. La materia debe ser del mismo nivel y año.</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPost("teaching-assignments")]
    public async Task<IActionResult> AssignTeacher(TeachingAssignmentRequest request)
    {
        var course = await db.Courses.FindAsync(request.CourseId);
        var subject = await db.Subjects.FindAsync(request.SubjectId);
        var teacher = await db.People.SingleOrDefaultAsync(x => x.Id == request.TeacherId && x.Type == PersonTypes.Profesor);
        if (course is null || subject is null || teacher is null) return BadRequest(new { message = "Datos inválidos." });
        if (course.Nivel != subject.Nivel || course.Level != subject.Level)
            return BadRequest(new { message = "La materia no corresponde al nivel y año del curso." });
        if (await db.TeachingAssignments.AnyAsync(x => x.CourseId == request.CourseId && x.SubjectId == request.SubjectId && x.TeacherId == request.TeacherId))
            return BadRequest(new { message = "Esa asignación ya existe." });

        db.TeachingAssignments.Add(new TeachingAssignment
        {
            CourseId = request.CourseId, SubjectId = request.SubjectId, TeacherId = request.TeacherId
        });
        audit.Change("Asignación de profesor", $"{teacher.FullName} → {subject.Name} en {Course.LabelOf(course.Nivel, course.Level, course.Division)}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Quita la asignación. Las notas cargadas se conservan (pertenecen al alumno, no al profesor).</summary>
    [Authorize(Roles = Roles.Management)]
    [HttpDelete("teaching-assignments/{id:int}")]
    public async Task<IActionResult> UnassignTeacher(int id)
    {
        var assignment = await db.TeachingAssignments.Include(x => x.Teacher).Include(x => x.Subject).SingleOrDefaultAsync(x => x.Id == id);
        if (assignment is null) return NotFound();
        db.TeachingAssignments.Remove(assignment);
        audit.Change("Quita asignación de profesor", $"{assignment.Teacher.FullName} - {assignment.Subject.Name}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Management)]
    [HttpPost("preceptor-assignments")]
    public async Task<IActionResult> AssignPreceptor(PreceptorAssignmentRequest request)
    {
        var course = await db.Courses.FindAsync(request.CourseId);
        var preceptor = await db.People.SingleOrDefaultAsync(x => x.Id == request.PreceptorId && x.Type == PersonTypes.Preceptor);
        if (course is null || preceptor is null) return BadRequest(new { message = "Datos inválidos." });
        if (await db.PreceptorAssignments.AnyAsync(x => x.CourseId == request.CourseId && x.PreceptorId == request.PreceptorId))
            return BadRequest(new { message = "Esa asignación ya existe." });

        db.PreceptorAssignments.Add(new PreceptorAssignment { CourseId = request.CourseId, PreceptorId = request.PreceptorId });
        audit.Change("Asignación de preceptor", $"{preceptor.FullName} → {Course.LabelOf(course.Nivel, course.Level, course.Division)}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Management)]
    [HttpDelete("preceptor-assignments/{id:int}")]
    public async Task<IActionResult> UnassignPreceptor(int id)
    {
        var assignment = await db.PreceptorAssignments.Include(x => x.Preceptor).SingleOrDefaultAsync(x => x.Id == id);
        if (assignment is null) return NotFound();
        db.PreceptorAssignments.Remove(assignment);
        audit.Change("Quita asignación de preceptor", assignment.Preceptor.FullName);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Panel, publicación y cierre

    /// <summary>
    /// Panel: etapa publicada, períodos cerrados, alumnos con muchas faltas y, por cada materia
    /// de cada curso, cuántos alumnos tienen nota en cada columna (para ver quién falta cargar).
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var year = await ActiveYearAsync();

        var courses = (await db.Courses.AsNoTracking()
                .Where(x => x.SchoolYearId == year.Id && x.IsActive)
                .ToListAsync())
            .OrderBy(x => NivelOrder(x.Nivel)).ThenBy(x => x.Level).ThenBy(x => x.Division)
            .ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(x => x.IsActive).ToListAsync();

        // Cantidad de alumnos activos por curso.
        var studentsByCourse = await db.Enrollments.AsNoTracking()
            .Where(x => x.IsActive && x.Course.SchoolYearId == year.Id)
            .GroupBy(x => x.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count);

        // Notas cargadas agrupadas por curso y materia (solo inscripciones activas del año).
        var loaded = await db.ReportCardEntries.AsNoTracking()
            .Where(x => x.Enrollment.IsActive && x.Enrollment.Course.SchoolYearId == year.Id)
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
            .Where(x => x.Course.SchoolYearId == year.Id)
            .Select(x => new { x.CourseId, x.SubjectId, x.Teacher.FullName })
            .ToListAsync();

        var rows = courses.SelectMany(course => subjects
            .Where(s => s.Nivel == course.Nivel && s.Level == course.Level)
            .OrderBy(s => s.DisplayOrder)
            .Select(subject =>
            {
                var l = loaded.FirstOrDefault(x => x.CourseId == course.Id && x.SubjectId == subject.Id);
                return new
                {
                    CourseId = course.Id,
                    course.Nivel,
                    Course = Course.LabelOf(course.Nivel, course.Level, course.Division),
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

        // Alerta de inasistencias: alumnos con faltas >= límite configurado.
        var threshold = await db.SchoolSettings.Select(x => x.AbsenceAlertThreshold).FirstOrDefaultAsync();
        if (threshold <= 0) threshold = 10;
        var absenceRows = await db.Attendances.AsNoTracking()
            .Where(x => x.Status == AttendanceStatuses.Ausente && x.Enrollment.IsActive && x.Enrollment.Course.SchoolYearId == year.Id)
            .GroupBy(x => new { x.Enrollment.StudentId, x.Enrollment.Student.FullName, x.Enrollment.Course.Nivel, x.Enrollment.Course.Level, x.Enrollment.Course.Division })
            .Select(g => new { g.Key.StudentId, g.Key.FullName, g.Key.Nivel, g.Key.Level, g.Key.Division, Absences = g.Count() })
            .Where(x => x.Absences >= threshold)
            .OrderByDescending(x => x.Absences)
            .Take(50)
            .ToListAsync();

        return Ok(new
        {
            year = year.Year,
            publishedStage = year.PublishedStage,
            stages = PublicationStages.Labels,
            closed = new { term1 = year.IsTerm1Closed, term2 = year.IsTerm2Closed, final = year.IsFinalClosed },
            canManage = User.IsManagement(),
            absenceThreshold = threshold,
            absenceAlerts = absenceRows.Select(x => new { x.StudentId, x.FullName, Course = Course.LabelOf(x.Nivel, x.Level, x.Division), x.Absences }),
            totals = new
            {
                students = await db.Enrollments.CountAsync(x => x.IsActive && x.Course.SchoolYearId == year.Id),
                teachers = await db.People.CountAsync(x => x.IsActive && x.Type == PersonTypes.Profesor),
                courses = courses.Count
            },
            rows
        });
    }

    /// <summary>
    /// Define hasta qué etapa del boletín pueden ver las familias. Si se publica algo nuevo,
    /// se crea un aviso para todas las familias.
    /// </summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPut("publication")]
    public async Task<IActionResult> SetPublication(PublicationRequest request)
    {
        if (request.Stage < PublicationStages.Nada || request.Stage > PublicationStages.Final)
            return BadRequest(new { message = "Etapa inválida." });
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        var previous = year.PublishedStage;
        year.PublishedStage = request.Stage;

        if (request.Stage > previous)
        {
            db.Notifications.Add(new Notification
            {
                Kind = NotificationKinds.Publicacion,
                Title = "Nuevas notas publicadas en el boletín",
                Body = $"Ya podés ver: {PublicationStages.Labels[request.Stage]}.",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = User.DisplayName()
            });
        }
        audit.Change("Publicación del boletín", $"{PublicationStages.Labels[previous]} → {PublicationStages.Labels[request.Stage]}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Cierra o reabre la carga de un período. Con el período cerrado, los profesores no
    /// pueden modificar esas columnas; Dirección sí (y queda en el historial).
    /// </summary>
    [Authorize(Roles = Roles.Management)]
    [HttpPut("closing")]
    public async Task<IActionResult> SetClosing(ClosingRequest request)
    {
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        year.IsTerm1Closed = request.Term1;
        year.IsTerm2Closed = request.Term2;
        year.IsFinalClosed = request.Final;
        audit.Change("Cierre de carga", $"1° cuatr.: {(request.Term1 ? "cerrado" : "abierto")}, 2° cuatr.: {(request.Term2 ? "cerrado" : "abierto")}, final: {(request.Final ? "cerrado" : "abierto")}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Auxiliares

    private Task<SchoolYear> ActiveYearAsync() => db.SchoolYears.AsNoTracking().SingleAsync(x => x.IsActive);

    /// <summary>Orden de los niveles para listar: Inicial, Primaria, Secundaria.</summary>
    public static int NivelOrder(string nivel) => Array.IndexOf(Niveles.All, nivel);

    private Task<List<PersonOption>> PeopleOfType(string type) => db.People.AsNoTracking()
        .Where(x => x.IsActive && x.Type == type)
        .OrderBy(x => x.FullName)
        .Select(x => new PersonOption(x.Id, x.FullName))
        .ToListAsync();
}

public sealed record PersonOption(int Id, string Name);
public sealed record CourseRequest(string Nivel, int Level, string? Division);
public sealed record SubjectRequest(string? Name, string Nivel, int Level);
public sealed record MoveRequest(bool Up);
public sealed record TeachingAssignmentRequest(int CourseId, int SubjectId, int TeacherId);
public sealed record PreceptorAssignmentRequest(int CourseId, int PreceptorId);
public sealed record PublicationRequest(int Stage);
public sealed record ClosingRequest(bool Term1, bool Term2, bool Final);
