using GestionEscolar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Services;

// =====================================================================================
// Contratos (DTOs) del boletín. Son los objetos que viajan en JSON al frontend.
// =====================================================================================

/// <summary>
/// Las 9 columnas de notas de la planilla del colegio. Se usa tanto para leer como para guardar.
/// </summary>
public sealed record GradeValues(
    string? Valuation1, int? Grade1, int? Intensification1,
    string? Valuation2, int? Grade2, int? Intensification2,
    int? December, int? February, int? FinalGrade)
{
    public static readonly GradeValues Empty = new(null, null, null, null, null, null, null, null, null);

    public static GradeValues From(ReportCardEntry e) => new(
        e.Valuation1, e.Grade1, e.Intensification1,
        e.Valuation2, e.Grade2, e.Intensification2,
        e.December, e.February, e.FinalGrade);
}

/// <summary>Una fila del boletín: una materia con sus notas.</summary>
public sealed record ReportCardRow(int SubjectId, string Subject, GradeValues Values);

/// <summary>Resumen de asistencia que se muestra debajo del boletín.</summary>
public sealed record AttendanceSummary(int Present, int Absent, int Late, int Justified);

/// <summary>Boletín completo de un alumno.</summary>
public sealed record ReportCard(
    int StudentId, string Student, string Dni, string Course, int Year,
    int PublishedStage, string PublishedStageLabel,
    bool IsFamilyView,
    IReadOnlyList<ReportCardRow> Rows,
    AttendanceSummary Attendance);

// =====================================================================================
// Lógica del boletín
// =====================================================================================

/// <summary>
/// Reglas de negocio del boletín: validar notas, guardarlas con historial
/// y armar el boletín de un alumno (ocultando lo no publicado si lo mira la familia).
/// </summary>
public sealed class ReportCardService(AppDbContext db)
{
    /// <summary>Valoraciones conceptuales de la Provincia de Buenos Aires.</summary>
    public static readonly string[] ConceptualValuations = ["TEA", "TEP", "TED"];

    // ---------------------------------------------------------------- Validación

    /// <summary>
    /// Valida y normaliza una fila de notas. Devuelve la versión limpia (ej. "tea" → "TEA")
    /// o un mensaje de error.
    /// </summary>
    public static (GradeValues? Values, string? Error) Validate(GradeValues v)
    {
        // Valoración preliminar: TEA/TEP/TED o un número del 1 al 10 (lo pidió el cliente "por si acaso").
        var (val1, err1) = NormalizeValuation(v.Valuation1);
        if (err1 is not null) return (null, err1);
        var (val2, err2) = NormalizeValuation(v.Valuation2);
        if (err2 is not null) return (null, err2);

        // Las demás columnas son números enteros del 1 al 10 (o vacías).
        int?[] numbers = [v.Grade1, v.Intensification1, v.Grade2, v.Intensification2, v.December, v.February, v.FinalGrade];
        if (numbers.Any(n => n is < 1 or > 10)) return (null, "Las calificaciones deben ser números del 1 al 10.");

        return (v with { Valuation1 = val1, Valuation2 = val2 }, null);
    }

