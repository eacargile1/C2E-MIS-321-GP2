using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public sealed class ProjectResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public decimal BudgetAmount { get; init; }
    public bool IsActive { get; init; }
    public Guid? DeliveryManagerUserId { get; init; }
    public Guid? EngagementPartnerUserId { get; init; }
    public Guid? AssignedFinanceUserId { get; init; }
    public IReadOnlyList<Guid> TeamMemberUserIds { get; init; } = Array.Empty<Guid>();
}

public sealed class CreateProjectRequest
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    [Required]
    public Guid ClientId { get; init; }

    [Range(0, 100000000)]
    public decimal BudgetAmount { get; init; }

    public Guid? DeliveryManagerUserId { get; init; }
    public Guid? EngagementPartnerUserId { get; init; }
    public Guid? AssignedFinanceUserId { get; init; }

    /// <summary>Optional roster (ICs, others) for the engagement; does not drive approval routing.</summary>
    public List<Guid>? TeamMemberUserIds { get; init; }
}

public sealed class PatchProjectRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    public Guid? ClientId { get; init; }

    [Range(0, 100000000)]
    public decimal? BudgetAmount { get; init; }

    public bool? IsActive { get; init; }

    public Guid? DeliveryManagerUserId { get; init; }
    public Guid? EngagementPartnerUserId { get; init; }
    public Guid? AssignedFinanceUserId { get; init; }

    /// <summary>When true, clears <see cref="DeliveryManagerUserId"/> (wins over a new id in the same request).</summary>
    public bool ClearDeliveryManager { get; init; }

    /// <summary>When true, clears <see cref="EngagementPartnerUserId"/>.</summary>
    public bool ClearEngagementPartner { get; init; }

    /// <summary>When true, clears <see cref="AssignedFinanceUserId"/>.</summary>
    public bool ClearAssignedFinance { get; init; }

    /// <summary>When non-null, replaces the full team roster (may be empty).</summary>
    public List<Guid>? TeamMemberUserIds { get; init; }
}

/// <summary>Active users for project staffing pickers (role as string for JSON clients).</summary>
public sealed class ProjectStaffingUserResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
}

/// <summary>Expense register for a project (manager / partner / admin / assigned finance).</summary>
public sealed class ProjectExpenseInsightsResponse
{
    public required string ClientName { get; init; }
    public required string ProjectName { get; init; }
    public decimal BudgetAmount { get; init; }
    public int PendingCount { get; init; }
    public int ApprovedCount { get; init; }
    public int RejectedCount { get; init; }
    public decimal PendingAmount { get; init; }
    public decimal ApprovedAmount { get; init; }
    public decimal RejectedAmount { get; init; }
    public required IReadOnlyList<ProjectExpenseRowResponse> Expenses { get; init; }
}

public sealed class ProjectExpenseRowResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string SubmitterEmail { get; init; }
    public required string ExpenseDate { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
}
