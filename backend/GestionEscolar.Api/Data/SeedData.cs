using GestionEscolar.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Data;

/// <summary>
/// Prepara la base al arrancar la aplicación:
///  1. Aplica las migraciones pendientes (crea/actualiza las tablas).
///  2. Si la base está vacía, crea el año lectivo y las materias de 1° a 3° año.
///  3. Si Seed:DemoData = true, carga personas, notas y asistencia ficticias para la demo.
///     Si es false, crea solo la cuenta de Dirección indicada en Seed:DirectorDni.
/// Todo es idempotente: si los datos ya existen, no se duplican.
/// </summary>
public static class SeedData
{
    /// <summary>Cuentas que se muestran como botones en el login cuando la app está en modo demo.</summary>
    public static readonly (string Label, string Dni)[] DemoAccounts =
    [
        ("Dirección", "20111111"),
        ("Secretaría", "20222222"),
        ("Preceptor", "20333333"),
        ("Profesor", "20444444"),
        ("Familia", "50111001")
    ];

    // Materias por año. Las de 1° son exactamente las del boletín que mandó el colegio.
    // Las de 2° y 3° son las del diseño curricular de la Provincia de Buenos Aires: confirmar con el colegio.
    // 4° a 6° dependen de la orientación, así que se cargan desde la pantalla "Cursos y materias".
    private static readonly Dictionary<int, string[]> SubjectsByLevel = new()
    {
        [1] =
        [
            "CIENCIAS NATURALES", "CIENCIAS SOCIALES", "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "INGLÉS",
            "MATEMÁTICA", "PRÁCTICAS DEL LENGUAJE", "CONSTRUCCIÓN DE LA CIUDADANÍA", "PORTUGUÉS"
        ],
        [2] =
        [
            "BIOLOGÍA", "CONSTRUCCIÓN DE LA CIUDADANÍA", "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "FÍSICO-QUÍMICA",
            "GEOGRAFÍA", "HISTORIA", "INGLÉS", "MATEMÁTICA", "PRÁCTICAS DEL LENGUAJE", "PORTUGUÉS"
        ],
        [3] =
        [
            "BIOLOGÍA", "CONSTRUCCIÓN DE LA CIUDADANÍA", "EDUCACIÓN ARTÍSTICA", "EDUCACIÓN FÍSICA", "FÍSICO-QUÍMICA",
            "GEOGRAFÍA", "HISTORIA", "INGLÉS", "MATEMÁTICA", "PRÁCTICAS DEL LENGUAJE", "PORTUGUÉS"
        ]
    };

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var people = services.GetRequiredService<PeopleService>();

        // 1. Esquema. La base en memoria (desarrollo sin PostgreSQL) no usa migraciones.
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();

        // 2. Estructura básica.
        if (!await db.SchoolYears.AnyAsync())
        {
            var year = configuration.GetValue("Seed:Year", DateTime.UtcNow.Year);
            db.SchoolYears.Add(new SchoolYear { Year = year, IsActive = true });
            foreach (var (level, names) in SubjectsByLevel)
            {
                db.Subjects.AddRange(names.Select((name, i) => new Subject { Name = name, Level = level, DisplayOrder = i + 1 }));
            }
            await db.SaveChangesAsync();
        }

        // 3. Personas.
        if (await db.People.AnyAsync()) return;

