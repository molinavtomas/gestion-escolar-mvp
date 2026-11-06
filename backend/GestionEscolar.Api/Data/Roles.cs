namespace GestionEscolar.Api.Data;

/// <summary>
/// Roles de las cuentas de usuario. Cada cuenta tiene exactamente un rol.
/// Se guardan como texto en la base y viajan dentro de la cookie de sesión
/// como claim de rol, lo que permite usar [Authorize(Roles = ...)] en los controladores.
///
/// Matriz de permisos (resumen; el detalle está en Auth/Permissions.cs):
///   Administrador  → todo (dueñas del colegio).
///   Director       → gestión académica completa; NO auditoría, configuración, ciclo lectivo
///                    ni cuentas de Administrador/Director.
///   Secretaria     → ve todo; da altas/bajas de personas; NO carga notas ni publica.
///   Preceptor      → ve todo; toma asistencia de SUS cursos; restablece claves de familias.
///   Profesor       → solo sus materias (notas) y asistencia de sus alumnos (lectura).
///   Familia        → solo sus hijos.
/// </summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Director = "Director";
    public const string Secretaria = "Secretaria";
    public const string Preceptor = "Preceptor";
    public const string Profesor = "Profesor";
    public const string Familia = "Familia";

    // Grupos de roles. Son cadenas separadas por comas porque así las espera [Authorize(Roles = ...)].

    /// <summary>Conducción: configura la estructura académica, publica, cierra períodos y corrige notas.</summary>
    public const string Management = Administrador + "," + Director;

    /// <summary>Pueden dar altas y bajas de personas (Secretaría incluida: SEC-05 queda para más adelante).</summary>
    public const string PeopleManagers = Management + "," + Secretaria;

    /// <summary>Pueden VER todo lo académico (cursos, boletines, personas, panel).</summary>
    public const string ViewAll = PeopleManagers + "," + Preceptor;

    public static readonly string[] All = [Administrador, Director, Secretaria, Preceptor, Profesor, Familia];
}

/// <summary>
/// Tipo de persona registrada en el colegio. Es distinto del rol de la cuenta:
/// un "Alumno" es una persona, pero la cuenta que se crea con su DNI tiene rol "Familia"
/// (la usan los padres para ver el boletín de su hijo, tal como pidió el cliente).
/// </summary>
public static class PersonTypes
{
    public const string Alumno = "Alumno";
    public const string Profesor = "Profesor";
    public const string Preceptor = "Preceptor";
    public const string Secretaria = "Secretaria";
    public const string Director = "Director";
    public const string Administrador = "Administrador";

    public static readonly string[] All = [Alumno, Profesor, Preceptor, Secretaria, Director, Administrador];

    /// <summary>Tipos "jerárquicos": solo un Administrador puede crearlos, editarlos o darlos de baja.</summary>
    public static readonly string[] Protected = [Director, Administrador];

    /// <summary>Devuelve el rol de la cuenta que corresponde a cada tipo de persona.</summary>
    public static string RoleFor(string personType) => personType switch
    {
        Alumno => Roles.Familia,
        Profesor => Roles.Profesor,
        Preceptor => Roles.Preceptor,
        Secretaria => Roles.Secretaria,
        Director => Roles.Director,
        Administrador => Roles.Administrador,
        _ => throw new ArgumentException($"Tipo de persona desconocido: {personType}")
    };
}

/// <summary>
/// Niveles educativos. Cada curso y cada materia pertenecen a un nivel, así se pueden
/// tener "1° A de Primaria" y "1° A de Secundaria" con materias distintas.
/// </summary>
public static class Niveles
{
    public const string Inicial = "Inicial";
    public const string Primaria = "Primaria";
    public const string Secundaria = "Secundaria";

    public static readonly string[] All = [Inicial, Primaria, Secundaria];

    /// <summary>Años válidos de cada nivel. En Inicial el "año" es la sala (3, 4 o 5 años).</summary>
    public static int[] LevelsOf(string nivel) => nivel switch
    {
        Inicial => [3, 4, 5],
        Primaria => [1, 2, 3, 4, 5, 6],
        Secundaria => [1, 2, 3, 4, 5, 6],
        _ => []
    };
}

/// <summary>Estados posibles de la asistencia diaria.</summary>
public static class AttendanceStatuses
{
    public const string Presente = "Presente";
    public const string Ausente = "Ausente";
    public const string Tarde = "Tarde";
    public const string Justificado = "Justificado";

    public static readonly string[] All = [Presente, Ausente, Tarde, Justificado];
}

/// <summary>
/// Etapas de publicación del boletín. Dirección elige "hasta qué etapa" pueden ver las familias.
/// Son acumulativas: publicar la etapa 3 implica que las etapas 1 y 2 también están visibles.
/// El orden sigue el calendario real del año lectivo.
/// </summary>
public static class PublicationStages
{
    public const int Nada = 0;
    public const int Valoracion1 = 1;      // 1° valoración preliminar
    public const int Cuatrimestre1 = 2;    // calificación + intensificación del 1° cuatrimestre
    public const int Valoracion2 = 3;      // 2° valoración preliminar
    public const int Cuatrimestre2 = 4;    // calificación + intensificación del 2° cuatrimestre
    public const int Final = 5;            // intensificación diciembre/febrero + calificación final

    /// <summary>Etiquetas para mostrar en la pantalla de publicación.</summary>
    public static readonly string[] Labels =
    [
        "Nada publicado",
        "1° valoración preliminar",
        "1° cuatrimestre completo",
        "2° valoración preliminar",
        "2° cuatrimestre completo",
        "Boletín final (diciembre, febrero y calificación final)"
    ];
}

/// <summary>Tipos de aviso que reciben las familias dentro del sistema.</summary>
public static class NotificationKinds
{
    public const string Comunicado = "Comunicado";
    public const string Publicacion = "Publicación";
    public const string Inasistencia = "Inasistencia";
}

/// <summary>Tipos de registro de auditoría.</summary>
public static class AuditKinds
{
    /// <summary>Intentos de ingreso (correctos y fallidos).</summary>
    public const string Acceso = "Acceso";
    /// <summary>Cambios administrativos: altas, bajas, publicaciones, asignaciones, configuración...</summary>
    public const string Cambio = "Cambio";
}
