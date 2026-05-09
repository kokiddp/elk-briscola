namespace Briscola.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "BriscolaCors";

    public string[] AllowedOrigins { get; set; } = [];
}
