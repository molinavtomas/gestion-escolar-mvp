using Microsoft.AspNetCore.Identity;

namespace GestionEscolar.Api.Data;

// =====================================================================================
// Entidades del dominio. Cada clase es una tabla en PostgreSQL (vía Entity Framework).
// Las relaciones e índices únicos se configuran en AppDbContext.OnModelCreating.
// =====================================================================================

/// <summary>
/// Cuenta de acceso. Hereda de IdentityUser, que ya trae usuario, hash de la clave,
/// bloqueo por intentos fallidos, etc. El nombre de usuario (UserName) es el DNI.
/// </summary>
public sealed class ApplicationUser : IdentityUser<int>
{
    /// <summary>Nombre que se muestra en la barra lateral.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Uno de los valores de <see cref="Roles"/>.</summary>
    public string Role { get; set; } = Roles.Familia;

    /// <summary>Una cuenta desactivada no puede iniciar sesión.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// true mientras la clave siga siendo el DNI. Se usa para mostrar un aviso
    /// recomendando cambiarla (el cambio es opcional, tal como se acordó).
    /// </summary>
    public bool UsesDefaultPassword { get; set; } = true;

    /// <summary>Persona a la que pertenece la cuenta. Para "Familia" es el alumno.</summary>
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
}

/// <summary>Cualquier persona del colegio: alumnos, profesores, preceptores, secretaría y dirección.</summary>
public sealed class Person
{
    public int Id { get; set; }

    /// <summary>Formato recomendado: "Apellido Nombre", así los listados quedan ordenados por apellido.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>DNI sin puntos. Es único y es el usuario (y la clave inicial) de la cuenta.</summary>
    public string Dni { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Uno de los valores de <see cref="PersonTypes"/>.</summary>
    public string Type { get; set; } = PersonTypes.Alumno;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Año lectivo (ej. 2026). Guarda también hasta qué etapa está publicado el boletín.
/// Por ahora el sistema trabaja con un único año activo.
/// </summary>
public sealed class SchoolYear
{
    public int Id { get; set; }
    public int Year { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Uno de los valores de <see cref="PublicationStages"/>.</summary>
    public int PublishedStage { get; set; } = PublicationStages.Nada;
}

/// <summary>Curso concreto, ej. "1° A". Level es el año (1 a 6) y Division la letra.</summary>
public sealed class Course
{
    public int Id { get; set; }
    public int SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
    public int Level { get; set; }
    public string Division { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Arma la etiqueta "1° A". Es estático para poder usarlo en proyecciones de EF.</summary>
    public static string LabelOf(int level, string division) => $"{level}° {division}";
}

/// <summary>
/// Materia de un año (Level). El boletín de cada año lista sus materias en el orden de DisplayOrder,
/// por eso "el formato es el mismo, solo cambian las materias".
/// </summary>
public sealed class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Inscripción de un alumno en un curso. Un alumno tiene una sola inscripción activa.</summary>
public sealed class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Person Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

/// <summary>Un profesor dicta una materia en un curso. Es lo que le da permiso para cargar esas notas.</summary>
public sealed class TeachingAssignment
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public Person Teacher { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
}

/// <summary>Un preceptor está a cargo de un curso (asistencia y consulta de boletines).</summary>
public sealed class PreceptorAssignment
{
    public int Id { get; set; }
    public int PreceptorId { get; set; }
    public Person Preceptor { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
}

/// <summary>
/// Una fila del boletín: las notas de UN alumno (vía su inscripción) en UNA materia.
/// Las columnas replican exactamente la planilla del colegio. Todas son opcionales
/// porque se van completando a lo largo del año.
/// </summary>
public sealed class ReportCardEntry
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    // ---- 1° cuatrimestre ----
    /// <summary>1° valoración preliminar: TEA, TEP, TED o un número del 1 al 10.</summary>
    public string? Valuation1 { get; set; }
    public int? Grade1 { get; set; }
    public int? Intensification1 { get; set; }

    // ---- 2° cuatrimestre ----
    /// <summary>2° valoración preliminar: TEA, TEP, TED o un número del 1 al 10.</summary>
    public string? Valuation2 { get; set; }
    public int? Grade2 { get; set; }
    public int? Intensification2 { get; set; }

    // ---- Intensificación y cierre ----
    public int? December { get; set; }
    public int? February { get; set; }
    public int? FinalGrade { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Historial de cambios de notas: quién cambió qué columna, de qué valor a qué valor y cuándo.
/// Sirve para resolver reclamos ("yo había puesto un 7").
/// </summary>
public sealed class ReportCardChange
{
    public int Id { get; set; }
    public int ReportCardEntryId { get; set; }
    public ReportCardEntry ReportCardEntry { get; set; } = null!;
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>Asistencia de un alumno en un día.</summary>
public sealed class Attendance
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Status { get; set; } = AttendanceStatuses.Presente;
    public string? Observation { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
