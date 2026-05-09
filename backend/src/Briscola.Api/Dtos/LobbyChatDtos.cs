using System.Diagnostics.CodeAnalysis;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record LobbyChatMessageDto(
    Guid Id,
    Guid FromUserId,
    string FromUserName,
    string Text,
    DateTimeOffset CreatedAt);
