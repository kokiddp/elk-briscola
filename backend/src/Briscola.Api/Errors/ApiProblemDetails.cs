using Briscola.Application.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Errors;

/// <summary>
/// Maps known application exceptions onto stable ProblemDetails responses.
/// Wired via <c>UseExceptionHandler</c> in <c>Program.cs</c>; the global
/// handler runs before MVC's filter pipeline so it catches both controller
/// and middleware exceptions.
/// </summary>
public static class ApiProblemDetails
{
    public const string TypeBase = "https://briscola.example/errors/";

    public static async Task WriteAsync(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        IExceptionHandlerFeature? feature = ctx.Features.Get<IExceptionHandlerFeature>();
        Exception? error = feature?.Error;
        ProblemDetails details = error switch
        {
            ValidationException ve => Validation(ve),
            GameNotFoundException nf => NotFound(nf),
            LobbyConflictException lc => Conflict("LobbyConflict", lc.Message),
            ConcurrencyConflictException cc => Conflict("ConcurrencyConflict", cc.Message),
            InvalidPasswordException ip => BadRequest("InvalidPassword", ip.Message),
            BriscolaApplicationException ax => BadRequest("ApplicationError", ax.Message),
            _ => Generic(error),
        };

        ctx.Response.StatusCode = details.Status ?? StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync<object>(details).ConfigureAwait(false);
    }

    private static ValidationProblemDetails Validation(ValidationException ve)
    {
        ValidationProblemDetails details = new(
            ve.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
        {
            Title = "Validation failed.",
            Status = StatusCodes.Status422UnprocessableEntity,
            Type = TypeBase + "validation",
        };
        details.Extensions["code"] = "ValidationFailed";
        return details;
    }

    private static ProblemDetails NotFound(GameNotFoundException ex) =>
        Problem(
            type: TypeBase + "game-not-found",
            title: "Game not found.",
            status: StatusCodes.Status404NotFound,
            code: "GameNotFound",
            detail: ex.Message);

    private static ProblemDetails Conflict(string code, string detail) =>
        Problem(
            type: TypeBase + code.ToLowerInvariant(),
            title: "Conflict.",
            status: StatusCodes.Status409Conflict,
            code: code,
            detail: detail);

    private static ProblemDetails BadRequest(string code, string detail) =>
        Problem(
            type: TypeBase + code.ToLowerInvariant(),
            title: "Bad request.",
            status: StatusCodes.Status400BadRequest,
            code: code,
            detail: detail);

    private static ProblemDetails Generic(Exception? error) =>
        Problem(
            type: TypeBase + "internal",
            title: "Internal server error.",
            status: StatusCodes.Status500InternalServerError,
            code: "Internal",
            detail: error?.Message ?? "Unknown error.");

    private static ProblemDetails Problem(string type, string title, int status, string code, string detail)
    {
        ProblemDetails details = new()
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
        };
        details.Extensions["code"] = code;
        return details;
    }
}
