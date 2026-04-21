namespace C2E.Api.Models;

/// <summary>Delivery staffing task on an active project (skills drive recommendations).</summary>
public sealed class ProjectTask
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Comma-separated skills (normalized when saving) for recommendation matching.</summary>
    public string RequiredSkills { get; set; } = "";

    public DateOnly? DueDate { get; set; }

    public Guid? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Open;

    public Guid CreatedByUserId { get; set; }
    public AppUser CreatedBy { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
