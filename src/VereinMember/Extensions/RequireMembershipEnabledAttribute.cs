using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace VereinMember.Extensions;

/// <summary>
/// Enforces <see cref="ServiceCollectionExtensions.Enabled"/> on server-side
/// controllers and Razor Page handlers (not only during view rendering).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireMembershipEnabledAttribute : Attribute, IAsyncActionFilter, IAsyncPageFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!ServiceCollectionExtensions.Enabled)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Membership module disabled",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "Die Mitgliederverwaltung ist derzeit deaktiviert."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        await next();
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!ServiceCollectionExtensions.Enabled)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        await next();
    }
}
