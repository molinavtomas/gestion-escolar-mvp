using GestionEscolar.Api.Data;

namespace GestionEscolar.Api.Auth;

/// <summary>
/// MATRIZ DE PERMISOS: qué puede hacer cada rol, en un solo lugar.
///
/// La API la usa para decidir y además se la envía al frontend (en /api/auth/me), que la usa
/// solo para mostrar u ocultar menús y botones. La seguridad real siempre la verifica la API:
/// esconder un botón no alcanza.
///
/// | Permiso            | Admin | Director | Secretaría | Preceptor      | Profesor        | Familia    |
/// |--------------------|-------|----------|------------|----------------|-----------------|------------|
/// | ViewAll            |  ✔    |   ✔      |    ✔       |   ✔            |                 |            |
/// | ManagePeople       |  ✔    |   ✔*     |    ✔*      |                |                 |            |
/// | ManageAcademic     |  ✔    |   ✔      |            |                |                 |            |
/// | EditGrades         |  ✔    |   ✔      |            |                | sus materias    |            |
/// | TakeAttendance     |  ✔    |   ✔      |            | sus cursos     |                 |            |
/// | ViewAttendance     |  ✔    |   ✔      |    ✔       |   ✔            | sus alumnos     | sus hijos  |
/// | ResetPasswords     | todas | -Admin   | -Dir/Admin | solo familias  |                 |            |
/// | SendNotices        |  ✔    |   ✔      |            |                |                 |            |
/// | Admin (auditoría,  |  ✔    |          |            |                |                 |            |
/// |  config, ciclos)   |       |          |            |                |                 |            |
///  * Director y Secretaría no pueden dar de alta/baja/editar Directores ni Administradores.
/// </summary>
public sealed record Permissions(
    bool ViewAll,
    bool ManagePeople,
    bool ManageAcademic,
    bool EditGrades,
    bool TakeAttendance,
    bool ViewAttendance,
    bool ResetPasswords,
    bool SendNotices,
    bool Admin,
    bool Family)
{
    public static Permissions For(string role) => role switch
    {
        Roles.Administrador => new(true, true, true, true, true, true, true, true, true, false),
        Roles.Director => new(true, true, true, true, true, true, true, true, false, false),
        Roles.Secretaria => new(true, true, false, false, false, true, true, false, false, false),
        Roles.Preceptor => new(true, false, false, false, true, true, true, false, false, false),
        Roles.Profesor => new(false, false, false, true, false, true, false, false, false, false),
        Roles.Familia => new(false, false, false, false, false, false, false, false, false, true),
        _ => new(false, false, false, false, false, false, false, false, false, false)
    };

    /// <summary>¿Este rol puede crear, editar o dar de baja a una persona de este tipo?</summary>
    public static bool CanManagePersonType(string role, string personType) => role switch
    {
        Roles.Administrador => true,
        Roles.Director or Roles.Secretaria => !PersonTypes.Protected.Contains(personType),
        _ => false
    };

    /// <summary>¿Este rol puede volver al DNI la clave de una persona de este tipo?</summary>
    public static bool CanResetPasswordOf(string role, string personType) => role switch
    {
        Roles.Administrador => true,
        Roles.Director => personType != PersonTypes.Administrador,
        Roles.Secretaria => !PersonTypes.Protected.Contains(personType),
        Roles.Preceptor => personType == PersonTypes.Alumno,
        _ => false
    };
}
