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
        private readonly IDocumentService _documentService;

        public SignatureVerificationController(ISignatureVerificationService verificationService, IUserService userService, IDocumentService documentService)
        {
            _verificationService = verificationService;
            _userService = userService;
            _documentService = documentService;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private bool TryGetCallerId(out Guid callerId)
        {
            var id = User.GetUserId();
            callerId = id ?? Guid.Empty;
            return id.HasValue;
        }

        private bool CallerIsAdmin => User.IsInRole(Roles.Admin);

        /// <summary>True when the caller is an SsmOfficer/SuOfficer whose role matches this document's type — mirrors ClaimsPrincipalExtensions.CanInitiateFor, but for reading verification status rather than initiating a document.</summary>
        private bool CallerIsOfficerForType(string? documentType) =>
            DocumentTypes.IsSsm(documentType) && User.IsInRole(Roles.SsmOfficer)
            || DocumentTypes.IsSu(documentType) && User.IsInRole(Roles.SuOfficer);

        /// <summary>
        /// Returns true when the caller is allowed to see verification status for signatures
        /// made by the given signer.
        /// Admins: any signer. Line Managers: their own direct reports only. SSM/SU Officers: any
        /// signer on a document of their matching type. Everyone else: themselves only.
        /// </summary>
        private async Task<bool> CanAccessSignaturesOfAsync(Guid signerUserId, Guid callerId, string? documentType = null)
        {
            if (callerId == signerUserId) return true;
            if (CallerIsAdmin) return true;
            if (CallerIsOfficerForType(documentType)) return true;

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

            var documentType = status.UserDocumentId == Guid.Empty
                ? null
                : (await _documentService.GetDocumentTypesByIdsAsync(new[] { status.UserDocumentId })).GetValueOrDefault(status.UserDocumentId);

            if (!await CanAccessSignaturesOfAsync(status.SignerUserId, callerId, documentType))
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

            var documentIds = results.Where(r => r.Status != "NotFound").Select(r => r.UserDocumentId).Distinct();
            var documentTypesById = await _documentService.GetDocumentTypesByIdsAsync(documentIds);

            // "NotFound" entries carry no signer-attributable data, so they're safe to return
            // to any authenticated caller; everything else is filtered by the same access rule
            // as the single-id endpoint.
            var allowed = new List<object>();
            foreach (var result in results)
            {
                var documentType = documentTypesById.GetValueOrDefault(result.UserDocumentId);
                if (result.Status == "NotFound" || await CanAccessSignaturesOfAsync(result.SignerUserId, callerId, documentType))
                    allowed.Add(result);
            }

            return Ok(allowed);
        }

        // ── GET signature history for a periodic training slot ──────────────────────────

        /// <summary>
        /// Returns every SignatureRecord version for a periodic training, grouped by signer role.
        /// Access follows the training's employee, not any individual signer: self, any admin, the
        /// employee's line manager, or an SSM/SU officer matching the training's document type.
        /// </summary>
        [HttpGet("training/{periodicTrainingId:guid}/history")]
        public async Task<IActionResult> GetTrainingSignatureHistory(Guid periodicTrainingId)
        {
            if (!TryGetCallerId(out var callerId))
                return Unauthorized();

            var history = await _verificationService.GetSignatureHistoryForTrainingAsync(periodicTrainingId);
            if (history == null)
                return NotFound(new { message = "No periodic training found with this id." });

            if (!await CanAccessSignaturesOfAsync(history.UserId, callerId, history.DocumentType))
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

            // Resolved once up front so the per-record officer check below (needed because a single
            // employee can have both an SSM and an SU document) doesn't re-query per user.
            var documentIds = statusesByUser.Values.SelectMany(list => list.Select(s => s.UserDocumentId)).Distinct();
            var documentTypesById = await _documentService.GetDocumentTypesByIdsAsync(documentIds);

            var allowed = new Dictionary<Guid, List<SignatureVerificationStatusResponseDTO>>();
            foreach (var userId in userIds)
            {
                var userStatuses = statusesByUser.GetValueOrDefault(userId, new List<SignatureVerificationStatusResponseDTO>());

                if (await CanAccessSignaturesOfAsync(userId, callerId))
                {
                    allowed[userId] = userStatuses;
                    continue;
                }

                // Blanket access above already covers self/admin/line-manager; an officer without
                // one of those still gets the subset of this employee's signatures that belong to
                // their matching document type.
                var officerVisible = userStatuses
                    .Where(s => CallerIsOfficerForType(documentTypesById.GetValueOrDefault(s.UserDocumentId)))
                    .ToList();
                if (officerVisible.Count > 0)
                    allowed[userId] = officerVisible;
            }

            return Ok(allowed);
        }
    }
}
