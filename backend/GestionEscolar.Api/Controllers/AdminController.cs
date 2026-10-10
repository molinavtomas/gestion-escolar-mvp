using GestionEscolar.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionEscolar.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public sealed class AdminController(AppDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        if (!await IsAdmin()) return Forbid();
        return Ok(new
        {
            people = await db.People.CountAsync(x => x.IsActive),
            students = await db.People.CountAsync(x => x.IsActive && x.Type == PersonTypes.Student),
            teachers = await db.People.CountAsync(x => x.IsActive && x.Type == PersonTypes.Teacher),
            courses = await db.Courses.CountAsync(x => x.IsActive),
            publishedGrades = await db.Grades.CountAsync(x => x.IsPublished),
            pendingGrades = await db.Grades.CountAsync(x => !x.IsPublished)
        });
    }

    [HttpGet("people")]
    public async Task<IActionResult> People([FromQuery] string? search = null)
    {
        if (!await IsAdmin()) return Forbid();
        var query = db.People.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.Identifier.ToLower().Contains(term));
        }
        return Ok(await query.OrderBy(x => x.FullName).Select(x => new
        {
            x.Id, x.FullName, x.Identifier, x.Email, x.Phone, x.Type, x.IsActive,
            HasAccount = db.Users.Any(u => u.PersonId == x.Id)
        }).ToListAsync());
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog()
    {
        if (!await IsAdmin()) return Forbid();
        return Ok(new
        {
            personTypes = new[] { PersonTypes.Student, PersonTypes.Teacher, PersonTypes.Preceptor, PersonTypes.Responsible, PersonTypes.Staff },
            profiles = new[] { Profiles.Administration, Profiles.Teacher, Profiles.Preceptor, Profiles.Family },
            years = await db.SchoolYears.AsNoTracking().OrderByDescending(x => x.Year).ToListAsync(),
            terms = await db.Terms.AsNoTracking().OrderBy(x => x.StartDate).Select(x => new { x.Id, x.Name, x.StartDate, x.EndDate, x.AreAveragesPublished, x.IsReportCardPublished }).ToListAsync(),
            courses = await db.Courses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.Division).Select(x => new { x.Id, Label = x.Name + " " + x.Division }).ToListAsync(),
            subjects = await db.Subjects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(),
            students = await PeopleByType(PersonTypes.Student),
            teachers = await PeopleByType(PersonTypes.Teacher),
            preceptors = await PeopleByType(PersonTypes.Preceptor),
            responsibles = await PeopleByType(PersonTypes.Responsible),
            familyLinks = await db.FamilyLinks.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.ResponsibleId, Responsible = x.Responsible.FullName, x.StudentId, Student = x.Student.FullName, x.Relationship }).ToListAsync(),
            enrollments = await db.Enrollments.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.StudentId, Student = x.Student.FullName, x.CourseId, Course = x.Course.Name + " " + x.Course.Division }).ToListAsync(),
            teachingAssignments = await db.TeachingAssignments.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.TeacherId, Teacher = x.Teacher.FullName, x.CourseId, Course = x.Course.Name + " " + x.Course.Division, x.SubjectId, Subject = x.Subject.Name }).ToListAsync(),
            preceptorAssignments = await db.PreceptorAssignments.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.PreceptorId, Preceptor = x.Preceptor.FullName, x.CourseId, Course = x.Course.Name + " " + x.Course.Division }).ToListAsync()
        });
    }

    [HttpPost("people")]
    public async Task<IActionResult> CreatePerson(PersonRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Identifier)) return BadRequest(new { message = "Nombre e identificador son obligatorios." });
        if (await db.People.AnyAsync(x => x.Identifier == request.Identifier.Trim())) return Conflict(new { message = "Ya existe una persona con ese identificador." });

        var person = new Person
        {
            FullName = request.FullName.Trim(), Identifier = request.Identifier.Trim(), Email = Clean(request.Email), Phone = Clean(request.Phone), Type = request.Type, IsActive = true
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        if (request.CreateAccount)
        {
            if (string.IsNullOrWhiteSpace(person.Email)) return BadRequest(new { message = "Se necesita un email para crear la cuenta." });
            var result = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = person.Email, Email = person.Email, EmailConfirmed = true, DisplayName = person.FullName,
                Profile = request.Profile ?? ProfileFor(person.Type), PersonId = person.Id, IsActive = true
            }, request.Password ?? "Demo1234!");
            if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors.Select(x => x.Description)) });
        }

        return CreatedAtAction(nameof(People), new { id = person.Id }, new { person.Id });
    }

    [HttpPut("people/{id:int}")]
    public async Task<IActionResult> UpdatePerson(int id, PersonRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        person.FullName = request.FullName.Trim();
        person.Identifier = request.Identifier.Trim();
        person.Email = Clean(request.Email);
        person.Phone = Clean(request.Phone);
        person.Type = request.Type;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("people/{id:int}/toggle")]
    public async Task<IActionResult> TogglePerson(int id)
    {
        if (!await IsAdmin()) return Forbid();
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        person.IsActive = !person.IsActive;
        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == id);
        if (account is not null) account.IsActive = person.IsActive;
        await db.SaveChangesAsync();
        return Ok(new { person.IsActive });
    }

    [HttpPost("accounts/{personId:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int personId, ResetPasswordRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        var account = await db.Users.SingleOrDefaultAsync(x => x.PersonId == personId);
        if (account is null) return NotFound(new { message = "La persona no tiene cuenta." });
        var token = await userManager.GeneratePasswordResetTokenAsync(account);
        var result = await userManager.ResetPasswordAsync(account, token, request.Password);
        return result.Succeeded ? NoContent() : BadRequest(new { message = string.Join(" ", result.Errors.Select(x => x.Description)) });
    }

    [HttpPost("family-links")]
    public async Task<IActionResult> CreateFamilyLink(FamilyLinkRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        if (await db.FamilyLinks.AnyAsync(x => x.ResponsibleId == request.ResponsibleId && x.StudentId == request.StudentId)) return Conflict(new { message = "El vínculo ya existe." });
        db.FamilyLinks.Add(new FamilyLink { ResponsibleId = request.ResponsibleId, StudentId = request.StudentId, Relationship = request.Relationship.Trim() });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("enrollments")]
    public async Task<IActionResult> CreateEnrollment(EnrollmentRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        if (await db.Enrollments.AnyAsync(x => x.StudentId == request.StudentId && x.CourseId == request.CourseId)) return Conflict(new { message = "El alumno ya está inscripto en el curso." });
        db.Enrollments.Add(new Enrollment { StudentId = request.StudentId, CourseId = request.CourseId });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("teaching-assignments")]
    public async Task<IActionResult> CreateTeachingAssignment(TeachingAssignmentRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        if (await db.TeachingAssignments.AnyAsync(x => x.TeacherId == request.TeacherId && x.CourseId == request.CourseId && x.SubjectId == request.SubjectId)) return Conflict(new { message = "La asignación ya existe." });
        db.TeachingAssignments.Add(new TeachingAssignment { TeacherId = request.TeacherId, CourseId = request.CourseId, SubjectId = request.SubjectId });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("preceptor-assignments")]
    public async Task<IActionResult> CreatePreceptorAssignment(PreceptorAssignmentRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        if (await db.PreceptorAssignments.AnyAsync(x => x.PreceptorId == request.PreceptorId && x.CourseId == request.CourseId)) return Conflict(new { message = "La asignación ya existe." });
        db.PreceptorAssignments.Add(new PreceptorAssignment { PreceptorId = request.PreceptorId, CourseId = request.CourseId });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("terms/{id:int}/publication")]
    public async Task<IActionResult> UpdatePublication(int id, PublicationRequest request)
    {
        if (!await IsAdmin()) return Forbid();
        var term = await db.Terms.FindAsync(id);
        if (term is null) return NotFound();
        term.AreAveragesPublished = request.AreAveragesPublished;
        term.IsReportCardPublished = request.IsReportCardPublished;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> IsAdmin() => (await userManager.GetUserAsync(User))?.Profile == Profiles.Administration;
    private Task<List<NamedOption>> PeopleByType(string type) => db.People.AsNoTracking().Where(x => x.IsActive && x.Type == type).OrderBy(x => x.FullName).Select(x => new NamedOption(x.Id, x.FullName)).ToListAsync();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ProfileFor(string type) => type switch { PersonTypes.Teacher => Profiles.Teacher, PersonTypes.Preceptor => Profiles.Preceptor, PersonTypes.Responsible => Profiles.Family, _ => Profiles.Administration };
}

public sealed record PersonRequest(string FullName, string Identifier, string Type, string? Email, string? Phone, bool CreateAccount = false, string? Profile = null, string? Password = null);
public sealed record ResetPasswordRequest(string Password);
public sealed record FamilyLinkRequest(int ResponsibleId, int StudentId, string Relationship);
public sealed record EnrollmentRequest(int StudentId, int CourseId);
public sealed record TeachingAssignmentRequest(int TeacherId, int CourseId, int SubjectId);
public sealed record PreceptorAssignmentRequest(int PreceptorId, int CourseId);
public sealed record PublicationRequest(bool AreAveragesPublished, bool IsReportCardPublished);
public sealed record NamedOption(int Id, string Name);
