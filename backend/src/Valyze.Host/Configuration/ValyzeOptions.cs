using Valyze.Domain.Enum;

namespace Valyze.Host.Configuration;

public sealed class ValyzeOptions
{
    public const string SectionName = "Valyze";

    public ValyzeMode Mode { get; init; } = ValyzeMode.Personal;
    public PersonalOptions Personal { get; init; } = new();
    public CorsOptions Cors { get; init; } = new();
}

public sealed class PersonalOptions
{
    public string SeedEmail { get; init; } = "owner@valyze.local";
    public string BaseCurrency { get; init; } = "EUR";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; init; } = [];
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "valyze";
    public string Audience { get; init; } = "valyze";
    public string SigningKey { get; init; } = "";
    public int ExpiryHours { get; init; } = 12;
}
