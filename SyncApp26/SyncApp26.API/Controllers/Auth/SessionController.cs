using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.API.Extensions;
using SyncApp26.API.Filters;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Controllers
{
    // Shares the api/authentication prefix with AuthenticationController/ImpersonationController -
    // no class-level [Authorize] here either, since every action states its own posture.
    [ApiController]
    [Route("api/Authentication")]
    public class SessionController : ControllerBase
    {
        // Just a hint to the browser - the JWT's own exp claim is what's actually enforced.
        private static readonly TimeSpan AccessTokenCookieLifetime = TimeSpan.FromMinutes(15);

        // The session's absolute cap - rotation never extends past what IssueAsync was given.
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(8);

        private readonly IUserService _userService;
        private readonly IImpersonationService _impersonationService;
        private readonly ITokenService _tokenService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAntiforgery _antiforgery;
        private readonly AuthCookieOptions _authCookieOptions;
        private readonly IStringLocalizer _localizer;

        public SessionController(
            IUserService userService,
            IImpersonationService impersonationService,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            IAntiforgery antiforgery,
            AuthCookieOptions authCookieOptions,
            ILocalizationService localizationService)
        {
            _userService = userService;
            _impersonationService = impersonationService;
            _tokenService = tokenService;
            _refreshTokenService = refreshTokenService;
            _antiforgery = antiforgery;
            _authCookieOptions = authCookieOptions;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Auth);
        }

        // Always 200, even with no session - a 401 here would loop the client's error interceptor
        // into logout() -> /login -> /me -> 401 again.
        [HttpGet("me")]
        [AllowAnonymous]
        public async Task<IActionResult> Me()
        {
            // Issued even for anonymous callers, who need a valid CSRF pairing before their first form submit.
            HttpContext.IssueXsrfCookie(_antiforgery, _authCookieOptions);

            if (User.GetUserId() is not { } userId)
            {
                return Ok(new { authenticated = false });
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return Ok(new { authenticated = false });
            }

            // Roles come from the signed token, not the DB, so the UI can't diverge from what the API authorizes.
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            object? impersonator = null;
            if (User.FindFirst(CustomClaimTypes.ImpersonatorId)?.Value is string impersonatorIdRaw &&
                Guid.TryParse(impersonatorIdRaw, out var impersonatorId))
            {
                var admin = await _userService.GetUserByIdAsync(impersonatorId);
                if (admin != null)
                {
                    impersonator = new
                    {
                        id = admin.Id,
                        email = admin.Email,
                        firstName = admin.FirstName,
                        lastName = admin.LastName,
                        roles = admin.RoleAssignments.Select(a => a.Role.Name)
                    };
                }
            }

            return Ok(new
            {
                authenticated = true,
                user = new
                {
                    id = user.Id,
                    email = user.Email,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    roles,
                    preferredLanguage = user.PreferredLanguage
                },
                impersonating = impersonator != null,
                impersonator
            });
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        [AllowDuringImpersonation]
        public async Task<IActionResult> Logout()
        {
            if (Request.Cookies.TryGetValue(AuthCookieExtensions.RefreshCookieName, out var refreshToken) &&
                !string.IsNullOrEmpty(refreshToken))
            {
                await _refreshTokenService.RevokeAsync(refreshToken);
            }

            Response.DeleteAuthCookie(_authCookieOptions);
            Response.DeleteRefreshCookie(_authCookieOptions);
            return Ok(new { message = _localizer["api.loggedOut"].Value });
        }

        // Rotates the refresh token and mints a fresh access token from it.
        [HttpPost("refresh")]
        [AllowAnonymous]
        [AllowDuringImpersonation]
        public async Task<IActionResult> Refresh()
        {
            if (!Request.Cookies.TryGetValue(AuthCookieExtensions.RefreshCookieName, out var rawToken) ||
                string.IsNullOrEmpty(rawToken))
            {
                return Unauthorized(new { message = _localizer["api.noRefreshToken"].Value });
            }

            var result = await _refreshTokenService.RotateAsync(rawToken);
            if (result.Outcome != RefreshOutcome.Success || result.UserId is not { } userId || result.Token is not { } newRefreshToken)
            {
                // Whatever the caller presented is no longer valid - the cookie must go with it.
                Response.DeleteRefreshCookie(_authCookieOptions);
                return Unauthorized(new { message = _localizer["api.refreshTokenInvalid"].Value });
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                Response.DeleteRefreshCookie(_authCookieOptions);
                return Unauthorized(new { message = _localizer["api.refreshTokenInvalid"].Value });
            }

            var roleNames = user.RoleAssignments.Select(a => a.Role.Name).ToList();
            var newAccessToken = await _tokenService.GenerateTokenAsync(userId, user.Email, roleNames);

            Response.AppendAuthCookie(_authCookieOptions, newAccessToken, AccessTokenCookieLifetime);
            Response.AppendRefreshCookie(_authCookieOptions, newRefreshToken.RawToken, newRefreshToken.ExpiresAt);

            return Ok(new { message = _localizer["api.sessionRefreshed"].Value });
        }

        [HttpPost("stop-impersonation")]
        [Authorize]
        [AllowDuringImpersonation]
        public async Task<IActionResult> StopImpersonation()
        {
            if (User.FindFirst(CustomClaimTypes.ImpersonatorId)?.Value is not string raw ||
                !Guid.TryParse(raw, out var impersonatorId))
            {
                return BadRequest(new { message = _localizer["api.notImpersonating"].Value });
            }

            var result = await _impersonationService.StopAsync(impersonatorId);

            return result.Status switch
            {
                ImpersonationStatus.ImpersonatorNotFound => Unauthorized(new { message = _localizer["api.impersonatorSessionGone"].Value }),
                ImpersonationStatus.ImpersonatorNotAdmin => Unauthorized(new { message = _localizer["api.impersonatorNotAdmin"].Value }),
                ImpersonationStatus.Success => await StopImpersonationSuccess(result),
                _ => StatusCode(500, new { message = _localizer["api.genericError"].Value })
            };
        }

        // Resuming the admin's own identity is a real session, so it gets a refresh token too.
        private async Task<IActionResult> StopImpersonationSuccess(ImpersonationResult result)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, AccessTokenCookieLifetime);

            var refreshToken = await _refreshTokenService.IssueAsync(result.UserId, DateTime.UtcNow.Add(RefreshTokenLifetime));
            Response.AppendRefreshCookie(_authCookieOptions, refreshToken.RawToken, refreshToken.ExpiresAt);

            return Ok(new
            {
                message = _localizer["api.impersonationEnded"].Value,
                user = new
                {
                    id = result.UserId,
                    email = result.Email,
                    firstName = result.FirstName,
                    lastName = result.LastName,
                    roles = result.Roles
                }
            });
        }
    }
}
