using System.Text.RegularExpressions;
using Briscola.Api.Dtos;
using FluentValidation;

namespace Briscola.Api.Validation;

internal static partial class UsernameRegex
{
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{3,32}$", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Must(u => UsernameRegex.Pattern().IsMatch(u))
            .WithMessage("Username must be 3-32 chars, alphanumeric, '_' or '-'.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10)
            .Must(p => p.Any(char.IsLetter)).WithMessage("Password must contain at least one letter.")
            .Must(p => p.Any(char.IsDigit)).WithMessage("Password must contain at least one digit.");
        RuleFor(x => x.DisplayName)
            .Must(d => string.IsNullOrEmpty(d) || (d.Length is >= 1 and <= 32))
            .WithMessage("DisplayName must be 1-32 characters.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UsernameOrEmail).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .Must(p => p.Any(char.IsLetter)).WithMessage("Password must contain at least one letter.")
            .Must(p => p.Any(char.IsDigit)).WithMessage("Password must contain at least one digit.");
    }
}
