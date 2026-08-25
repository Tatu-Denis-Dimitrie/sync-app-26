using System.Security.Claims;
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
        private static readonly TimeSpan SessionCookieLifetime = TimeSpan.FromHours(8);

        private readonly IUserService _userService;
        private readonly IImpersonationService _impersonationService;
        private readonly AuthCookieOptions _authCookieOptions;

        public SessionController(IUserService userService, IImpersonationService impersonationService, AuthCookieOptions authCookieOptions)
        {
            _userService = userService;
            _impersonationService = impersonationService;
            _authCookieOptions = authCookieOptions;
        }

        // Always 200, even when there's no session - a 401 here would send the client's error
        // interceptor into logout(), which redirects to /login, which calls /me again: an infinite
        // reload loop on the login page itself.
        [HttpGet("me")]
        [AllowAnonymous]
        public async Task<IActionResult> Me()
        {
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
        public IActionResult Logout()
        {
            Response.DeleteAuthCookie(_authCookieOptions);
            return Ok(new { message = "Logged out." });
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
                ImpersonationStatus.Success => StopImpersonationSuccess(result),
                _ => StatusCode(500, new { message = "An error occurred while processing your request." })
            };
        }

        private IActionResult StopImpersonationSuccess(ImpersonationResult result)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, SessionCookieLifetime);

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
