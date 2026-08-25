using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly AuthCookieOptions _authCookieOptions;

        public ImpersonationController(IImpersonationService impersonationService, AuthCookieOptions authCookieOptions)
        {
            _impersonationService = impersonationService;
            _authCookieOptions = authCookieOptions;
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
                ImpersonationStatus.TargetNotFound => NotFound(new { message = "User not found." }),
                ImpersonationStatus.TargetIsAdmin => StatusCode(403, new { message = "You cannot view as another administrator." }),
                ImpersonationStatus.SelfImpersonation => BadRequest(new { message = "You cannot view as yourself." }),
                ImpersonationStatus.Success => ImpersonationSuccess(result),
                _ => StatusCode(500, new { message = "An error occurred while processing your request." })
            };
        }

        // The cookie is additive for now - the body still carries the token so the existing
        // localStorage-based client keeps working untouched.
        private IActionResult ImpersonationSuccess(ImpersonationResult result)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, ImpersonationCookieLifetime);

            return Ok(new
            {
                message = "Impersonation session started.",
                token = result.Token,
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
