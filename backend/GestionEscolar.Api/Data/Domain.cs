using Microsoft.AspNetCore.Identity;

namespace GestionEscolar.Api.Data;

public static class Profiles
{
    public const string Administration = "Administracion";
    public const string Teacher = "Docente";
    public const string Preceptor = "Preceptor";
    public const string Family = "Responsable";
}

public static class PersonTypes
{
    public const string Student = "Alumno";
    public const string Teacher = "Docente";
    public const string Preceptor = "Preceptor";
    public const string Responsible = "Responsable";
    public const string Staff = "Personal";
}

public static class AttendanceStatuses
{
    public static readonly string[] All = ["Presente", "Ausente", "Tarde", "Justificado"];
}

public sealed class ApplicationUser : IdentityUser<int>
{
    public string DisplayName { get; set; } = string.Empty;
    public string Profile { get; set; } = Profiles.Family;
    public bool IsActive { get; set; } = true;
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
}

public sealed class Person
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Type { get; set; } = PersonTypes.Student;
    public bool IsActive { get; set; } = true;
}

public sealed class FamilyLink
{
    public int Id { get; set; }
    public int ResponsibleId { get; set; }
    public Person Responsible { get; set; } = null!;
    public int StudentId { get; set; }
    public Person Student { get; set; } = null!;
    public string Relationship { get; set; } = "Responsable";
    public bool IsActive { get; set; } = true;
}

public sealed class SchoolYear
{
    public int Id { get; set; }
    public int Year { get; set; }
    public bool IsActive { get; set; }
}

public sealed class Term
{
    public int Id { get; set; }
    public int SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool AreAveragesPublished { get; set; }
    public bool IsReportCardPublished { get; set; }
}

public sealed class Course
{
    public int Id { get; set; }
    public int SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Person Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class TeachingAssignment
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public Person Teacher { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class PreceptorAssignment
{
    public int Id { get; set; }
    public int PreceptorId { get; set; }
    public Person Preceptor { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class Evaluation
{
    public int Id { get; set; }
    public int TeachingAssignmentId { get; set; }
    public TeachingAssignment TeachingAssignment { get; set; } = null!;
    public int TermId { get; set; }
    public Term Term { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
}

public sealed class Grade
{
    public int Id { get; set; }
    public int EvaluationId { get; set; }
    public Evaluation Evaluation { get; set; } = null!;
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public int Score { get; set; }
    public string? Observation { get; set; }
    public bool IsPublished { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GradeHistory
{
    public int Id { get; set; }
    public int GradeId { get; set; }
    public Grade Grade { get; set; } = null!;
    public int? PreviousScore { get; set; }
    public int NewScore { get; set; }
    public string? PreviousObservation { get; set; }
    public string? NewObservation { get; set; }
    public bool PreviousPublished { get; set; }
    public bool NewPublished { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}

public sealed class Attendance
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Status { get; set; } = AttendanceStatuses.All[0];
    public string? Observation { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
