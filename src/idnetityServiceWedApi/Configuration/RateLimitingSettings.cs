namespace idnetityServiceWedApi.Configuration;

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;
    public int RegisterPermitLimit { get; init; } = 20;
    public int TokenPermitLimit { get; init; } = 30;
    public int WindowSeconds { get; init; } = 60;
}
