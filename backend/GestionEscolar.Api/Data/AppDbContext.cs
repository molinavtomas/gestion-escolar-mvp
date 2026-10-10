using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<FamilyLink> FamilyLinks => Set<FamilyLink>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TeachingAssignment> TeachingAssignments => Set<TeachingAssignment>();
    public DbSet<PreceptorAssignment> PreceptorAssignments => Set<PreceptorAssignment>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<GradeHistory> GradeHistory => Set<GradeHistory>();
    public DbSet<Attendance> Attendances => Set<Attendance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Person>().HasIndex(x => x.Identifier).IsUnique();
        builder.Entity<FamilyLink>().HasIndex(x => new { x.ResponsibleId, x.StudentId }).IsUnique();
        builder.Entity<Enrollment>().HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();
        builder.Entity<TeachingAssignment>().HasIndex(x => new { x.TeacherId, x.CourseId, x.SubjectId }).IsUnique();
        builder.Entity<PreceptorAssignment>().HasIndex(x => new { x.PreceptorId, x.CourseId }).IsUnique();
        builder.Entity<Grade>().HasIndex(x => new { x.EvaluationId, x.EnrollmentId }).IsUnique();
        builder.Entity<Attendance>().HasIndex(x => new { x.EnrollmentId, x.Date }).IsUnique();

        builder.Entity<ApplicationUser>()
            .HasOne(x => x.Person)
            .WithOne()
            .HasForeignKey<ApplicationUser>(x => x.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<FamilyLink>().HasOne(x => x.Responsible).WithMany().HasForeignKey(x => x.ResponsibleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FamilyLink>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Enrollment>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TeachingAssignment>().HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PreceptorAssignment>().HasOne(x => x.Preceptor).WithMany().HasForeignKey(x => x.PreceptorId).OnDelete(DeleteBehavior.Restrict);
    }
}
