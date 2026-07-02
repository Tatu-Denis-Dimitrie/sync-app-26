using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.UserSignature;
using SyncApp26.Shared.DTOs.Response.UserSignature;
using System.Security.Claims;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserSignatureController : ControllerBase
    {
        private readonly IUserSignatureService _signatureService;
        private readonly IUserService _userService;

        public UserSignatureController(IUserSignatureService signatureService, IUserService userService)
        {
            _signatureService = signatureService;
            _userService = userService;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private bool TryGetCallerId(out Guid callerId)
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out callerId);
        }

        private string CallerEmail =>
            User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

        private bool CallerIsAdmin => User.IsInRole("Admin");

        /// <summary>
        /// Returns true when the caller is allowed to access the given user's signature data.
        /// Admins: any user.
        /// Line Managers: their own direct reports only.
        /// Everyone else: themselves only.
        /// </summary>
        private async Task<bool> CanAccessSignatureOfAsync(Guid targetUserId, Guid callerId)
        {
            if (callerId == targetUserId) return true;
            if (CallerIsAdmin) return true;

            if (User.IsInRole("Line Manager"))
            {
                var target = await _userService.GetUserByIdAsync(targetUserId);
                return target?.AssignedToId == callerId;
            }

            return false;
        }

        // ── GET my own signature ──────────────────────────────────────────────────────────

        /// <summary>Returns the current active signature of the authenticated user, or null if none exists.</summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMySignature()
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var sig = await _signatureService.GetUserSignatureAsync(callerId);
            if (sig == null)
                return Ok((object?)null);

            return Ok(MapToDto(sig));
        }

        // ── GET any user's signature (privileged) ────────────────────────────────────────

        /// <summary>Returns the current active signature for the given user.</summary>
        [HttpGet("{userId:guid}")]
        public async Task<IActionResult> GetUserSignature(Guid userId)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            if (!await CanAccessSignatureOfAsync(userId, callerId))
                return Forbid();

            var sig = await _signatureService.GetUserSignatureAsync(userId);
            if (sig == null)
                return NotFound(new { message = "No active signature found for this user." });

            return Ok(MapToDto(sig));
        }

        // ── SAVE (create or update) own signature ────────────────────────────────────────

        /// <summary>
        /// Saves (creates or replaces) the authenticated user's stored signature.
        /// A SHA-256 integrity hash and a server-issued RSA cryptographic proof are computed
        /// automatically; an immutable audit entry is appended on every save.
        /// </summary>
        [HttpPost("save")]
        public async Task<IActionResult> SaveMySignature([FromBody] SaveUserSignatureRequestDTO request)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.SignatureData))
                return BadRequest(new { message = "SignatureData is required." });

            var allowedMethods = new[] { "Draw", "Type" };
            if (!allowedMethods.Contains(request.SignatureMethod, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { message = "SignatureMethod must be 'Draw' or 'Type'." });

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            try
            {
                await _signatureService.SaveUserSignatureAsync(
                    callerId,
                    request.SignatureData,
                    request.SignatureMethod,
                    ip,
                    callerId,
                    CallerEmail);

                var saved = await _signatureService.GetUserSignatureAsync(callerId);
                return Ok(new
                {
                    message = "Signature saved successfully.",
                    signature = saved != null ? MapToDto(saved) : null
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ── REVOKE own signature ──────────────────────────────────────────────────────────

        /// <summary>
        /// Marks the authenticated user's stored signature as revoked.
        /// The data is retained for audit; a "Revoked" entry is appended to the history.
        /// </summary>
        [HttpDelete("revoke")]
        public async Task<IActionResult> RevokeMySignature()
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            try
            {
                await _signatureService.RevokeUserSignatureAsync(callerId, ip, callerId, CallerEmail);
                return Ok(new { message = "Signature revoked. The audit trail has been preserved." });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── GET audit history ─────────────────────────────────────────────────────────────

        /// <summary>Returns the full immutable audit trail for the given user's signature.</summary>
        [HttpGet("{userId:guid}/history")]
        public async Task<IActionResult> GetSignatureHistory(Guid userId)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            if (!await CanAccessSignatureOfAsync(userId, callerId))
                return Forbid();

            var history = await _signatureService.GetUserSignatureHistoryAsync(userId);
            var result = history.Select(h => new UserSignatureHistoryResponseDTO
            {
                Id = h.Id,
                UserId = h.UserId,
                SignatureMethod = h.SignatureMethod,
                SignatureHash = h.SignatureHash,
                Action = h.Action,
                IpAddress = h.IpAddress,
                PerformedByUserId = h.PerformedByUserId,
                PerformedByEmail = h.PerformedByEmail,
                CreatedAt = h.CreatedAt
            });

            return Ok(result);
        }

        /// <summary>Returns the full immutable audit trail for the authenticated user's own signature.</summary>
        [HttpGet("my/history")]
        public async Task<IActionResult> GetMySignatureHistory()
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var history = await _signatureService.GetUserSignatureHistoryAsync(callerId);
            var result = history.Select(h => new UserSignatureHistoryResponseDTO
            {
                Id = h.Id,
                UserId = h.UserId,
                SignatureMethod = h.SignatureMethod,
                SignatureHash = h.SignatureHash,
                Action = h.Action,
                IpAddress = h.IpAddress,
                PerformedByUserId = h.PerformedByUserId,
                PerformedByEmail = h.PerformedByEmail,
                CreatedAt = h.CreatedAt
            });

            return Ok(result);
        }

        // ── Mapper ────────────────────────────────────────────────────────────────────────

        private static UserSignatureResponseDTO MapToDto(Domain.Entities.UserSignature sig) => new()
        {
            Id = sig.Id,
            UserId = sig.UserId,
            SignatureData = sig.SignatureData,
            SignatureMethod = sig.SignatureMethod,
            SignatureHash = sig.SignatureHash,
            CreatedAt = sig.CreatedAt,
            UpdatedAt = sig.UpdatedAt,
            IsActive = sig.RevokedAt == null
        };
    }
}
