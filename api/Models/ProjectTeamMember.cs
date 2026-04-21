namespace C2E.Api.Models;

/// <summary>Optional roster of people associated with the engagement (ICs, delivery leadership, etc.).</summary>
public sealed class ProjectTeamMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
}