    private static (string? Value, string? Error) NormalizeValuation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var value = raw.Trim().ToUpperInvariant();
        if (ConceptualValuations.Contains(value)) return (value, null);
        if (int.TryParse(value, out var n) && n is >= 1 and <= 10) return (n.ToString(), null);
        return (null, $"Valoración \"{raw}\" inválida. Usá TEA, TEP, TED o un número del 1 al 10.");
    }

    // ---------------------------------------------------------------- Guardado

    /// <summary>
    /// Guarda (crea o actualiza) las notas de un alumno en una materia y registra en el
    /// historial cada columna que cambió. No llama a SaveChanges: lo hace quien la usa,
    /// así una grilla completa se guarda en una sola operación.
    /// </summary>
    public async Task SaveAsync(int enrollmentId, int subjectId, GradeValues values, string changedBy)
    {
        var entry = await db.ReportCardEntries
            .SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.SubjectId == subjectId);

        if (entry is null)
        {
            // Si la fila no existe y viene vacía, no hace falta crear nada.
            if (values == GradeValues.Empty) return;
            entry = new ReportCardEntry { EnrollmentId = enrollmentId, SubjectId = subjectId };
            db.ReportCardEntries.Add(entry);
        }

        var now = DateTime.UtcNow;
        var changes = new List<ReportCardChange>();

        // Compara el valor viejo con el nuevo y, si cambió, anota el cambio en el historial.
        void Track<T>(string field, T oldValue, T newValue)
        {
            if (Equals(oldValue, newValue)) return;
            changes.Add(new ReportCardChange
            {
                ReportCardEntry = entry,
                Field = field,
                OldValue = oldValue?.ToString(),
                NewValue = newValue?.ToString(),
                ChangedBy = changedBy,
                ChangedAtUtc = now
            });
        }

        // Explícito columna por columna: es más largo, pero se entiende a primera vista.
        Track("1° valoración preliminar", entry.Valuation1, values.Valuation1);
        Track("1° cuatrimestre", entry.Grade1, values.Grade1);
        Track("Intensificación 1° cuatrimestre", entry.Intensification1, values.Intensification1);
        Track("2° valoración preliminar", entry.Valuation2, values.Valuation2);
        Track("2° cuatrimestre", entry.Grade2, values.Grade2);
        Track("Intensificación 2° cuatrimestre", entry.Intensification2, values.Intensification2);
        Track("Diciembre", entry.December, values.December);
        Track("Febrero", entry.February, values.February);
        Track("Calificación final", entry.FinalGrade, values.FinalGrade);

        if (changes.Count == 0) return;

        entry.Valuation1 = values.Valuation1;
        entry.Grade1 = values.Grade1;
        entry.Intensification1 = values.Intensification1;
        entry.Valuation2 = values.Valuation2;
        entry.Grade2 = values.Grade2;
        entry.Intensification2 = values.Intensification2;
        entry.December = values.December;
        entry.February = values.February;
        entry.FinalGrade = values.FinalGrade;
        entry.UpdatedAtUtc = now;
        entry.UpdatedBy = changedBy;
        db.ReportCardChanges.AddRange(changes);
    }

    // ---------------------------------------------------------------- Lectura

    /// <summary>
    /// Arma el boletín del alumno en su curso actual. Si <paramref name="familyView"/> es true,
    /// borra las columnas que Dirección todavía no publicó.
    /// Devuelve null si el alumno no existe o no está inscripto.
    /// </summary>
    public async Task<ReportCard?> BuildAsync(int studentId, bool familyView)
    {
        var enrollment = await db.Enrollments.AsNoTracking()
            .Include(x => x.Student)
            .Include(x => x.Course).ThenInclude(x => x.SchoolYear)
            .Where(x => x.StudentId == studentId && x.IsActive)
            .SingleOrDefaultAsync();
        if (enrollment is null) return null;

        // Materias del año del curso, en el orden del boletín.
        var subjects = await db.Subjects.AsNoTracking()
            .Where(x => x.Level == enrollment.Course.Level && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync();

        // Notas cargadas, indexadas por materia para buscarlas rápido.
        var entries = await db.ReportCardEntries.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollment.Id)
            .ToDictionaryAsync(x => x.SubjectId);

        var stage = enrollment.Course.SchoolYear.PublishedStage;
        var rows = subjects.Select(subject =>
        {
            var values = entries.TryGetValue(subject.Id, out var e) ? GradeValues.From(e) : GradeValues.Empty;
            if (familyView) values = HideUnpublished(values, stage);
            return new ReportCardRow(subject.Id, subject.Name, values);
        }).ToList();

        // Resumen de asistencia del curso actual.
        var attendance = await db.Attendances.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollment.Id)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
        int Count(string status) => attendance.GetValueOrDefault(status);

        return new ReportCard(
            enrollment.StudentId,
            enrollment.Student.FullName,
            enrollment.Student.Dni,
            Course.LabelOf(enrollment.Course.Level, enrollment.Course.Division),
            enrollment.Course.SchoolYear.Year,
            stage,
            PublicationStages.Labels[stage],
            familyView,
            rows,
            new AttendanceSummary(
                Count(AttendanceStatuses.Presente), Count(AttendanceStatuses.Ausente),
                Count(AttendanceStatuses.Tarde), Count(AttendanceStatuses.Justificado)));
    }

    /// <summary>Vacía las columnas que pertenecen a etapas todavía no publicadas.</summary>
    public static GradeValues HideUnpublished(GradeValues v, int stage) => v with
    {
        Valuation1 = stage >= PublicationStages.Valoracion1 ? v.Valuation1 : null,
        Grade1 = stage >= PublicationStages.Cuatrimestre1 ? v.Grade1 : null,
        Intensification1 = stage >= PublicationStages.Cuatrimestre1 ? v.Intensification1 : null,
        Valuation2 = stage >= PublicationStages.Valoracion2 ? v.Valuation2 : null,
        Grade2 = stage >= PublicationStages.Cuatrimestre2 ? v.Grade2 : null,
        Intensification2 = stage >= PublicationStages.Cuatrimestre2 ? v.Intensification2 : null,
        December = stage >= PublicationStages.Final ? v.December : null,
        February = stage >= PublicationStages.Final ? v.February : null,
        FinalGrade = stage >= PublicationStages.Final ? v.FinalGrade : null
    };
}
