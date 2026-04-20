namespace C2E.Api.Models;

public sealed class ProjectEmployeeAssignment
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public DateTime AssignedAtUtc { get; set; }
}
