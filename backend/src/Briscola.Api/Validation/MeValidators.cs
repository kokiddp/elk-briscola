using Briscola.Api.Dtos;
using FluentValidation;

namespace Briscola.Api.Validation;

public sealed class MePatchRequestValidator : AbstractValidator<MePatchRequest>
{
    public MePatchRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .Must(d => d is null || (d.Length is >= 1 and <= 32))
            .WithMessage("DisplayName must be 1-32 characters.");
        RuleFor(x => x.ActiveCardSetId)
            .Must(id => id is null || (id.Length is >= 1 and <= 64))
            .WithMessage("ActiveCardSetId must be 1-64 characters.");
    }
}
