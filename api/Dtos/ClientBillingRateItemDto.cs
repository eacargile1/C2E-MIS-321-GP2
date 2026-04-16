namespace C2E.Api.Dtos;

public sealed class ClientBillingRateItemDto
{
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public decimal? DefaultHourlyRate { get; init; }
}
