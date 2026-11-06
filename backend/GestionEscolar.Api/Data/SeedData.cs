using GestionEscolar.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Data;

/// <summary>
/// Prepara la base al arrancar la aplicación:
///  1. Aplica las migraciones pendientes (crea/actualiza las tablas).
///  2. Asegura que exista la fila de configuración institucional.
///  3. Si la base está vacía, crea el año lectivo y las materias de Primaria y Secundaria.
///  4. Si Seed:DemoData = true, carga personas, notas y asistencia ficticias para la demo.
///     Si es false, crea solo la cuenta de Administrador indicada en Seed:AdminDni.
/// Todo es idempotente: si los datos ya existen, no se duplican.
/// </summary>
public static class SeedData
{
    /// <summary>Cuentas que se muestran como botones en el login cuando la app está en modo demo.</summary>
    public static readonly (string Label, string Dni)[] DemoAccounts =
    [
        ("Administración", "20999999"),
        ("Dirección", "20111111"),
        ("Secretaría", "20222222"),
        ("Preceptor", "20333333"),
        ("Profesor", "20444444"),
        ("Familia", "50111001")
    ];

    // Materias por nivel y año.
    //  - Secundaria 1°: exactamente las del boletín que mandó el colegio.
    //  - Secundaria 2° y 3°: diseño curricular de la Provincia de Buenos Aires (confirmar con el colegio).
    //  - Primaria: áreas del diseño curricular bonaerense (confirmar con el colegio).
    //  - Secundaria 4° a 6° e Inicial dependen del colegio: se cargan desde "Cursos y materias".
    private static readonly string[] Secundaria1 =
    [
        "CIENCIAS NATURALES", "CIENCIAS SOCIALES", "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "INGLÉS",
        "MATEMÁTICA", "PRÁCTICAS DEL LENGUAJE", "CONSTRUCCIÓN DE LA CIUDADANÍA", "PORTUGUÉS"
    ];
    private static readonly string[] Secundaria2y3 =
    [
        "BIOLOGÍA", "CONSTRUCCIÓN DE LA CIUDADANÍA", "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "FÍSICO-QUÍMICA",
        "GEOGRAFÍA", "HISTORIA", "INGLÉS", "MATEMÁTICA", "PRÁCTICAS DEL LENGUAJE", "PORTUGUÉS"
    ];
    private static readonly string[] Primaria =
    [
        "PRÁCTICAS DEL LENGUAJE", "MATEMÁTICA", "CIENCIAS SOCIALES", "CIENCIAS NATURALES",
        "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "INGLÉS"
    ];

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var people = services.GetRequiredService<PeopleService>();

        // 1. Esquema. La base en memoria (desarrollo sin PostgreSQL) no usa migraciones.
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();

        // 2. Configuración institucional (una sola fila).
        if (!await db.SchoolSettings.AnyAsync())
        {
            db.SchoolSettings.Add(new SchoolSettings());
            await db.SaveChangesAsync();
        }

        // 3. Estructura básica.
        if (!await db.SchoolYears.AnyAsync())
        {
            var year = configuration.GetValue("Seed:Year", DateTime.UtcNow.Year);
            db.SchoolYears.Add(new SchoolYear { Year = year, IsActive = true });
            AddSubjects(db, Niveles.Secundaria, 1, Secundaria1);
            AddSubjects(db, Niveles.Secundaria, 2, Secundaria2y3);
            AddSubjects(db, Niveles.Secundaria, 3, Secundaria2y3);
            foreach (var level in Niveles.LevelsOf(Niveles.Primaria)) AddSubjects(db, Niveles.Primaria, level, Primaria);
            await db.SaveChangesAsync();
        }

        // 4. Personas.
        if (await db.People.AnyAsync()) return;

