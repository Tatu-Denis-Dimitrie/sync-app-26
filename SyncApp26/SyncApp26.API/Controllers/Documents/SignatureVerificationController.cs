using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.SignatureVerification;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;
using SyncApp26.API.Extensions;
using SyncApp26.API.Filters;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/signatures")]
    [Authorize]
    public class SignatureVerificationController : ControllerBase
    {
        private const int MaxBatchSize = 100;
        private const int MaxUsersPerRequest = 200;

        private readonly ISignatureVerificationService _verificationService;
        private readonly IUserService _userService;

        public SignatureVerificationController(ISignatureVerificationService verificationService, IUserService userService)
        {
            _verificationService = verificationService;
            _userService = userService;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private bool TryGetCallerId(out Guid callerId)
        {
            var id = User.GetUserId();
            callerId = id ?? Guid.Empty;
            return id.HasValue;
        }

        private bool CallerIsAdmin => User.IsInRole(Roles.Admin);

        /// <summary>
        /// Returns true when the caller is allowed to see verification status for signatures
        /// made by the given signer.
        /// Admins: any signer. Line Managers: their own direct reports only. Everyone else: themselves only.
        /// </summary>
        private async Task<bool> CanAccessSignaturesOfAsync(Guid signerUserId, Guid callerId)
        {
            if (callerId == signerUserId) return true;
            if (CallerIsAdmin) return true;

            if (User.IsInRole(Roles.LineManager))
            {
                var signer = await _userService.GetUserByIdAsync(signerUserId);
                return signer?.AssignedToId == callerId;
            }

            return false;
        }

        // ── GET verification status for a single signature ──────────────────────────────

        /// <summary>Recomputes and returns the HMAC/chain verification status of one signature record.</summary>
        [HttpGet("{id:guid}/verification-status")]
        public async Task<IActionResult> GetVerificationStatus(Guid id)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var status = await _verificationService.GetVerificationStatusAsync(id);
            if (status == null)
                return NotFound(new { message = "No signature record found with this id." });

            if (!await CanAccessSignaturesOfAsync(status.SignerUserId, callerId))
                return Forbid();

            return Ok(status);
        }

        // ── POST verification status for a batch of signatures ──────────────────────────

        /// <summary>
        /// Recomputes and returns the HMAC/chain verification status for a batch of signature
        /// records. Ids the caller is not allowed to see are silently omitted from the result.
        /// </summary>
        [HttpPost("verification-status/batch")]
        [AllowDuringImpersonation]
        public async Task<IActionResult> GetVerificationStatusBatch([FromBody] BatchVerificationStatusRequestDTO request)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            if (request.SignatureIds.Count == 0)
                return BadRequest(new { message = "SignatureIds must contain at least one id." });

            if (request.SignatureIds.Count > MaxBatchSize)
                return BadRequest(new { message = $"SignatureIds must not contain more than {MaxBatchSize} ids." });

            var results = await _verificationService.GetVerificationStatusBatchAsync(request.SignatureIds);

            // "NotFound" entries carry no signer-attributable data, so they're safe to return
            // to any authenticated caller; everything else is filtered by the same access rule
            // as the single-id endpoint.
            var allowed = new List<object>();
            foreach (var result in results)
            {
                if (result.Status == "NotFound" || await CanAccessSignaturesOfAsync(result.SignerUserId, callerId))
                    allowed.Add(result);
            }

            return Ok(allowed);
        }

        // ── GET signature history for a periodic training slot ──────────────────────────

        /// <summary>
        /// Returns every SignatureRecord version for a periodic training, grouped by signer role.
        /// Access follows the training's employee, not any individual signer: self, any admin, or
        /// the employee's line manager.
        /// </summary>
        [HttpGet("training/{periodicTrainingId:guid}/history")]
        public async Task<IActionResult> GetTrainingSignatureHistory(Guid periodicTrainingId)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var history = await _verificationService.GetSignatureHistoryForTrainingAsync(periodicTrainingId);
            if (history == null)
                return NotFound(new { message = "No periodic training found with this id." });

            if (!await CanAccessSignaturesOfAsync(history.UserId, callerId))
                return Forbid();

            return Ok(history);
        }

        // ── POST verification status grouped by employee ────────────────────────────────

        /// <summary>
        /// Recomputes and returns the verification status of every SignatureRecord belonging to
        /// each requested employee's documents (their own signature and any manager/admin
        /// countersignatures) — the real-time check for "did launching a new session break any of
        /// this employee's existing signatures." UserIds the caller is not allowed to see are
        /// silently omitted from the result; employees with no signatures yet get an empty list,
        /// not an error.
        /// </summary>
        [HttpPost("verification-status/by-users")]
        [AllowDuringImpersonation]
        public async Task<IActionResult> GetVerificationStatusForUsers([FromBody] VerificationStatusForUsersRequestDTO request)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var userIds = request.UserIds.Distinct().ToList();

            if (userIds.Count == 0)
                return BadRequest(new { message = "UserIds must contain at least one id." });

            if (userIds.Count > MaxUsersPerRequest)
                return BadRequest(new { message = $"UserIds must not contain more than {MaxUsersPerRequest} ids." });

            var statusesByUser = await _verificationService.GetVerificationStatusForUsersAsync(userIds);

            var allowed = new Dictionary<Guid, List<SignatureVerificationStatusResponseDTO>>();
            foreach (var userId in userIds)
            {
                if (await CanAccessSignaturesOfAsync(userId, callerId))
                    allowed[userId] = statusesByUser.GetValueOrDefault(userId, new List<SignatureVerificationStatusResponseDTO>());
            }

            return Ok(allowed);
        }
    }
}
