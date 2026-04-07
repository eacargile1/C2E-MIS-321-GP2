namespace C2E.Api.Models;

public sealed class Client
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    /// <summary>Default hourly billing rate for this client (PRD FR32). Visible only to Admin/Finance (domain privacy rules).</summary>
    public decimal? DefaultBillingRate { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
