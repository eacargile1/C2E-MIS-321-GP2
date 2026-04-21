namespace C2E.Api.Models;

public sealed class UserSkill
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
