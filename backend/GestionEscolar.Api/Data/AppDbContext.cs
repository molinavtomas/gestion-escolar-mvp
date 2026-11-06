using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Data;

/// <summary>
/// Contexto de Entity Framework: representa la base de datos.
/// Hereda de IdentityDbContext para incluir las tablas de usuarios de ASP.NET Identity.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TeachingAssignment> TeachingAssignments => Set<TeachingAssignment>();
    public DbSet<PreceptorAssignment> PreceptorAssignments => Set<PreceptorAssignment>();
    public DbSet<ReportCardEntry> ReportCardEntries => Set<ReportCardEntry>();
    public DbSet<ReportCardChange> ReportCardChanges => Set<ReportCardChange>();
    public DbSet<Attendance> Attendances => Set<Attendance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---- Índices únicos: evitan duplicados aunque la interfaz falle ----
        builder.Entity<Person>().HasIndex(x => x.Dni).IsUnique();
        builder.Entity<Course>().HasIndex(x => new { x.SchoolYearId, x.Level, x.Division }).IsUnique();
        builder.Entity<Subject>().HasIndex(x => new { x.Level, x.Name }).IsUnique();
        builder.Entity<Enrollment>().HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();
        builder.Entity<TeachingAssignment>().HasIndex(x => new { x.CourseId, x.SubjectId, x.TeacherId }).IsUnique();
        builder.Entity<PreceptorAssignment>().HasIndex(x => new { x.PreceptorId, x.CourseId }).IsUnique();
        builder.Entity<ReportCardEntry>().HasIndex(x => new { x.EnrollmentId, x.SubjectId }).IsUnique();
        builder.Entity<Attendance>().HasIndex(x => new { x.EnrollmentId, x.Date }).IsUnique();

        // ---- Longitudes máximas: documentan el dato y evitan textos absurdos ----
        builder.Entity<Person>().Property(x => x.FullName).HasMaxLength(150);
        builder.Entity<Person>().Property(x => x.Dni).HasMaxLength(20);
        builder.Entity<Person>().Property(x => x.Type).HasMaxLength(20);
        builder.Entity<Subject>().Property(x => x.Name).HasMaxLength(100);
        builder.Entity<Course>().Property(x => x.Division).HasMaxLength(10);
        builder.Entity<ReportCardEntry>().Property(x => x.Valuation1).HasMaxLength(3);
        builder.Entity<ReportCardEntry>().Property(x => x.Valuation2).HasMaxLength(3);

        // ---- Relaciones ----
        // Cada cuenta pertenece a una persona; una persona tiene como mucho una cuenta.
        builder.Entity<ApplicationUser>()
            .HasOne(x => x.Person)
            .WithOne()
            .HasForeignKey<ApplicationUser>(x => x.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: no se puede borrar una persona que todavía tiene inscripciones o asignaciones.
        // En la práctica nunca borramos personas: las desactivamos (IsActive = false).
        builder.Entity<Enrollment>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TeachingAssignment>().HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PreceptorAssignment>().HasOne(x => x.Preceptor).WithMany().HasForeignKey(x => x.PreceptorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReportCardEntry>().HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
