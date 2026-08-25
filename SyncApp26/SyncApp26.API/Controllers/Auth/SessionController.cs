using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.API.Extensions;
using SyncApp26.API.Filters;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Controllers
{
    // Shares the api/authentication route prefix with AuthenticationController and
    // ImpersonationController on purpose (same reasoning as ImpersonationController's own comment):
    // no class-level [Authorize] here either, since every action below has its own posture.
    [ApiController]
    [Route("api/Authentication")]
    public class SessionController : ControllerBase
    {
        // The cookie's own Expires is just a hint to the browser for when to stop sending it - the
        // JWT's signed exp claim (TokenService.AccessTokenMinutes) is what's actually enforced.
        private static readonly TimeSpan AccessTokenCookieLifetime = TimeSpan.FromMinutes(15);

        // The session's absolute cap: RefreshTokenService.RotateAsync never extends ExpiresAt past
        // what IssueAsync is given here, so this number is the real "how long can a session last".
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(8);

        private readonly IUserService _userService;
        private readonly IImpersonationService _impersonationService;
        private readonly ITokenService _tokenService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAntiforgery _antiforgery;
        private readonly AuthCookieOptions _authCookieOptions;

        public SessionController(
            IUserService userService,
            IImpersonationService impersonationService,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            IAntiforgery antiforgery,
            AuthCookieOptions authCookieOptions)
        {
            _userService = userService;
            _impersonationService = impersonationService;
            _tokenService = tokenService;
            _refreshTokenService = refreshTokenService;
            _antiforgery = antiforgery;
            _authCookieOptions = authCookieOptions;
        }

        // Always 200, even when there's no session - a 401 here would send the client's error
        // interceptor into logout(), which redirects to /login, which calls /me again: an infinite
        // reload loop on the login page itself.
        [HttpGet("me")]
        [AllowAnonymous]
        public async Task<IActionResult> Me()
        {
            // Issued unconditionally, even for an anonymous caller - a first-time visitor needs a
            // valid CSRF pairing in place before they ever submit a form (login, register, ...).
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

            // Roles come from the signed token, not the DB: a stale or tampered claim can't survive
            // signature verification, so the UI's view of roles can never diverge from what the API
            // will actually authorize.
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
                    roles
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
            return Ok(new { message = "Logged out." });
        }

        // Rotates the refresh token and mints a fresh access token from it - the client calls this
        // when the 15-minute access token expires, so the user never has to re-enter credentials
        // until the refresh token itself hits its 8h absolute cap (or gets revoked).
        [HttpPost("refresh")]
        [AllowAnonymous]
        [AllowDuringImpersonation]
        public async Task<IActionResult> Refresh()
        {
            if (!Request.Cookies.TryGetValue(AuthCookieExtensions.RefreshCookieName, out var rawToken) ||
                string.IsNullOrEmpty(rawToken))
            {
                return Unauthorized(new { message = "No refresh token present." });
            }

            var result = await _refreshTokenService.RotateAsync(rawToken);
            if (result.Outcome != RefreshOutcome.Success || result.UserId is not { } userId || result.Token is not { } newRefreshToken)
            {
                // Whatever the caller presented is no longer valid - the cookie must go with it.
                Response.DeleteRefreshCookie(_authCookieOptions);
                return Unauthorized(new { message = "Refresh token is invalid or expired." });
            }

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                Response.DeleteRefreshCookie(_authCookieOptions);
                return Unauthorized(new { message = "Refresh token is invalid or expired." });
            }

            var roleNames = user.RoleAssignments.Select(a => a.Role.Name).ToList();
            var newAccessToken = await _tokenService.GenerateTokenAsync(userId, user.Email, roleNames);

            Response.AppendAuthCookie(_authCookieOptions, newAccessToken, AccessTokenCookieLifetime);
            Response.AppendRefreshCookie(_authCookieOptions, newRefreshToken.RawToken, newRefreshToken.ExpiresAt);

            return Ok(new { message = "Session refreshed." });
        }

        [HttpPost("stop-impersonation")]
        [Authorize]
        [AllowDuringImpersonation]
        public async Task<IActionResult> StopImpersonation()
        {
            if (User.FindFirst(CustomClaimTypes.ImpersonatorId)?.Value is not string raw ||
                !Guid.TryParse(raw, out var impersonatorId))
            {
                return BadRequest(new { message = "Not currently impersonating." });
            }

            var result = await _impersonationService.StopAsync(impersonatorId);

            return result.Status switch
            {
                ImpersonationStatus.ImpersonatorNotFound => Unauthorized(new { message = "Your original session no longer exists. Please log in again." }),
                ImpersonationStatus.ImpersonatorNotAdmin => Unauthorized(new { message = "Your original session no longer has admin access. Please log in again." }),
                ImpersonationStatus.Success => await StopImpersonationSuccess(result),
                _ => StatusCode(500, new { message = "An error occurred while processing your request." })
            };
        }

        // Resuming the admin's own identity is a real session again, so - unlike the impersonation
        // token it replaces - it gets a refresh token too.
        private async Task<IActionResult> StopImpersonationSuccess(ImpersonationResult result)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, AccessTokenCookieLifetime);

            var refreshToken = await _refreshTokenService.IssueAsync(result.UserId, DateTime.UtcNow.Add(RefreshTokenLifetime));
            Response.AppendRefreshCookie(_authCookieOptions, refreshToken.RawToken, refreshToken.ExpiresAt);

            return Ok(new
            {
                message = "Impersonation session ended.",
                token = result.Token,
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
