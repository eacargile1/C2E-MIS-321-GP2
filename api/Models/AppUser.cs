namespace C2E.Api.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    /// <summary>Friendly name shown in the UI (not a unique login identifier).</summary>
    public string DisplayName { get; set; } = string.Empty;
    public required string PasswordHash { get; set; }
    public AppRole Role { get; set; } = AppRole.IC;
    public bool IsActive { get; set; } = true;

    /// <summary>Optional direct manager for expense approval routing (managers see only their team).</summary>
    public Guid? ManagerUserId { get; set; }
    public AppUser? Manager { get; set; }
    public ICollection<AppUser> DirectReports { get; set; } = new List<AppUser>();
    public ICollection<ClientEmployeeAssignment> ClientAssignments { get; set; } = new List<ClientEmployeeAssignment>();
    public ICollection<ProjectEmployeeAssignment> ProjectAssignments { get; set; } = new List<ProjectEmployeeAssignment>();
    public ICollection<UserSkill> Skills { get; set; } = new List<UserSkill>();
}