        if (configuration.GetValue("Seed:DemoData", true))
        {
            await SeedDemoAsync(db, people);
        }
        else
        {
            // Producción con datos reales: solo la cuenta de Dirección para empezar a cargar.
            var dni = configuration["Seed:DirectorDni"];
            var name = configuration["Seed:DirectorName"] ?? "Dirección";
            if (!string.IsNullOrWhiteSpace(dni))
            {
                var (_, error) = await people.CreateAsync(name, dni, PersonTypes.Director);
                if (error is not null) throw new InvalidOperationException($"No se pudo crear la cuenta de Dirección: {error}");
            }
        }
    }

    /// <summary>Datos ficticios coherentes para mostrar el sistema funcionando.</summary>
    private static async Task SeedDemoAsync(AppDbContext db, PeopleService people)
    {
        var year = await db.SchoolYears.SingleAsync(x => x.IsActive);
        // Simulamos que estamos en noviembre: ya se publicó hasta la 2° valoración preliminar.
        year.PublishedStage = PublicationStages.Valoracion2;

        var course1 = new Course { SchoolYearId = year.Id, Level = 1, Division = "A" };
        var course2 = new Course { SchoolYearId = year.Id, Level = 2, Division = "A" };
        db.Courses.AddRange(course1, course2);
        await db.SaveChangesAsync();

        // Pequeño atajo local: crea la persona y falla fuerte si algo sale mal (es solo la demo).
        async Task<Person> Create(string name, string dni, string type, int? courseId = null)
        {
            var (person, error) = await people.CreateAsync(name, dni, type, courseId: courseId);
            return person ?? throw new InvalidOperationException(error);
        }

        // ---- Personal ----
        await Create("Fernández Graciela", "20111111", PersonTypes.Director);
        await Create("López Mariana", "20222222", PersonTypes.Secretaria);
        var preceptor = await Create("Ruiz Pablo", "20333333", PersonTypes.Preceptor);
        var laura = await Create("Gómez Laura", "20444444", PersonTypes.Profesor);
        var diego = await Create("Sosa Diego", "20555555", PersonTypes.Profesor);
        var carolina = await Create("Díaz Carolina", "20666666", PersonTypes.Profesor);

        // ---- Alumnos ----
        string[] names1 = ["Acosta Martina", "Benítez Lucas", "Castro Valentina", "Domínguez Thiago",
                           "Fernández Sofía", "Giménez Benjamín", "Herrera Emma", "Ibarra Joaquín"];
        string[] names2 = ["Juárez Catalina", "Luna Mateo", "Medina Isabella", "Núñez Felipe"];
        for (var i = 0; i < names1.Length; i++) await Create(names1[i], $"5011100{i + 1}", PersonTypes.Alumno, course1.Id);
        for (var i = 0; i < names2.Length; i++) await Create(names2[i], $"4922200{i + 1}", PersonTypes.Alumno, course2.Id);

        // ---- Asignaciones ----
        var subjects = await db.Subjects.ToListAsync();
        int SubjectId(int level, string name) => subjects.Single(x => x.Level == level && x.Name == name).Id;

        db.TeachingAssignments.AddRange(
            new TeachingAssignment { TeacherId = laura.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "MATEMÁTICA") },
            new TeachingAssignment { TeacherId = laura.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "CIENCIAS NATURALES") },
            new TeachingAssignment { TeacherId = laura.Id, CourseId = course2.Id, SubjectId = SubjectId(2, "MATEMÁTICA") },
            new TeachingAssignment { TeacherId = diego.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "PRÁCTICAS DEL LENGUAJE") },
            new TeachingAssignment { TeacherId = diego.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "CIENCIAS SOCIALES") },
            new TeachingAssignment { TeacherId = carolina.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "INGLÉS") },
            new TeachingAssignment { TeacherId = carolina.Id, CourseId = course1.Id, SubjectId = SubjectId(1, "PORTUGUÉS") });
        db.PreceptorAssignments.AddRange(
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = course1.Id },
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = course2.Id });
        await db.SaveChangesAsync();

        // ---- Notas de 1° A: 1° cuatrimestre completo, 2° valoración cargada y
        //      2° cuatrimestre solo en Matemática (así el tablero muestra carga pendiente) ----
        var enrollments = await db.Enrollments.Where(x => x.CourseId == course1.Id).OrderBy(x => x.Id).ToListAsync();
        var gradedSubjects = new[] { "MATEMÁTICA", "CIENCIAS NATURALES", "PRÁCTICAS DEL LENGUAJE", "CIENCIAS SOCIALES", "INGLÉS", "PORTUGUÉS" }
            .Select(name => SubjectId(1, name)).ToList();

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
                    UpdatedAtUtc = DateTime.UtcNow,
                    UpdatedBy = "Carga inicial"
                });
            }
        }

        // ---- Asistencia de la última semana en 1° A ----
        var monday = new DateOnly(year.Year, 11, 2);
        for (var d = 0; d < 4; d++)
        {
            for (var s = 0; s < enrollments.Count; s++)
            {
                var status = (s + d) % 9 == 0 ? AttendanceStatuses.Ausente
                           : (s + d) % 7 == 0 ? AttendanceStatuses.Tarde
                           : AttendanceStatuses.Presente;
                db.Attendances.Add(new Attendance
                {
                    EnrollmentId = enrollments[s].Id, Date = monday.AddDays(d), Status = status, UpdatedAtUtc = DateTime.UtcNow
                });
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Valoración conceptual aproximada a partir de una nota numérica.</summary>
    private static string ValuationFor(int grade) => grade >= 7 ? "TEA" : grade >= 4 ? "TEP" : "TED";
}
