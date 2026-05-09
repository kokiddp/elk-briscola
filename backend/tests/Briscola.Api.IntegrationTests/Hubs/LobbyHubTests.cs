using Briscola.Api.Dtos;
using Microsoft.AspNetCore.SignalR.Client;

namespace Briscola.Api.IntegrationTests.Hubs;

[Collection(HubTests.Name)]
public sealed class LobbyHubTests : HubTestHarness
{
    [Fact]
    public async Task Authenticated_user_can_connect_subscribe_and_disconnect()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        HubConnection hub = BuildHubConnection("/hubs/lobby", alice.AccessToken);
        await hub.StartAsync();

        await hub.InvokeAsync("SubscribeOpen");
        await hub.InvokeAsync("UnsubscribeOpen");

        await hub.DisposeAsync();
    }

    [Fact]
    public async Task Chat_round_trip_broadcasts_to_subscribers()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        TokenResponse bob = await RegisterAndLoginAsync("bob");

        HubConnection aliceHub = BuildHubConnection("/hubs/lobby", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/lobby", bob.AccessToken);

        TaskCompletionSource<LobbyChatMessageDto> bobReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bobHub.On<LobbyChatMessageDto>("ChatMessage", msg => bobReceived.TrySetResult(msg));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        await aliceHub.InvokeAsync("SubscribeOpen");
        await bobHub.InvokeAsync("SubscribeOpen");

        await aliceHub.InvokeAsync("SendChat", "hello world");

        LobbyChatMessageDto delivered = await bobReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        delivered.Text.Should().Be("hello world");
        delivered.FromUserName.Should().Be("alice");

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Sender_also_receives_their_own_chat_echo()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        HubConnection hub = BuildHubConnection("/hubs/lobby", alice.AccessToken);

        TaskCompletionSource<LobbyChatMessageDto> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<LobbyChatMessageDto>("ChatMessage", msg => received.TrySetResult(msg));

        await hub.StartAsync();
        await hub.InvokeAsync("SubscribeOpen");
        await hub.InvokeAsync("SendChat", "echo");

        LobbyChatMessageDto echoed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        echoed.Text.Should().Be("echo");

        await hub.DisposeAsync();
    }

    [Fact]
    public async Task Chat_text_is_truncated_at_500_chars()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        HubConnection hub = BuildHubConnection("/hubs/lobby", alice.AccessToken);

        TaskCompletionSource<LobbyChatMessageDto> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<LobbyChatMessageDto>("ChatMessage", msg => received.TrySetResult(msg));

        await hub.StartAsync();
        await hub.InvokeAsync("SubscribeOpen");
        await hub.InvokeAsync("SendChat", new string('x', 600));

        LobbyChatMessageDto echoed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        echoed.Text.Length.Should().Be(500);

        await hub.DisposeAsync();
    }

    [Fact]
    public async Task Whitespace_chat_is_dropped()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        HubConnection hub = BuildHubConnection("/hubs/lobby", alice.AccessToken);

        bool received = false;
        hub.On<LobbyChatMessageDto>("ChatMessage", _ => { received = true; });

        await hub.StartAsync();
        await hub.InvokeAsync("SubscribeOpen");
        await hub.InvokeAsync("SendChat", "   ");

        await Task.Delay(200);
        received.Should().BeFalse();

        await hub.DisposeAsync();
    }
}
