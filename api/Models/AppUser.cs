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
}
