namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Shared scaffolding for REST integration tests. Each test class owns
/// its own <see cref="BriscolaApiFactory"/> (i.e. its own in-memory DB)
/// so tests don't see each other's users/games. xUnit calls
/// <see cref="IAsyncLifetime"/> for setup/teardown; the factory itself
/// is disposed via <see cref="IDisposable"/>.
/// </summary>
public abstract class RestTestBase : IAsyncLifetime, IDisposable
{
    protected BriscolaApiFactory Factory { get; } = new();

    public Task InitializeAsync() => Factory.InitializeAsync();

    public async Task DisposeAsync() => await Factory.DisposeAsync().ConfigureAwait(false);

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
