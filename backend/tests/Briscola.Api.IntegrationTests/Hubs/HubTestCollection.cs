namespace Briscola.Api.IntegrationTests.Hubs;

/// <summary>
/// Marker for the xUnit collection that serializes hub tests. SignalR's
/// negotiate handshake against the in-memory <c>TestServer</c> occasionally
/// 500s when many handshakes overlap; serializing the hub-test classes
/// keeps the suite green without slowing it down meaningfully (each test
/// is sub-second).
/// </summary>
[CollectionDefinition(HubTests.Name, DisableParallelization = true)]
public sealed class HubTests
{
    public const string Name = "hub-tests";
}
