namespace Briscola.Api.Configuration;

public sealed class MigrationOptions
{
    public const string SectionName = "Migrations";

    public bool RunOnStartup { get; set; }
}
