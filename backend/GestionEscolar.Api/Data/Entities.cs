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

    /// <summary>Última vez que el usuario abrió "Avisos" (para contar los no leídos).</summary>
    public DateTime? NotificationsSeenAtUtc { get; set; }

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

    /// <summary>
    /// Grupo familiar (solo alumnos). Los hermanos comparten el mismo número, y con el DNI de
    /// cualquiera de ellos la familia ve a todos. null = sin hermanos vinculados.
    /// </summary>
    public int? FamilyGroupId { get; set; }
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

    // Cierre de carga: con el período cerrado, los profesores ya no pueden modificar esas columnas.
    // Dirección/Administración sí (para corregir errores), y queda en el historial.
    public bool IsTerm1Closed { get; set; }
    public bool IsTerm2Closed { get; set; }
    public bool IsFinalClosed { get; set; }
}

/// <summary>
/// Curso concreto de un año lectivo, ej. "Secundaria 1° A".
/// Nivel = Inicial/Primaria/Secundaria, Level = año (o sala en Inicial), Division = letra.
/// </summary>
public sealed class Course
{
    public int Id { get; set; }
    public int SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
    public string Nivel { get; set; } = Niveles.Secundaria;
    public int Level { get; set; }
    public string Division { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Arma la etiqueta "Secundaria 1° A" o "Inicial Sala 4 A".</summary>
    public static string LabelOf(string nivel, int level, string division) =>
        nivel == Niveles.Inicial ? $"Inicial Sala {level} {division}" : $"{nivel} {level}° {division}";
}

/// <summary>
/// Materia de un año (Level). El boletín de cada año lista sus materias en el orden de DisplayOrder,
/// por eso "el formato es el mismo, solo cambian las materias".
/// </summary>
public sealed class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nivel { get; set; } = Niveles.Secundaria;
    public int Level { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Inscripción de un alumno en un curso. Un alumno tiene como mucho una inscripción activa
/// POR AÑO LECTIVO; las de años anteriores se conservan para poder ver sus boletines.
/// </summary>
public sealed class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Person Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    /// <summary>Observaciones generales de Dirección que se imprimen en el boletín.</summary>
    public string? ReportCardComment { get; set; }
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

    /// <summary>Observación del profesor sobre el alumno en esta materia (se imprime en el boletín).</summary>
    public string? Observation { get; set; }

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

/// <summary>
/// Configuración institucional (una sola fila). La edita el Administrador.
/// </summary>
public sealed class SchoolSettings
{
    public int Id { get; set; }
    public string SchoolName { get; set; } = "Colegio de los Buenos Ayres";

    /// <summary>Logo subido por el Administrador. Si es null se usa el escudo por defecto.</summary>
    public byte[]? LogoData { get; set; }
    public string? LogoContentType { get; set; }

    /// <summary>A partir de cuántas inasistencias se marca al alumno con una alerta.</summary>
    public int AbsenceAlertThreshold { get; set; } = 10;
}

/// <summary>
/// Registro de auditoría: ingresos al sistema y cambios administrativos.
/// Solo lo ve el Administrador (permite controlar a todos, incluido el Director).
/// </summary>
public sealed class AuditEntry
{
    public int Id { get; set; }
    public DateTime AtUtc { get; set; }
    /// <summary><see cref="AuditKinds"/>.</summary>
    public string Kind { get; set; } = AuditKinds.Cambio;
    public string? UserDni { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    /// <summary>Qué pasó, en pocas palabras (ej. "Alta de persona", "Clave incorrecta").</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Detalle legible (ej. "Pérez Juan (DNI 45123456) - Alumno").</summary>
    public string? Detail { get; set; }
    public string? Ip { get; set; }
}

/// <summary>
/// Aviso dentro del sistema para las familias: comunicados, publicación del boletín e inasistencias.
/// Destino: StudentId (un alumno), CourseId (un curso) o ninguno (todo el colegio).
/// </summary>
public sealed class Notification
{
    public int Id { get; set; }
    /// <summary><see cref="NotificationKinds"/>.</summary>
    public string Kind { get; set; } = NotificationKinds.Comunicado;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? StudentId { get; set; }
    public int? CourseId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Confirmación de lectura del boletín por parte de la familia (reemplaza la firma en papel).
/// Se registra por alumno, año lectivo y etapa publicada.
/// </summary>
public sealed class ReportCardAcknowledgement
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int SchoolYearId { get; set; }
    public int Stage { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}
