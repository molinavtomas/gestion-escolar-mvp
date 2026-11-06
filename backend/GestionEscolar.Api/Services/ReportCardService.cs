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

/// <summary>Una fila del boletín: una materia con sus notas y la observación del profesor.</summary>
public sealed record ReportCardRow(int SubjectId, string Subject, GradeValues Values, string? Observation);

/// <summary>Resumen de asistencia que se muestra debajo del boletín.</summary>
public sealed record AttendanceSummary(int Present, int Absent, int Late, int Justified);

/// <summary>Boletín completo de un alumno en un año lectivo.</summary>
public sealed record ReportCard(
    int StudentId, string Student, string Dni,
    string Nivel, string Course, int Year, int SchoolYearId, bool IsCurrentYear,
    string SchoolName,
    int PublishedStage, string PublishedStageLabel,
    bool IsFamilyView,
    IReadOnlyList<ReportCardRow> Rows,
    string? Comment,
    AttendanceSummary Attendance,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedBy);

// =====================================================================================
// Lógica del boletín
// =====================================================================================

/// <summary>
/// Reglas de negocio del boletín: validar notas, guardarlas con historial (respetando los
/// períodos cerrados) y armar el boletín de un alumno (ocultando lo no publicado a las familias).
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
    ///
    /// Si un período está cerrado (<paramref name="year"/>) y quien guarda NO es de conducción
    /// (<paramref name="canEditClosed"/> = false), cambiar esas columnas devuelve un error.
    /// </summary>
    public async Task<string?> SaveAsync(int enrollmentId, int subjectId, GradeValues values, string? observation,
        string changedBy, SchoolYear year, bool canEditClosed)
    {
        observation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        if (observation is { Length: > 300 }) return "La observación no puede superar los 300 caracteres.";

        var entry = await db.ReportCardEntries
            .SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.SubjectId == subjectId);
        var old = entry is null ? GradeValues.Empty : GradeValues.From(entry);

        // ---- Períodos cerrados ----
        if (!canEditClosed)
        {
            var term1Changed = old.Valuation1 != values.Valuation1 || old.Grade1 != values.Grade1 || old.Intensification1 != values.Intensification1;
            var term2Changed = old.Valuation2 != values.Valuation2 || old.Grade2 != values.Grade2 || old.Intensification2 != values.Intensification2;
            var finalChanged = old.December != values.December || old.February != values.February || old.FinalGrade != values.FinalGrade;
            if (term1Changed && year.IsTerm1Closed) return "El 1° cuatrimestre está cerrado. Pedí la corrección a Dirección.";
            if (term2Changed && year.IsTerm2Closed) return "El 2° cuatrimestre está cerrado. Pedí la corrección a Dirección.";
            if (finalChanged && year.IsFinalClosed) return "Las notas finales están cerradas. Pedí la corrección a Dirección.";
        }

        if (entry is null)
        {
            // Si la fila no existe y viene vacía, no hace falta crear nada.
            if (values == GradeValues.Empty && observation is null) return null;
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
        Track("Observación", entry.Observation, observation);

        if (changes.Count == 0) return null;

        entry.Valuation1 = values.Valuation1;
        entry.Grade1 = values.Grade1;
        entry.Intensification1 = values.Intensification1;
        entry.Valuation2 = values.Valuation2;
        entry.Grade2 = values.Grade2;
        entry.Intensification2 = values.Intensification2;
        entry.December = values.December;
        entry.February = values.February;
        entry.FinalGrade = values.FinalGrade;
        entry.Observation = observation;
        entry.UpdatedAtUtc = now;
        entry.UpdatedBy = changedBy;
        db.ReportCardChanges.AddRange(changes);
        return null;
    }

    // ---------------------------------------------------------------- Lectura

    /// <summary>
    /// Busca la inscripción "que corresponde mostrar" de un alumno:
    /// la del año pedido, o si no se pide año, la del año activo, o si no tiene, la más reciente.
    /// </summary>
    public Task<Enrollment?> FindEnrollmentAsync(int studentId, int? schoolYearId) =>
        db.Enrollments.AsNoTracking()
            .Include(x => x.Student)
            .Include(x => x.Course).ThenInclude(x => x.SchoolYear)
            .Where(x => x.StudentId == studentId && x.IsActive)
            .Where(x => schoolYearId == null || x.Course.SchoolYearId == schoolYearId)
            .OrderByDescending(x => x.Course.SchoolYear.IsActive) // primero el año activo
            .ThenByDescending(x => x.Course.SchoolYear.Year)      // si no, el más reciente
            .FirstOrDefaultAsync();

    /// <summary>
    /// Arma el boletín del alumno. Si <paramref name="familyView"/> es true, borra las columnas
    /// que Dirección todavía no publicó. Devuelve null si el alumno no está inscripto.
    /// </summary>
    public async Task<ReportCard?> BuildAsync(int studentId, bool familyView, int? schoolYearId = null)
    {
        var enrollment = await FindEnrollmentAsync(studentId, schoolYearId);
        if (enrollment is null) return null;
        var course = enrollment.Course;

        // Materias del año del curso (mismo nivel), en el orden del boletín.
        var subjects = await db.Subjects.AsNoTracking()
            .Where(x => x.Nivel == course.Nivel && x.Level == course.Level && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync();

        // Notas cargadas, indexadas por materia para buscarlas rápido.
        var entries = await db.ReportCardEntries.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollment.Id)
            .ToDictionaryAsync(x => x.SubjectId);

        // En años anteriores (ya cerrados) se muestra todo: el ciclo terminó.
        var stage = course.SchoolYear.IsActive ? course.SchoolYear.PublishedStage : PublicationStages.Final;
        var rows = subjects.Select(subject =>
        {
            var hasEntry = entries.TryGetValue(subject.Id, out var e);
            var values = hasEntry ? GradeValues.From(e!) : GradeValues.Empty;
            if (familyView) values = HideUnpublished(values, stage);
            // La observación del profesor se muestra a la familia recién con el 1° cuatrimestre publicado.
            var observation = hasEntry && (!familyView || stage >= PublicationStages.Cuatrimestre1) ? e!.Observation : null;
            return new ReportCardRow(subject.Id, subject.Name, values, observation);
        }).ToList();

        // Resumen de asistencia de esta inscripción.
        var attendance = await db.Attendances.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollment.Id)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
        int Count(string status) => attendance.GetValueOrDefault(status);

        // ¿La familia ya confirmó la lectura de la etapa publicada?
        var ack = await db.ReportCardAcknowledgements.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.SchoolYearId == course.SchoolYearId && x.Stage == stage)
            .FirstOrDefaultAsync();

        var schoolName = await db.SchoolSettings.Select(x => x.SchoolName).FirstOrDefaultAsync() ?? "Colegio";

        return new ReportCard(
            enrollment.StudentId,
            enrollment.Student.FullName,
            enrollment.Student.Dni,
            course.Nivel,
            Course.LabelOf(course.Nivel, course.Level, course.Division),
            course.SchoolYear.Year,
            course.SchoolYearId,
            course.SchoolYear.IsActive,
            schoolName,
            stage,
            PublicationStages.Labels[stage],
            familyView,
            rows,
            // El comentario general de Dirección también respeta la publicación del 1° cuatrimestre.
            !familyView || stage >= PublicationStages.Cuatrimestre1 ? enrollment.ReportCardComment : null,
            new AttendanceSummary(
                Count(AttendanceStatuses.Presente), Count(AttendanceStatuses.Ausente),
                Count(AttendanceStatuses.Tarde), Count(AttendanceStatuses.Justificado)),
            ack?.AtUtc,
            ack?.AcknowledgedBy);
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
