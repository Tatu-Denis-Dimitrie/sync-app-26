using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.API.Extensions;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Controllers
{
    // Separate from AuthenticationController on purpose: that controller has no class-level
    // [Authorize] at all (every action there states its own posture, mostly [AllowAnonymous]), so an
    // action-level exception there would be an easy-to-miss anonymity default for the next person who
    // copies an action into that file. Stating "Admin-only" once at the class level here is the
    // fail-closed idiom already used by RolesController.
    [ApiController]
    [Route("api/Authentication")]
    [Authorize(Roles = Roles.Admin)]
    public class ImpersonationController : ControllerBase
    {
        private static readonly TimeSpan ImpersonationCookieLifetime = TimeSpan.FromMinutes(30);

        private readonly IImpersonationService _impersonationService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly AuthCookieOptions _authCookieOptions;
        private readonly IStringLocalizer _localizer;

        public ImpersonationController(
            IImpersonationService impersonationService, IRefreshTokenService refreshTokenService, AuthCookieOptions authCookieOptions,
            ILocalizationService localizationService)
        {
            _impersonationService = impersonationService;
            _refreshTokenService = refreshTokenService;
            _authCookieOptions = authCookieOptions;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Auth);
        }

        [HttpPost("impersonate/{userId:guid}")]
        public async Task<IActionResult> Impersonate(Guid userId)
        {
            if (User.GetUserId() is not { } adminId)
            {
                return Unauthorized();
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var result = await _impersonationService.StartAsync(adminId, userId, ipAddress);

            return result.Status switch
            {
                ImpersonationStatus.TargetNotFound => NotFound(new { message = _localizer["api.userNotFound"].Value }),
                ImpersonationStatus.TargetIsAdmin => StatusCode(403, new { message = _localizer["api.cannotViewAsAdmin"].Value }),
                ImpersonationStatus.SelfImpersonation => BadRequest(new { message = _localizer["api.cannotViewAsSelf"].Value }),
                ImpersonationStatus.Success => await ImpersonationSuccess(result, adminId),
                _ => StatusCode(500, new { message = _localizer["api.genericError"].Value })
            };
        }

        // Impersonation sessions don't get a refresh token, so the admin's own is revoked here -
        // otherwise it would silently outlive the 30-minute access token.
        private async Task<IActionResult> ImpersonationSuccess(ImpersonationResult result, Guid adminId)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, ImpersonationCookieLifetime);

            await _refreshTokenService.RevokeAllForUserAsync(adminId);
            Response.DeleteRefreshCookie(_authCookieOptions);

            return Ok(new
            {
                message = _localizer["api.impersonationStarted"].Value,
                user = new
                {
                    id = result.UserId,
                    email = result.Email,
                    firstName = result.FirstName,
                    lastName = result.LastName,
                    roles = result.Roles
                },
                impersonating = true
            });
        }
    }
}
