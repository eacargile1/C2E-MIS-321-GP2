namespace C2E.Api.Models;

public sealed class Project
{
    public Guid Id { get; set; }

    public required string Name { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public decimal BudgetAmount { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ProjectEmployeeAssignment> EmployeeAssignments { get; set; } = new List<ProjectEmployeeAssignment>();
}
