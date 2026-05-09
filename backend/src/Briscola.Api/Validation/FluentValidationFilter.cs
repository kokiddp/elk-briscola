using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Briscola.Api.Validation;

/// <summary>
/// Resolves an <see cref="IValidator{T}"/> for every action argument that
/// has one registered, runs it, and throws <see cref="ValidationException"/>
/// on failure (which the global ProblemDetails handler turns into a 422).
/// This is the modern replacement for the archived
/// <c>FluentValidation.AspNetCore</c> auto-validation package.
/// </summary>
public sealed class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        IServiceProvider sp = context.HttpContext.RequestServices;
        foreach (object? argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            Type validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            object? validator = sp.GetService(validatorType);
            if (validator is null)
            {
                continue;
            }

            ValidationContext<object> vctx = new(argument);
            FluentValidation.Results.ValidationResult result = await ((IValidator)validator)
                .ValidateAsync(vctx, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        await next().ConfigureAwait(false);
    }
}
