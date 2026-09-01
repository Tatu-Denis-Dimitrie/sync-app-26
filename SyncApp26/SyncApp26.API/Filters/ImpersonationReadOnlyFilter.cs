using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Filters
{
    /// <summary>
    /// Global, fail-closed gate for view-only impersonation: any non-GET request on a token carrying
    /// the ImpersonatorId claim is refused, unless the action is explicitly marked
    /// [AllowDuringImpersonation]. Registered once in Program.cs (AddControllers(options => ...)), so
    /// it runs on every action — a new endpoint is blocked by default, not the other way around.
    ///
    /// IAuthorizationFilter, not an action filter: authorization filters are the first stage of the
    /// MVC pipeline, running before model binding. That means (a) it's the semantically correct stage
    /// for an authorization decision, (b) a large request body is never deserialized just to be
    /// thrown away, and (c) nothing downstream can run before this filter has had a chance to reject
    /// the request. [Authorize] itself already ran in ASP.NET Core's endpoint-routing authorization
    /// middleware by the time any MVC filter executes, so HttpContext.User is already populated here —
    /// this filter is a strictly-narrowing second gate, not a replacement for [Authorize].
    ///
    /// Known gaps, deliberately not fixed here: GET api/authentication/verify-email and
    /// GET api/DataChangeRequest/confirm-email mutate state behind a GET, so this verb-based filter
    /// lets them through — but both are [AllowAnonymous] token-consumption flows reachable without any
    /// session at all, so impersonation grants no capability the impersonator didn't already have by
    /// holding the emailed link. MVC filters also never run for SignalR hub invocations — SyncHub is
    /// broadcast-only today, so there is no write surface there to protect.
    /// </summary>
    public sealed class ImpersonationReadOnlyFilter : IAuthorizationFilter
    {
        public const string BlockedCode = "IMPERSONATION_READ_ONLY";

        private static readonly HashSet<string> ReadOnlyMethods =
            new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

        private readonly ILogger<ImpersonationReadOnlyFilter> _logger;

        public ImpersonationReadOnlyFilter(ILogger<ImpersonationReadOnlyFilter> logger)
        {
            _logger = logger;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // Presence of the claim is the signal — never its parseability. A malformed value must
            // still mean "impersonating", or a crafted/corrupted token could buy write access.
            if (context.HttpContext.User?.FindFirst(CustomClaimTypes.ImpersonatorId) is null)
            {
                return;
            }

            if (ReadOnlyMethods.Contains(context.HttpContext.Request.Method))
            {
                return;
            }

            if (context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringImpersonationAttribute>().Any())
            {
                return;
            }

            _logger.LogWarning(
                "Blocked write attempt during impersonation: {Method} {Path}.",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);

            // ObjectResult with an explicit StatusCode, not Forbid(): Forbid() delegates to the JWT
            // bearer handler's forbid path, which writes a bodyless 403 — the client needs a body
            // (the "code" field) to key its error handling off.
            var localizer = context.HttpContext.RequestServices.GetRequiredService<ILocalizationService>()
                .GetScopedLocalizer(LocalizationScopes.Auth);

            context.Result = new ObjectResult(new
            {
                code = BlockedCode,
                message = localizer["api.viewOnlyMode"].Value
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
