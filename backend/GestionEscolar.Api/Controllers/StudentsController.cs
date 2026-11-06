using GestionEscolar.Api.Data;
using GestionEscolar.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

/// <summary>
/// Listado de alumnos para la pantalla de Boletines: filtros (año lectivo, nivel, año, curso,
/// búsqueda por nombre o DNI) y paginación del lado del servidor, pensado para colegios con
/// muchos cursos. También exporta el listado filtrado a CSV (Excel).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.ViewAll)]
[Route("api/students")]
public sealed class StudentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] StudentFilter filter)
    {
        var pageSize = Math.Clamp(filter.PageSize, 5, 100);
        var page = Math.Max(1, filter.Page);
        var (query, year) = await FilteredAsync(filter);

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(x => x.Student.FullName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.StudentId,
                x.Student.FullName,
                x.Student.Dni,
                x.CourseId,
                x.Course.Nivel,
                x.Course.Level,
                x.Course.Division,
                Absences = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Ausente),
                // ¿La familia confirmó la lectura de la etapa publicada?
                Acknowledged = db.ReportCardAcknowledgements.Any(a =>
                    a.StudentId == x.StudentId && a.SchoolYearId == year.Id && a.Stage == year.PublishedStage)
            })
            .ToListAsync();

        return Ok(new
        {
            total, page, pageSize,
            year = new { year.Id, year.Year, year.IsActive, year.PublishedStage },
            items = rows.Select(x => new
            {
                x.StudentId, x.FullName, x.Dni, x.CourseId, x.Nivel,
                Course = Course.LabelOf(x.Nivel, x.Level, x.Division),
                x.Absences, x.Acknowledged
            })
        });
    }

    /// <summary>Descarga el listado filtrado (sin paginar) como CSV para Excel.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] StudentFilter filter)
    {
        var (query, year) = await FilteredAsync(filter);
        var rows = await query
            .OrderBy(x => x.Course.Nivel).ThenBy(x => x.Course.Level).ThenBy(x => x.Course.Division).ThenBy(x => x.Student.FullName)
            .Select(x => new
            {
                x.Student.FullName, x.Student.Dni, x.Student.Email, x.Student.Phone,
                x.Course.Nivel, x.Course.Level, x.Course.Division,
                Absences = db.Attendances.Count(a => a.EnrollmentId == x.Id && a.Status == AttendanceStatuses.Ausente)
            })
            .ToListAsync();

        var csv = CsvExport.Build(
            ["Alumno", "DNI", "Nivel", "Curso", "Email", "Teléfono", "Inasistencias"],
            rows.Select(x => new object?[] { x.FullName, x.Dni, x.Nivel, Course.LabelOf(x.Nivel, x.Level, x.Division), x.Email, x.Phone, x.Absences }));
        return File(csv, "text/csv", $"alumnos-{year.Year}.csv");
    }

    /// <summary>
    /// Arma la consulta de inscripciones según los filtros. Trabaja sobre inscripciones (no personas)
    /// porque el curso depende del año lectivo elegido.
    /// </summary>
    private async Task<(IQueryable<Enrollment> Query, SchoolYear Year)> FilteredAsync(StudentFilter f)
    {
        var year = f.YearId is null
            ? await db.SchoolYears.AsNoTracking().SingleAsync(x => x.IsActive)
            : await db.SchoolYears.AsNoTracking().SingleAsync(x => x.Id == f.YearId);

        var query = db.Enrollments.AsNoTracking()
            .Where(x => x.IsActive && x.Course.SchoolYearId == year.Id);

        if (f.CourseId is not null) query = query.Where(x => x.CourseId == f.CourseId);
        if (!string.IsNullOrWhiteSpace(f.Nivel)) query = query.Where(x => x.Course.Nivel == f.Nivel);
        if (f.Level is not null) query = query.Where(x => x.Course.Level == f.Level);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim().ToLower();
            query = query.Where(x => x.Student.FullName.ToLower().Contains(term) || x.Student.Dni.Contains(term));
        }
        return (query, year);
    }
}

/// <summary>Filtros del listado (llegan como query string: ?nivel=Secundaria&amp;level=1&amp;page=2...).</summary>
public sealed class StudentFilter
{
    public int? YearId { get; set; }
    public string? Nivel { get; set; }
    public int? Level { get; set; }
    public int? CourseId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}
