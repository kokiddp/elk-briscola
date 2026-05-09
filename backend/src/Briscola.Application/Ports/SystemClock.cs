using System.Diagnostics.CodeAnalysis;

namespace Briscola.Application.Ports;

[ExcludeFromCodeCoverage]
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
