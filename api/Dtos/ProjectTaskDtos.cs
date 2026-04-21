namespace C2E.Api.Dtos;

public sealed class ProjectTaskResponse
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public required string ClientName { get; init; }
    public required string ProjectName { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> RequiredSkills { get; init; }
    public string? DueDate { get; init; }
    public Guid? AssignedUserId { get; init; }
    public string? AssignedEmail { get; init; }
    public required string Status { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class CreateProjectTaskRequest
{
    public Guid ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public List<string>? RequiredSkills { get; init; }
    public string? DueDate { get; init; }
    public Guid? AssignedUserId { get; init; }
}

public sealed class PatchProjectTaskRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public List<string>? RequiredSkills { get; init; }
    public string? DueDate { get; init; }
    public Guid? AssignedUserId { get; init; }
    public bool ClearAssignedUser { get; init; }
    public string? Status { get; init; }
}
