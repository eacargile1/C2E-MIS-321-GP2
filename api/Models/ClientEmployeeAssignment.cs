namespace C2E.Api.Models;

public sealed class ClientEmployeeAssignment
{
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public DateTime AssignedAtUtc { get; set; }
}
