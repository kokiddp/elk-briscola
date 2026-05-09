using System.Diagnostics.CodeAnalysis;

namespace Briscola.Application.Ports;

[ExcludeFromCodeCoverage]
public sealed class SystemRandomSourceFactory : IRandomSourceFactory
{
    public IRandomSource Create() => new SystemRandomSource(Random.Shared.NextInt64());
}