        if (configuration.GetValue("Seed:DemoData", true))
        {
            await SeedDemoAsync(db, people);
        }
        else
        {
            // Producción con datos reales: solo la cuenta de Administración para empezar a cargar.
            var dni = configuration["Seed:AdminDni"];
            var name = configuration["Seed:AdminName"] ?? "Administración";
            if (!string.IsNullOrWhiteSpace(dni))
            {
                var (_, error) = await people.CreateAsync(name, dni, PersonTypes.Administrador);
                if (error is not null) throw new InvalidOperationException($"No se pudo crear la cuenta de Administración: {error}");
            }
        }
    }

    private static void AddSubjects(AppDbContext db, string nivel, int level, string[] names) =>
        db.Subjects.AddRange(names.Select((name, i) => new Subject { Name = name, Nivel = nivel, Level = level, DisplayOrder = i + 1 }));

    /// <summary>Datos ficticios coherentes para mostrar el sistema funcionando.</summary>
    private static async Task SeedDemoAsync(AppDbContext db, PeopleService people)
    {
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        // Simulamos que estamos en noviembre: publicado hasta la 2° valoración y el 1° cuatrimestre cerrado.
        year.PublishedStage = PublicationStages.Valoracion2;
        year.IsTerm1Closed = true;

        var settings = await db.SchoolSettings.SingleAsync();
        settings.AbsenceAlertThreshold = 8;

        Course NewCourse(string nivel, int level, string division) =>
            db.Courses.Add(new Course { SchoolYearId = year.Id, Nivel = nivel, Level = level, Division = division }).Entity;
        var prim1A = NewCourse(Niveles.Primaria, 1, "A");
        var prim3A = NewCourse(Niveles.Primaria, 3, "A");
        var prim6A = NewCourse(Niveles.Primaria, 6, "A");
        var sec1A = NewCourse(Niveles.Secundaria, 1, "A");
        var sec1B = NewCourse(Niveles.Secundaria, 1, "B");
        var sec2A = NewCourse(Niveles.Secundaria, 2, "A");
        await db.SaveChangesAsync();

        // Pequeño atajo local: crea la persona y falla fuerte si algo sale mal (es solo la demo).
        async Task<Person> Create(string name, string dni, string type, int? courseId = null)
        {
            var (person, error) = await people.CreateAsync(name, dni, type, courseId: courseId);
            return person ?? throw new InvalidOperationException(error);
        }

        // ---- Personal ----
        await Create("Rodríguez Silvia", "20999999", PersonTypes.Administrador);
        await Create("Fernández Graciela", "20111111", PersonTypes.Director);
        await Create("López Mariana", "20222222", PersonTypes.Secretaria);
        var preceptor = await Create("Ruiz Pablo", "20333333", PersonTypes.Preceptor);
        var laura = await Create("Gómez Laura", "20444444", PersonTypes.Profesor);
        var diego = await Create("Sosa Diego", "20555555", PersonTypes.Profesor);
        var carolina = await Create("Díaz Carolina", "20666666", PersonTypes.Profesor);
        var monica = await Create("Paz Mónica", "20777777", PersonTypes.Profesor);

        // ---- Alumnos de Secundaria 1° A (los del ejemplo de boletín) ----
        string[] sec1ANames = ["Acosta Martina", "Benítez Lucas", "Castro Valentina", "Domínguez Thiago",
                               "Fernández Sofía", "Giménez Benjamín", "Herrera Emma", "Ibarra Joaquín"];
        for (var i = 0; i < sec1ANames.Length; i++) await Create(sec1ANames[i], $"5011100{i + 1}", PersonTypes.Alumno, sec1A.Id);

        // ---- Resto de los cursos: nombres generados (6 alumnos por curso) ----
        string[] surnames = ["Juárez", "Luna", "Medina", "Núñez", "Ortiz", "Pereyra", "Quiroga", "Romero", "Sánchez", "Torres", "Vega", "Zárate"];
        string[] firstNames = ["Catalina", "Mateo", "Isabella", "Felipe", "Olivia", "Bautista", "Mía", "Santino", "Julieta", "Lautaro", "Renata", "Bruno"];
        var otherCourses = new[] { prim1A, prim3A, prim6A, sec1B, sec2A };
        var dniBase = 45000000;
        for (var c = 0; c < otherCourses.Length; c++)
        {
            for (var i = 0; i < 6; i++)
            {
                var name = $"{surnames[(c * 5 + i) % surnames.Length]} {firstNames[(c * 3 + i * 7) % firstNames.Length]}";
                await Create(name, (dniBase + c * 100 + i).ToString(), PersonTypes.Alumno, otherCourses[c].Id);
            }
        }

        // ---- Hermanos: Martina (Secundaria 1° A) y Tomás (Primaria 3° A) ----
        // Con el DNI de cualquiera de los dos, la familia ve a ambos.
        var tomas = await Create("Acosta Tomás", "52111002", PersonTypes.Alumno, prim3A.Id);
        var martina = await db.People.SingleAsync(x => x.Dni == "50111001");
        await people.LinkSiblingsAsync(martina.Id, tomas.Id);

        // ---- Asignaciones ----
        var subjects = await db.Subjects.ToListAsync();
        int SubjectId(string nivel, int level, string name) => subjects.Single(x => x.Nivel == nivel && x.Level == level && x.Name == name).Id;
        TeachingAssignment Assign(Person teacher, Course course, string subject) =>
            new() { TeacherId = teacher.Id, CourseId = course.Id, SubjectId = SubjectId(course.Nivel, course.Level, subject) };

        db.TeachingAssignments.AddRange(
            Assign(laura, sec1A, "MATEMÁTICA"),
            Assign(laura, sec1A, "CIENCIAS NATURALES"),
            Assign(laura, sec2A, "MATEMÁTICA"),
            Assign(diego, sec1A, "PRÁCTICAS DEL LENGUAJE"),
            Assign(diego, sec1A, "CIENCIAS SOCIALES"),
            Assign(carolina, sec1A, "INGLÉS"),
            Assign(carolina, sec1A, "PORTUGUÉS"),
            Assign(monica, prim3A, "PRÁCTICAS DEL LENGUAJE"),
            Assign(monica, prim3A, "MATEMÁTICA"));
        db.PreceptorAssignments.AddRange(
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = sec1A.Id },
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = sec1B.Id },
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = sec2A.Id });
        await db.SaveChangesAsync();

        // ---- Notas de Secundaria 1° A: 1° cuatrimestre completo, 2° valoración cargada y
        //      2° cuatrimestre solo en Matemática (así el panel muestra carga pendiente) ----
        var enrollments = await db.Enrollments.Where(x => x.CourseId == sec1A.Id).OrderBy(x => x.Id).ToListAsync();
        var gradedSubjects = new[] { "MATEMÁTICA", "CIENCIAS NATURALES", "PRÁCTICAS DEL LENGUAJE", "CIENCIAS SOCIALES", "INGLÉS", "PORTUGUÉS" }
            .Select(name => SubjectId(Niveles.Secundaria, 1, name)).ToList();

        for (var s = 0; s < enrollments.Count; s++)
        {
            for (var m = 0; m < gradedSubjects.Count; m++)
            {
                // Notas "pseudoaleatorias" pero siempre iguales (de 4 a 10), para que la demo sea reproducible.
                var grade1 = 4 + (s * 3 + m * 5) % 7;
                var grade2 = 4 + (s * 5 + m * 3 + 2) % 7;
                db.ReportCardEntries.Add(new ReportCardEntry
                {
                    EnrollmentId = enrollments[s].Id,
                    SubjectId = gradedSubjects[m],
                    Valuation1 = ValuationFor(grade1),
                    Grade1 = grade1,
                    // Quien no aprobó (menos de 7) rinde intensificación.
                    Intensification1 = grade1 < 7 ? Math.Min(10, grade1 + 3) : null,
                    Valuation2 = ValuationFor(grade2),
                    Grade2 = m == 0 ? grade2 : null,
                    Observation = grade1 < 6 ? "Necesita reforzar los contenidos del cuatrimestre." : null,
                    UpdatedAtUtc = DateTime.UtcNow,
                    UpdatedBy = "Carga inicial"
                });
            }
        }
        enrollments[0].ReportCardComment = "Muy buen desempeño general. Felicitaciones por el compromiso.";

        // ---- Asistencia de octubre y noviembre en Secundaria 1° A ----
        // Domínguez Thiago (índice 3) falta seguido, para que aparezca en la alerta de inasistencias.
        var day = new DateOnly(year.Year, 10, 1);
        var end = new DateOnly(year.Year, 11, 5);
        var dayIndex = 0;
        for (; day <= end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            for (var s = 0; s < enrollments.Count; s++)
            {
                var status = s == 3 && dayIndex % 2 == 0 ? AttendanceStatuses.Ausente
                           : (s + dayIndex) % 11 == 0 ? AttendanceStatuses.Ausente
                           : (s + dayIndex) % 7 == 0 ? AttendanceStatuses.Tarde
                           : AttendanceStatuses.Presente;
                db.Attendances.Add(new Attendance
                {
                    EnrollmentId = enrollments[s].Id, Date = day, Status = status, UpdatedAtUtc = DateTime.UtcNow,
                    Observation = status == AttendanceStatuses.Tarde ? "Ingresó 8:15" : null
                });
            }
            dayIndex++;
        }

        // ---- Avisos ----
        db.Notifications.AddRange(
            new Notification
            {
                Kind = NotificationKinds.Comunicado,
                Title = "¡Bienvenidos al nuevo sistema!",
                Body = "Desde ahora pueden consultar el boletín y la asistencia de sus hijos desde aquí. Recomendamos cambiar la clave inicial.",
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                CreatedBy = "Fernández Graciela"
            },
            new Notification
            {
                Kind = NotificationKinds.Comunicado,
                Title = "Salida educativa de 1° A",
                Body = "El viernes 13 visitamos el Museo de Ciencias Naturales. Traer la autorización firmada.",
                CourseId = sec1A.Id,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                CreatedBy = "Fernández Graciela"
            });
        await db.SaveChangesAsync();
    }

    /// <summary>Valoración conceptual aproximada a partir de una nota numérica.</summary>
    private static string ValuationFor(int grade) => grade >= 7 ? "TEA" : grade >= 4 ? "TEP" : "TED";
}
