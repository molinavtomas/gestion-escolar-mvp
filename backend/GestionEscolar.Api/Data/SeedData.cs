using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (!await db.People.AnyAsync())
        {
            await SeedSchoolAsync(db);
        }

        await SeedUsersAsync(services, db, configuration);
    }

    private static async Task SeedSchoolAsync(AppDbContext db)
    {
        var admin = new Person { FullName = "Mariana López", Identifier = "DNI-28000111", Email = "secretaria@demo.edu", Type = PersonTypes.Staff };
        var teacher1 = new Person { FullName = "Laura Gómez", Identifier = "DOC-1001", Email = "docente@demo.edu", Type = PersonTypes.Teacher };
        var teacher2 = new Person { FullName = "Diego Fernández", Identifier = "DOC-1002", Email = "diego@demo.edu", Type = PersonTypes.Teacher };
        var preceptor = new Person { FullName = "Pablo Ruiz", Identifier = "PRE-2001", Email = "preceptor@demo.edu", Type = PersonTypes.Preceptor };
        var responsible1 = new Person { FullName = "Ana Álvarez", Identifier = "DNI-32000222", Email = "familia@demo.edu", Phone = "11 5555-1010", Type = PersonTypes.Responsible };
        var responsible2 = new Person { FullName = "Carla Torres", Identifier = "DNI-33000333", Email = "carla@demo.edu", Phone = "11 5555-2020", Type = PersonTypes.Responsible };
        var student1 = new Person { FullName = "Sofía Álvarez", Identifier = "ALU-0001", Type = PersonTypes.Student };
        var student2 = new Person { FullName = "Mateo Álvarez", Identifier = "ALU-0002", Type = PersonTypes.Student };
        var student3 = new Person { FullName = "Lucía Torres", Identifier = "ALU-0003", Type = PersonTypes.Student };
        var student4 = new Person { FullName = "Tomás Medina", Identifier = "ALU-0004", Type = PersonTypes.Student };
        var student5 = new Person { FullName = "Valentina Suárez", Identifier = "ALU-0005", Type = PersonTypes.Student };

        db.People.AddRange(admin, teacher1, teacher2, preceptor, responsible1, responsible2, student1, student2, student3, student4, student5);
        await db.SaveChangesAsync();

        var year = new SchoolYear { Year = 2026, IsActive = true };
        db.SchoolYears.Add(year);
        await db.SaveChangesAsync();

        var term1 = new Term { SchoolYearId = year.Id, Name = "Primer trimestre", StartDate = new DateOnly(2026, 3, 2), EndDate = new DateOnly(2026, 5, 29), AreAveragesPublished = true, IsReportCardPublished = true };
        var term2 = new Term { SchoolYearId = year.Id, Name = "Segundo trimestre", StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31), AreAveragesPublished = true, IsReportCardPublished = false };
        var term3 = new Term { SchoolYearId = year.Id, Name = "Tercer trimestre", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 11, 27), AreAveragesPublished = false, IsReportCardPublished = false };
        db.Terms.AddRange(term1, term2, term3);

        var course1 = new Course { SchoolYearId = year.Id, Name = "1°", Division = "A" };
        var course2 = new Course { SchoolYearId = year.Id, Name = "2°", Division = "A" };
        db.Courses.AddRange(course1, course2);

        var math = new Subject { Name = "Matemática" };
        var language = new Subject { Name = "Lengua" };
        var naturalScience = new Subject { Name = "Ciencias Naturales" };
        var socialScience = new Subject { Name = "Ciencias Sociales" };
        db.Subjects.AddRange(math, language, naturalScience, socialScience);
        await db.SaveChangesAsync();

        db.FamilyLinks.AddRange(
            new FamilyLink { ResponsibleId = responsible1.Id, StudentId = student1.Id, Relationship = "Madre" },
            new FamilyLink { ResponsibleId = responsible1.Id, StudentId = student2.Id, Relationship = "Madre" },
            new FamilyLink { ResponsibleId = responsible2.Id, StudentId = student3.Id, Relationship = "Madre" });

        var enrollment1 = new Enrollment { StudentId = student1.Id, CourseId = course1.Id };
        var enrollment2 = new Enrollment { StudentId = student2.Id, CourseId = course1.Id };
        var enrollment3 = new Enrollment { StudentId = student3.Id, CourseId = course1.Id };
        var enrollment4 = new Enrollment { StudentId = student4.Id, CourseId = course2.Id };
        var enrollment5 = new Enrollment { StudentId = student5.Id, CourseId = course2.Id };
        db.Enrollments.AddRange(enrollment1, enrollment2, enrollment3, enrollment4, enrollment5);

        var assignment1 = new TeachingAssignment { TeacherId = teacher1.Id, CourseId = course1.Id, SubjectId = math.Id };
        var assignment2 = new TeachingAssignment { TeacherId = teacher2.Id, CourseId = course1.Id, SubjectId = language.Id };
        var assignment3 = new TeachingAssignment { TeacherId = teacher1.Id, CourseId = course2.Id, SubjectId = naturalScience.Id };
        var assignment4 = new TeachingAssignment { TeacherId = teacher2.Id, CourseId = course2.Id, SubjectId = socialScience.Id };
        db.TeachingAssignments.AddRange(assignment1, assignment2, assignment3, assignment4);
        db.PreceptorAssignments.AddRange(
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = course1.Id },
            new PreceptorAssignment { PreceptorId = preceptor.Id, CourseId = course2.Id });
        await db.SaveChangesAsync();

        var evaluation1 = new Evaluation { TeachingAssignmentId = assignment1.Id, TermId = term3.Id, Title = "Números y operaciones", Date = new DateOnly(2026, 9, 18) };
        var evaluation2 = new Evaluation { TeachingAssignmentId = assignment1.Id, TermId = term3.Id, Title = "Resolución de problemas", Date = new DateOnly(2026, 10, 2) };
        var evaluation3 = new Evaluation { TeachingAssignmentId = assignment2.Id, TermId = term3.Id, Title = "Comprensión lectora", Date = new DateOnly(2026, 9, 25) };
        db.Evaluations.AddRange(evaluation1, evaluation2, evaluation3);
        await db.SaveChangesAsync();

        var grades = new[]
        {
            new Grade { EvaluationId = evaluation1.Id, EnrollmentId = enrollment1.Id, Score = 9, Observation = "Excelente resolución", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation1.Id, EnrollmentId = enrollment2.Id, Score = 7, Observation = "Buen trabajo", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation1.Id, EnrollmentId = enrollment3.Id, Score = 8, Observation = "Muy buen progreso", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation2.Id, EnrollmentId = enrollment1.Id, Score = 8, Observation = "Revisar el último ejercicio", IsPublished = false, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation3.Id, EnrollmentId = enrollment1.Id, Score = 10, Observation = "Lectura fluida", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation3.Id, EnrollmentId = enrollment2.Id, Score = 6, Observation = "Practicar inferencias", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow },
            new Grade { EvaluationId = evaluation3.Id, EnrollmentId = enrollment3.Id, Score = 9, Observation = "Muy buena interpretación", IsPublished = true, UpdatedAtUtc = DateTime.UtcNow }
        };
        db.Grades.AddRange(grades);

        var attendanceDate = new DateOnly(2026, 10, 8);
        db.Attendances.AddRange(
            new Attendance { EnrollmentId = enrollment1.Id, Date = attendanceDate, Status = "Presente", UpdatedAtUtc = DateTime.UtcNow },
            new Attendance { EnrollmentId = enrollment2.Id, Date = attendanceDate, Status = "Tarde", Observation = "Ingreso 08:15", UpdatedAtUtc = DateTime.UtcNow },
            new Attendance { EnrollmentId = enrollment3.Id, Date = attendanceDate, Status = "Ausente", UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.GradeHistory.AddRange(grades.Select(grade => new GradeHistory
        {
            GradeId = grade.Id,
            NewScore = grade.Score,
            NewObservation = grade.Observation,
            NewPublished = grade.IsPublished,
            ChangedBy = "Carga inicial",
            ChangedAtUtc = grade.UpdatedAtUtc
        }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedUsersAsync(IServiceProvider services, AppDbContext db, IConfiguration configuration)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var password = configuration["DemoSeed:Password"] ?? "Demo1234!";

        var users = new[]
        {
            ("secretaria@demo.edu", "Mariana López", Profiles.Administration),
            ("docente@demo.edu", "Laura Gómez", Profiles.Teacher),
            ("preceptor@demo.edu", "Pablo Ruiz", Profiles.Preceptor),
            ("familia@demo.edu", "Ana Álvarez", Profiles.Family)
        };

        foreach (var (email, name, profile) in users)
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;

            var person = await db.People.SingleAsync(x => x.Email == email);
            var result = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                Profile = profile,
                PersonId = person.Id,
                IsActive = true
            }, password);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"No se pudo crear {email}: {string.Join(", ", result.Errors.Select(x => x.Description))}");
            }
        }
    }
}
