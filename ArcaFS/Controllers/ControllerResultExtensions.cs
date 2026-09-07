using Microsoft.AspNetCore.Mvc;

namespace ArcaFS.Controllers;

/// <summary>
/// Translates an <see cref="Ardalis.Result"/> status into the matching HTTP response.
/// </summary>
/// <remarks>
/// Written here rather than pulled in from <c>Ardalis.Result.AspNetCore</c> so the mapping —
/// in particular Forbidden staying a 403 with no body, which is what keeps a permission denial
/// from revealing whether the path exists — is visible and testable in this codebase.
/// </remarks>
public static class ControllerResultExtensions
{
    public static IActionResult FromResult<T>(this ControllerBase controller, Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Ok => controller.Ok(result.Value),
            ResultStatus.Created => controller.Ok(result.Value),
            ResultStatus.NoContent => controller.NoContent(),
            _ => controller.FromFailure(result),
        };

    public static IActionResult FromResult(this ControllerBase controller, Result result) =>
        result.Status switch
        {
            ResultStatus.Ok or ResultStatus.NoContent => controller.NoContent(),
            _ => controller.FromFailure(result),
        };

    private static IActionResult FromFailure(this ControllerBase controller, Ardalis.Result.IResult result) =>
        result.Status switch
        {
            ResultStatus.NotFound => controller.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = FirstMessage(result) ?? "The item was not found.",
            }),

            // No detail: a caller who may not read a path must not learn from the error whether
            // it exists, which a "not found" versus "forbidden" distinction would tell them.
            ResultStatus.Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden),

            ResultStatus.Unauthorized => controller.Unauthorized(),

            ResultStatus.Invalid => controller.BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage)),
            }),

            ResultStatus.Conflict => controller.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = FirstMessage(result) ?? "The item already exists.",
            }),

            ResultStatus.Unavailable => controller.StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail = FirstMessage(result),
                }),

            _ => controller.StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Error",
                Detail = FirstMessage(result) ?? "The operation could not be completed.",
            }),
        };

    private static string? FirstMessage(Ardalis.Result.IResult result) =>
        result.Errors.FirstOrDefault();
}
