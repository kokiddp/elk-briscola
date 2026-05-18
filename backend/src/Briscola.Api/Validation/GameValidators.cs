using Briscola.Api.Dtos;
using FluentValidation;

namespace Briscola.Api.Validation;

public sealed class CreateGameRequestValidator : AbstractValidator<CreateGameRequestDto>
{
    public CreateGameRequestValidator()
    {
        // Game names are an internal-only field — the UI no longer
        // collects them. Accept null / empty; cap any caller-supplied
        // value at 64 chars defensively.
        RuleFor(x => x.Name)
            .MaximumLength(64)
            .When(x => !string.IsNullOrEmpty(x.Name));
        RuleFor(x => x.Mode).IsInEnum();
        When(x => x.IsPrivate, () =>
        {
            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(4)
                .MaximumLength(64);
        });
    }
}

public sealed class JoinGameRequestValidator : AbstractValidator<JoinGameRequestDto>
{
    public JoinGameRequestValidator()
    {
        // Password is optional; only validated against the game's hash at the
        // service layer. We don't reject empty here because public games take
        // no password.
        RuleFor(x => x.Password)
            .MaximumLength(64)
            .When(x => x.Password is not null);
    }
}
