namespace C2E.Api.Models;

public sealed class Project
{
    public Guid Id { get; set; }

    public required string Name { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public decimal BudgetAmount { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Delivery manager who approves IC time/expenses booked to this project (tier IC → Manager).</summary>
    public Guid? DeliveryManagerUserId { get; set; }
    public AppUser? DeliveryManager { get; set; }

    /// <summary>Engagement partner who approves manager time/expenses for this project (tier Manager → Partner).</summary>
    public Guid? EngagementPartnerUserId { get; set; }
    public AppUser? EngagementPartner { get; set; }

    /// <summary>Finance lead assigned to this project (full budget visibility; may update budget only).</summary>
    public Guid? AssignedFinanceUserId { get; set; }
    public AppUser? AssignedFinanceUser { get; set; }

    public ICollection<ProjectTeamMember> TeamMembers { get; set; } = new List<ProjectTeamMember>();

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ProjectEmployeeAssignment> EmployeeAssignments { get; set; } = new List<ProjectEmployeeAssignment>();

    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
}
