namespace GestionEscolar.Api.Data;

/// <summary>
/// Roles de las cuentas de usuario. Cada cuenta tiene exactamente un rol.
/// Se guardan como texto en la base y viajan dentro de la cookie de sesión
/// como claim de rol, lo que permite usar [Authorize(Roles = ...)] en los controladores.
/// </summary>
public static class Roles
{
    public const string Director = "Director";
    public const string Secretaria = "Secretaria";
    public const string Preceptor = "Preceptor";
    public const string Profesor = "Profesor";
    public const string Familia = "Familia";

    /// <summary>
    /// Personal directivo/administrativo: tiene acceso completo de gestión.
    /// Es una cadena separada por comas porque así la espera el atributo [Authorize(Roles = ...)].
    /// </summary>
    public const string Staff = Director + "," + Secretaria;

    /// <summary>Todos los roles, en el orden en que se muestran en la interfaz.</summary>
    public static readonly string[] All = [Director, Secretaria, Preceptor, Profesor, Familia];
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

    public static readonly string[] All = [Alumno, Profesor, Preceptor, Secretaria, Director];

    /// <summary>Devuelve el rol de la cuenta que corresponde a cada tipo de persona.</summary>
    public static string RoleFor(string personType) => personType switch
    {
        Alumno => Roles.Familia,
        Profesor => Roles.Profesor,
        Preceptor => Roles.Preceptor,
        Secretaria => Roles.Secretaria,
        Director => Roles.Director,
        _ => throw new ArgumentException($"Tipo de persona desconocido: {personType}")
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
