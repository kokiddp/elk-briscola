using System.Diagnostics.CodeAnalysis;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Ports;

[ExcludeFromCodeCoverage]
public sealed class SystemRandomSourceFactory : IRandomSourceFactory
{
    public IRandomSource Create() => new SeededRandomSource(Random.Shared.NextInt64());
}
