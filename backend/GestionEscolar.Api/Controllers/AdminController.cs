using System.IO.Compression;
using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Funciones exclusivas del Administrador (dueñas del colegio):
/// configuración institucional, años lectivos, promoción de alumnos, exportación y estadísticas.
/// (La auditoría está en AuditController.)
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Administrador)]
[Route("api/admin")]
public sealed class AdminController(AppDbContext db, PeopleService people, AuditService audit) : ControllerBase
{
    // ================================================================ Configuración (ADM-09)

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var s = await SettingsAsync();
        return Ok(new { s.SchoolName, s.AbsenceAlertThreshold, HasCustomLogo = s.LogoData != null });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(SettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SchoolName) || request.SchoolName.Length > 150)
            return BadRequest(new { message = "El nombre es obligatorio (máximo 150 caracteres)." });
        if (request.AbsenceAlertThreshold is < 1 or > 200)
            return BadRequest(new { message = "El límite de faltas debe estar entre 1 y 200." });

        var s = await SettingsAsync();
        s.SchoolName = request.SchoolName.Trim();
        s.AbsenceAlertThreshold = request.AbsenceAlertThreshold;
        audit.Change("Configuración actualizada", $"Nombre: {s.SchoolName}; límite de faltas: {s.AbsenceAlertThreshold}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sube un logo nuevo (PNG o JPG de hasta 1 MB). Se guarda en la base.</summary>
    [HttpPost("settings/logo")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Elegí un archivo." });
        if (file.Length > 1_000_000) return BadRequest(new { message = "El logo no puede superar 1 MB." });
        if (file.ContentType is not ("image/png" or "image/jpeg"))
            return BadRequest(new { message = "El logo tiene que ser PNG o JPG." });

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        var s = await SettingsAsync();
        s.LogoData = memory.ToArray();
        s.LogoContentType = file.ContentType;
        audit.Change("Logo actualizado");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Vuelve al escudo por defecto.</summary>
    [HttpDelete("settings/logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        var s = await SettingsAsync();
        s.LogoData = null;
        s.LogoContentType = null;
        audit.Change("Logo restablecido al original");
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Años lectivos (ADM-10)

    [HttpGet("years")]
    public async Task<IActionResult> Years() => Ok(await db.SchoolYears.AsNoTracking()
        .OrderByDescending(x => x.Year)
        .Select(x => new
        {
            x.Id, x.Year, x.IsActive,
            Courses = db.Courses.Count(c => c.SchoolYearId == x.Id),
            Students = db.Enrollments.Count(e => e.IsActive && e.Course.SchoolYearId == x.Id)
        })
        .ToListAsync());

    /// <summary>
    /// Abre un nuevo año lectivo: el actual pasa a "solo lectura" y se crea el nuevo, opcionalmente
    /// copiando los cursos (vacíos) y las asignaciones de profesores y preceptores.
    /// Los alumnos se pasan después, desde "Promoción".
    /// </summary>
    [HttpPost("years")]
    public async Task<IActionResult> OpenYear(NewYearRequest request)
    {
        var current = await db.SchoolYears.SingleAsync(x => x.IsActive);
        if (request.Year <= current.Year) return BadRequest(new { message = $"El año nuevo tiene que ser posterior a {current.Year}." });
        if (await db.SchoolYears.AnyAsync(x => x.Year == request.Year)) return BadRequest(new { message = "Ese año ya existe." });

        current.IsActive = false;
        var next = new SchoolYear { Year = request.Year, IsActive = true };
        db.SchoolYears.Add(next);
        await db.SaveChangesAsync();

        if (request.CopyCourses)
        {
            var oldCourses = await db.Courses.Where(x => x.SchoolYearId == current.Id && x.IsActive).ToListAsync();
            var map = new Dictionary<int, Course>(); // curso viejo → curso nuevo
            foreach (var old in oldCourses)
            {
                var copy = new Course { SchoolYearId = next.Id, Nivel = old.Nivel, Level = old.Level, Division = old.Division };
                db.Courses.Add(copy);
                map[old.Id] = copy;
            }
            await db.SaveChangesAsync();

            if (request.CopyAssignments)
            {
                var teaching = await db.TeachingAssignments.Where(x => map.Keys.Contains(x.CourseId)).ToListAsync();
                db.TeachingAssignments.AddRange(teaching.Select(t => new TeachingAssignment
                    { CourseId = map[t.CourseId].Id, SubjectId = t.SubjectId, TeacherId = t.TeacherId }));
                var preceptors = await db.PreceptorAssignments.Where(x => map.Keys.Contains(x.CourseId)).ToListAsync();
                db.PreceptorAssignments.AddRange(preceptors.Select(p => new PreceptorAssignment
                    { CourseId = map[p.CourseId].Id, PreceptorId = p.PreceptorId }));
            }
        }

        audit.Change("Nuevo año lectivo", $"{current.Year} → {request.Year}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ================================================================ Promoción (ADM-11)

    /// <summary>
    /// Alumnos de un curso de un año anterior con su situación: materias aprobadas (final ≥ 7),
    /// pendientes, y si ya fueron pasados a un curso del año activo.
    /// </summary>
    [HttpGet("promotion")]
    public async Task<IActionResult> PromotionCandidates([FromQuery] int fromCourseId)
    {
        var from = await db.Courses.AsNoTracking().Include(x => x.SchoolYear).SingleOrDefaultAsync(x => x.Id == fromCourseId);
        if (from is null) return NotFound();
        var subjectCount = await db.Subjects.CountAsync(x => x.Nivel == from.Nivel && x.Level == from.Level && x.IsActive);

        var rows = await db.Enrollments.AsNoTracking()
            .Where(x => x.CourseId == fromCourseId && x.IsActive)
            .OrderBy(x => x.Student.FullName)
            .Select(x => new
            {
                x.StudentId,
                x.Student.FullName,
                x.Student.Dni,
                Approved = db.ReportCardEntries.Count(r => r.EnrollmentId == x.Id && r.FinalGrade >= 7),
                CurrentCourse = db.Enrollments
                    .Where(e => e.StudentId == x.StudentId && e.IsActive && e.Course.SchoolYear.IsActive)
                    .Select(e => new { e.Course.Nivel, e.Course.Level, e.Course.Division }).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new
        {
            from = new { from.Id, Label = Course.LabelOf(from.Nivel, from.Level, from.Division), from.SchoolYear.Year, from.SchoolYear.IsActive },
            subjectCount,
            students = rows.Select(x => new
            {
                x.StudentId, x.FullName, x.Dni, x.Approved,
                Pending = Math.Max(0, subjectCount - x.Approved),
                CurrentCourse = x.CurrentCourse is null ? null : Course.LabelOf(x.CurrentCourse.Nivel, x.CurrentCourse.Level, x.CurrentCourse.Division)
            })
        });
    }

    /// <summary>Inscribe a los alumnos elegidos en un curso del año activo. Sus boletines anteriores se conservan.</summary>
    [HttpPost("promotion")]
    public async Task<IActionResult> Promote(PromotionRequest request)
    {
        var to = await db.Courses.Include(x => x.SchoolYear).SingleOrDefaultAsync(x => x.Id == request.ToCourseId);
        if (to is null || !to.SchoolYear.IsActive) return BadRequest(new { message = "El curso destino tiene que ser del año lectivo activo." });
        if (request.StudentIds.Count == 0) return BadRequest(new { message = "Elegí al menos un alumno." });

        var valid = await db.People.Where(x => request.StudentIds.Contains(x.Id) && x.Type == PersonTypes.Alumno && x.IsActive)
            .Select(x => x.Id).ToListAsync();
        foreach (var id in valid) await people.EnrollAsync(id, to.Id);

        audit.Change("Promoción de alumnos", $"{valid.Count} alumno(s) → {Course.LabelOf(to.Nivel, to.Level, to.Division)} ({to.SchoolYear.Year})");
        await db.SaveChangesAsync();
        return Ok(new { promoted = valid.Count });
    }

    // ================================================================ Exportación (ADM-13)

    /// <summary>
    /// Descarga un ZIP con todos los datos en CSV (se abren con Excel): personas, cursos,
    /// materias, inscripciones, notas y asistencia. Sirve como respaldo propio del colegio.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                var entry = zip.CreateEntry(name);
                using var s = entry.Open();
                s.Write(content);
            }

            var persons = await db.People.AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
            Add("personas.csv", CsvExport.Build(["Nombre", "DNI", "Tipo", "Email", "Teléfono", "Activo", "Grupo familiar"],
                persons.Select(p => new object?[] { p.FullName, p.Dni, p.Type, p.Email, p.Phone, p.IsActive, p.FamilyGroupId })));

            var courses = await db.Courses.AsNoTracking().Include(x => x.SchoolYear).ToListAsync();
            Add("cursos.csv", CsvExport.Build(["Año lectivo", "Nivel", "Año", "División"],
                courses.Select(c => new object?[] { c.SchoolYear.Year, c.Nivel, c.Level, c.Division })));

            var subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Nivel).ThenBy(x => x.Level).ThenBy(x => x.DisplayOrder).ToListAsync();
            Add("materias.csv", CsvExport.Build(["Nivel", "Año", "Orden", "Materia", "Activa"],
                subjects.Select(s => new object?[] { s.Nivel, s.Level, s.DisplayOrder, s.Name, s.IsActive })));

            var enrollments = await db.Enrollments.AsNoTracking()
                .Select(e => new { e.Student.FullName, e.Student.Dni, e.Course.SchoolYear.Year, e.Course.Nivel, e.Course.Level, e.Course.Division, e.IsActive, e.ReportCardComment })
                .ToListAsync();
            Add("inscripciones.csv", CsvExport.Build(["Alumno", "DNI", "Año lectivo", "Curso", "Activa", "Observación general"],
                enrollments.Select(e => new object?[] { e.FullName, e.Dni, e.Year, Course.LabelOf(e.Nivel, e.Level, e.Division), e.IsActive, e.ReportCardComment })));

            var grades = await db.ReportCardEntries.AsNoTracking()
                .Select(r => new
                {
                    r.Enrollment.Student.FullName, r.Enrollment.Student.Dni, r.Enrollment.Course.SchoolYear.Year,
                    r.Enrollment.Course.Nivel, r.Enrollment.Course.Level, r.Enrollment.Course.Division, Subject = r.Subject.Name,
                    r.Valuation1, r.Grade1, r.Intensification1, r.Valuation2, r.Grade2, r.Intensification2,
                    r.December, r.February, r.FinalGrade, r.Observation
                })
                .ToListAsync();
            Add("notas.csv", CsvExport.Build(
                ["Alumno", "DNI", "Año lectivo", "Curso", "Materia", "1° valoración", "1° cuatr.", "Intens. 1°", "2° valoración", "2° cuatr.", "Intens. 2°", "Diciembre", "Febrero", "Final", "Observación"],
                grades.Select(g => new object?[] { g.FullName, g.Dni, g.Year, Course.LabelOf(g.Nivel, g.Level, g.Division), g.Subject,
                    g.Valuation1, g.Grade1, g.Intensification1, g.Valuation2, g.Grade2, g.Intensification2, g.December, g.February, g.FinalGrade, g.Observation })));

            var attendance = await db.Attendances.AsNoTracking()
                .Select(a => new { a.Enrollment.Student.FullName, a.Enrollment.Student.Dni, a.Date, a.Status, a.Observation })
                .ToListAsync();
            Add("asistencia.csv", CsvExport.Build(["Alumno", "DNI", "Fecha", "Estado", "Observación"],
                attendance.Select(a => new object?[] { a.FullName, a.Dni, a.Date, a.Status, a.Observation })));
        }

        audit.Change("Exportación completa de datos");
        await db.SaveChangesAsync();
        return File(zipStream.ToArray(), "application/zip", $"colegio-datos-{DateTime.UtcNow:yyyyMMdd}.zip");
    }

    // ================================================================ Estadísticas (ADM-17)

    /// <summary>
    /// Promedios por curso y por materia del año activo, y porcentaje de notas menores a 7
    /// (sobre las notas cargadas del 1° y 2° cuatrimestre).
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var entries = await db.ReportCardEntries.AsNoTracking()
            .Where(x => x.Enrollment.IsActive && x.Enrollment.Course.SchoolYear.IsActive)
            .Select(x => new { x.Enrollment.Course.Nivel, x.Enrollment.Course.Level, x.Enrollment.Course.Division, Subject = x.Subject.Name, x.Grade1, x.Grade2, x.FinalGrade })
            .ToListAsync();

        // Calcula promedio y % de desaprobados de un grupo de filas (en memoria: son pocas por colegio).
        object Summarize(IEnumerable<int?> grades)
        {
            var list = grades.Where(g => g.HasValue).Select(g => g!.Value).ToList();
            return new
            {
                Count = list.Count,
                Average = list.Count == 0 ? (double?)null : Math.Round(list.Average(), 2),
                FailedPercent = list.Count == 0 ? (double?)null : Math.Round(100.0 * list.Count(g => g < 7) / list.Count, 1)
            };
        }

        var byCourse = entries
            .GroupBy(x => new { x.Nivel, x.Level, x.Division })
            .OrderBy(g => AcademicController.NivelOrder(g.Key.Nivel)).ThenBy(g => g.Key.Level).ThenBy(g => g.Key.Division)
            .Select(g => new
            {
                Course = Course.LabelOf(g.Key.Nivel, g.Key.Level, g.Key.Division),
                Term1 = Summarize(g.Select(x => x.Grade1)),
                Term2 = Summarize(g.Select(x => x.Grade2)),
                Final = Summarize(g.Select(x => x.FinalGrade))
            });

        var bySubject = entries
            .GroupBy(x => x.Subject)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Subject = g.Key,
                Term1 = Summarize(g.Select(x => x.Grade1)),
                Term2 = Summarize(g.Select(x => x.Grade2)),
                Final = Summarize(g.Select(x => x.FinalGrade))
            });

        return Ok(new { byCourse, bySubject });
    }

    private async Task<SchoolSettings> SettingsAsync() =>
        await db.SchoolSettings.FirstOrDefaultAsync() ?? db.SchoolSettings.Add(new SchoolSettings()).Entity;
}

public sealed record SettingsRequest(string SchoolName, int AbsenceAlertThreshold);
public sealed record NewYearRequest(int Year, bool CopyCourses, bool CopyAssignments);
public sealed record PromotionRequest(int ToCourseId, IReadOnlyList<int> StudentIds);
