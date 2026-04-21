namespace C2E.Api.Models;

public sealed class PtoRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;

    public PtoRequestStatus Status { get; set; }

    /// <summary>Primary approver (IC → org manager; Manager/Finance → reporting partner).</summary>
    public Guid ApproverUserId { get; set; }
    public AppUser Approver { get; set; } = null!;

    /// <summary>Optional second approver (IC → reporting partner when set; Manager/Finance → org manager when set). Either may approve or reject.</summary>
    public Guid? SecondaryApproverUserId { get; set; }
    public AppUser? SecondaryApprover { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }
}
