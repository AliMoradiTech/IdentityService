namespace idnetityServiceWedApi.Contracts;

public sealed record UserRegisteredEvent(Guid EventId, Guid UserId, string Email, string UserName, string? DeviceToken, DateTimeOffset OccurredAt);
