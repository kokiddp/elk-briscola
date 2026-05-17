using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Briscola.Api.Dtos;
using Briscola.Api.IntegrationTests.Rest;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Briscola.Api.IntegrationTests.Hubs;

/// <summary>
/// Builds <see cref="HubConnection"/> instances that pipe through the
/// in-memory <see cref="TestServer"/> exposed by
/// <see cref="BriscolaApiFactory"/>. The harness owns user creation and
/// JWT acquisition so per-test setup stays one liner.
/// </summary>
public abstract class HubTestHarness : RestTestBase
{
    public async Task<TokenResponse> RegisterAndLoginAsync(string username)
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(username, $"{username}@example.com", "Strong-Pass-123", username));
        register.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(username, "Strong-Pass-123"));
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        return (await login.Content.ReadFromJsonAsync<TokenResponse>(TestJsonOptions.Default))!;
    }

    /// <summary>
    /// Builds an authenticated <see cref="HubConnection"/> piped through the
    /// in-memory test server. Caller is responsible for <c>StartAsync</c>
    /// and <c>DisposeAsync</c>.
    ///
    /// The hub's JSON protocol on the server side is configured for
    /// camelCase + <see cref="JsonStringEnumConverter"/> (matches the
    /// AsyncAPI contract and the Angular client). The default <c>HubConnection</c>
    /// client uses unmodified <c>JsonSerializerOptions</c>, so payloads
    /// containing enum-typed fields (e.g. the <c>Phase</c> on
    /// <see cref="RedactedStateForUserDto"/>) silently fail to deserialize
    /// and the test's <c>On&lt;T&gt;</c> handler never fires. Adding the
    /// matching converter here keeps the test wire format aligned.
    /// </summary>
    public HubConnection BuildHubConnection(string hubPath, string accessToken)
    {
        TestServer server = Factory.Server;
        IHubConnectionBuilder builder = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, hubPath), opts =>
            {
                opts.HttpMessageHandlerFactory = _ => server.CreateHandler();
                opts.Transports = HttpTransportType.LongPolling;
                opts.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                // SignalR's default header forwarding doesn't pick up our
                // bearer token in the in-memory transport — set it on the
                // negotiate request explicitly so the JWT validator on the
                // negotiate endpoint accepts the connection.
                opts.Headers["Authorization"] = $"Bearer {accessToken}";
            });
        // Mirror the server's JsonHubProtocol config (camelCase + enum
        // strings, set in Program.cs). The default client protocol uses
        // PascalCase + numeric enums, which silently fails to deserialize
        // typed payloads like RedactedStateForUserDto and makes any
        // `On<T>("...", handler)` handler never fire.
        builder.Services.Configure<JsonHubProtocolOptions>(opts =>
        {
            opts.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        return builder.Build();
    }
}
