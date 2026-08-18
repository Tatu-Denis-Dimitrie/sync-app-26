using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.API.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;
using SyncApp26.API.Extensions;
using System.Collections.Concurrent;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly IEmailService _emailService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IDocumentSigningService _documentSigningService;
        private readonly IUserService _userService;
        private readonly ISignatureVerificationService _signatureVerificationService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        // Same in-memory job board as DocumentSignatureController's bulk-sign jobs: a bulk generation
        // outlives its HTTP request, so its progress lives here and is polled by jobId.
        private static readonly ConcurrentDictionary<string, BulkGenerateProgress> BulkGenerateJobs = new();

        public DocumentController(
            IDocumentService documentService,
            IEmailService emailService,
            IDocumentSignatureService documentSignatureService,
            IDocumentSigningService documentSigningService,
            IUserService userService,
            ISignatureVerificationService signatureVerificationService,
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory)
        {
            _documentService = documentService;
            _emailService = emailService;
            _documentSignatureService = documentSignatureService;
            _documentSigningService = documentSigningService;
            _userService = userService;
            _signatureVerificationService = signatureVerificationService;
            _configuration = configuration;
            _scopeFactory = scopeFactory;
        }

        // Flat DTO — avoids serializing deep User navigation property chains
        private static object MapDocument(UserDocument d, DocumentSignatureIdsDTO? signatureIds) => new
        {
            d.Id,
            d.UserId,
            UserFirstName = d.User?.FirstName,
            UserLastName = d.User?.LastName,
            UserEmail = d.User?.Email,
            UserDepartment = d.User?.Department?.Name,
            UserFunction = d.User?.Function?.Name,
            d.DocumentType,
            d.Status,
            d.GeneratedAt,
            d.PdfFilePath,
            d.DocumentHash,
            d.UserSignatureMethod,
            d.UserSignatureData,
            d.UserSignatureIpAddress,
            d.UserSignedAt,
            d.ManagerSignatureMethod,
            d.ManagerSignatureData,
            d.ManagerSignatureIpAddress,
            d.ManagerSignedAt,
            d.InstructorSignatureMethod,
            d.InstructorSignatureData,
            d.InstructorSignatureIpAddress,
            d.InstructorSignedAt,
            d.AdminSignatureMethod,
            d.AdminSignatureData,
            d.AdminSignatureIpAddress,
            d.AdminSignedAt,
            UserSignatureId = signatureIds?.UserSignatureId,
            ManagerSignatureId = signatureIds?.ManagerSignatureId,
            InstructorSignatureId = signatureIds?.InstructorSignatureId,
            AdminSignatureId = signatureIds?.AdminSignatureId,
        };

        // Resolves and attaches each document's current signature-record ids in one batched
        // lookup, then maps — every list endpoint funnels through this single helper.
        private async Task<IEnumerable<object>> MapDocumentsAsync(IEnumerable<UserDocument> documents)
        {
            var docs = documents as IList<UserDocument> ?? documents.ToList();
            var lookup = await _signatureVerificationService.GetLatestSignatureRecordIdsAsync(docs.Select(d => d.Id));
            return docs.Select(d => MapDocument(d, lookup.GetValueOrDefault(d.Id)));
        }

        public class GenerateDocumentDto
        {
            public Guid UserId { get; set; }
            public string DocumentType { get; set; } = string.Empty;
        }

        public class BulkGenerateDocumentDto
        {
            /// <summary>"SSM", "SU", or "Both"</summary>
            public string DocumentType { get; set; } = string.Empty;
            public List<Guid>? SelectedUserIds { get; set; }
        }

        [HttpPost("bulk-generate")]
        public async Task<IActionResult> BulkGenerateDocuments([FromBody] BulkGenerateDocumentDto request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType))
                return BadRequest(new { message = "DocumentType is required (SSM, SU, or Both)." });

            var adminEmail = User.GetEmail() ?? "admin@syncapp26.com";
            var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";

            var typesToProcess = ResolveAuthorizedTypes(request.DocumentType);
            if (typesToProcess.Count == 0)
                return Forbid();

            int totalGenerated = 0, totalSkipped = 0;
            var generatedIdsByType = new List<(string Type, List<Guid> DocumentIds)>();
            var countsByType = new Dictionary<string, int>();

            foreach (var (type, restrictToAssignedToId) in typesToProcess)
            {
                var result = await _documentService.BulkGenerateDocumentsAsync(type, adminEmail, request.SelectedUserIds, restrictToAssignedToId);
                totalGenerated += result.Generated;
                totalSkipped += result.Skipped;
                generatedIdsByType.Add((type, result.GeneratedDocumentIds));
                countsByType[type] = result.Generated;
            }

            var emailOutcome = await SendSignatureRequestsAsync(
                _documentService, _documentSignatureService, _emailService, frontendUrl, generatedIdsByType);

            return Ok(new
            {
                message = BuildBulkGenerateMessage(totalGenerated, totalSkipped, countsByType, emailOutcome),
                generated = totalGenerated,
                skipped = totalSkipped,
                generatedByType = countsByType,
                emailsSent = emailOutcome.Sent,
                emailsFailed = emailOutcome.Failed,
                emailError = emailOutcome.FirstError
            });
        }

        [HttpPost("bulk-generate-async")]
        public async Task<IActionResult> BulkGenerateDocumentsAsync([FromBody] BulkGenerateDocumentDto request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType))
                return BadRequest(new { message = "DocumentType is required (SSM, SU, or Both)." });

            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var typesToProcess = ResolveAuthorizedTypes(request.DocumentType);
            if (typesToProcess.Count == 0)
                return Forbid();

            var adminEmail = User.GetEmail() ?? "admin@syncapp26.com";
            var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";

            int total = 0;
            foreach (var (_, restrictToAssignedToId) in typesToProcess)
                total += (await _documentService.GetBulkGenerateTargetUserIdsAsync(request.SelectedUserIds, restrictToAssignedToId)).Count;

            if (total == 0)
                return Ok(new { message = "No documents to generate.", jobId = (string?)null, total = 0 });

            var jobId = Guid.NewGuid().ToString();
            var progress = new BulkGenerateProgress { OwnerUserId = userId, Total = total };
            BulkGenerateJobs[jobId] = progress;

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var logger = serviceProvider.GetService<ILogger<DocumentController>>();

                var generatedIdsByType = new List<(string Type, List<Guid> DocumentIds)>();

                try
                {
                    var documentService = serviceProvider.GetRequiredService<IDocumentService>();

                    int generatedBefore = 0, skippedBefore = 0;
                    foreach (var (type, restrictToAssignedToId) in typesToProcess)
                    {
                        var result = await documentService.BulkGenerateDocumentsAsync(
                            type, adminEmail, request.SelectedUserIds, restrictToAssignedToId,
                            onProgress: (generated, skipped) =>
                            {
                                progress.Generated = generatedBefore + generated;
                                progress.Skipped = skippedBefore + skipped;
                            });

                        generatedBefore += result.Generated;
                        skippedBefore += result.Skipped;
                        progress.Generated = generatedBefore;
                        progress.Skipped = skippedBefore;
                        progress.GeneratedByType[type] = result.Generated;
                        generatedIdsByType.Add((type, result.GeneratedDocumentIds));
                    }

                    progress.Message = BuildBulkGenerateMessage(progress.Generated, progress.Skipped, progress.GeneratedByType, null);
                }
                catch (Exception ex)
                {
                    progress.Error = ex.Message;
                    generatedIdsByType.Clear();
                }
                finally
                {
                    progress.Phase = "done";
                    progress.Completed = true;
                }

                if (generatedIdsByType.Count == 0) return;

                try
                {
                    var emailOutcome = await SendSignatureRequestsAsync(
                        serviceProvider.GetRequiredService<IDocumentService>(),
                        serviceProvider.GetRequiredService<IDocumentSignatureService>(),
                        serviceProvider.GetRequiredService<IEmailService>(),
                        frontendUrl, generatedIdsByType);

                    progress.EmailsSent = emailOutcome.Sent;
                    progress.EmailsFailed = emailOutcome.Failed;
                    progress.EmailError = emailOutcome.FirstError;
                    progress.EmailsAborted = emailOutcome.AbortedEarly;

                    if (emailOutcome.Failed > 0)
                        logger.LogWarning(
                            "Bulk generate job {JobId}: {Sent} signature email(s) sent, {Failed} failed{Aborted}. First error: {Error}",
                            jobId, emailOutcome.Sent, emailOutcome.Failed,
                            emailOutcome.AbortedEarly ? " (remaining skipped after repeated failures)" : string.Empty,
                            emailOutcome.FirstError);
                    else
                        logger.LogInformation("Bulk generate job {JobId}: {Sent} signature email(s) sent.", jobId, emailOutcome.Sent);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Bulk generate job {JobId}: signature email dispatch failed.", jobId);
                }
            });

            return Ok(new { jobId, total });
        }

        [HttpGet("bulk-generate-status/{jobId}")]
        public IActionResult GetBulkGenerateStatus(string jobId)
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            if (!BulkGenerateJobs.TryGetValue(jobId, out var progress))
                return NotFound(new { message = "Job not found" });

            if (progress.OwnerUserId != userId)
                return Forbid();

            return Ok(new
            {
                total = progress.Total,
                generated = progress.Generated,
                skipped = progress.Skipped,
                processed = progress.Generated + progress.Skipped,
                phase = progress.Phase,
                generatedByType = progress.GeneratedByType,
                emailsSent = progress.EmailsSent,
                emailsFailed = progress.EmailsFailed,
                emailError = progress.EmailError,
                emailsAborted = progress.EmailsAborted,
                completed = progress.Completed,
                message = progress.Message,
                error = progress.Error
            });
        }

        private List<(string Type, Guid? RestrictToAssignedToId)> ResolveAuthorizedTypes(string documentType)
        {
            var requestedTypes = documentType.Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? new[] { "SSM", "SU" }
                : new[] { documentType.ToUpper() };

            bool isLineManager = User.IsInRole(Roles.LineManager);
            var currentUserId = User.GetUserId();
            var typesToProcess = new List<(string Type, Guid? RestrictToAssignedToId)>();
            foreach (var type in requestedTypes)
            {
                if (User.CanInitiateFor(type))
                    typesToProcess.Add((type, null));
                else if (isLineManager && currentUserId is { } managerId)
                    typesToProcess.Add((type, managerId));
            }

            return typesToProcess;
        }

        private static string BuildBulkGenerateMessage(int generated, int skipped, Dictionary<string, int> byType, BulkEmailOutcome? email)
        {
            var breakdown = byType.Count > 1
                ? $" ({string.Join(", ", byType.Select(kv => $"{kv.Value} {kv.Key}"))})"
                : string.Empty;

            var message = $"Bulk generation complete. {generated} document(s) generated{breakdown}, {skipped} skipped.";

            if (email is null)
                return $"{message} Signature request emails are being sent in the background.";

            if (email.Failed == 0)
                return $"{message} {email.Sent} signature request(s) sent to employees.";

            message += $" {email.Sent} signature request(s) sent, {email.Failed} failed";
            if (email.AbortedEarly)
                message += " — remaining notifications skipped because email delivery is failing repeatedly";
            return string.IsNullOrWhiteSpace(email.FirstError) ? $"{message}." : $"{message}. First error: {email.FirstError}";
        }

        private sealed class BulkEmailOutcome
        {
            public int Sent { get; set; }
            public int Failed { get; set; }
            public string? FirstError { get; set; }
            public bool AbortedEarly { get; set; }
        }

        // A misconfigured or unreachable SMTP server fails every message identically, and each
        // attempt still burns a full connect timeout (~0.9s measured against smtp.gmail.com), so
        // retrying it once per generated document turned a ~3 second generation into minutes of
        // dead waiting. Stop after this many consecutive failures instead. The counter resets on
        // every success, so an isolated bad recipient address never aborts the rest of the run.
        private const int ConsecutiveEmailFailureLimit = 3;

        private static async Task<BulkEmailOutcome> SendSignatureRequestsAsync(
            IDocumentService documentService,
            IDocumentSignatureService documentSignatureService,
            IEmailService emailService,
            string frontendUrl,
            IReadOnlyList<(string Type, List<Guid> DocumentIds)> generatedIdsByType)
        {
            var outcome = new BulkEmailOutcome();
            int consecutiveFailures = 0;

            foreach (var (type, documentIds) in generatedIdsByType)
            {
                var pendingDocs = await documentService.GetPendingUserDocumentsByIdsAsync(documentIds);
                foreach (var doc in pendingDocs)
                {
                    if (doc.User?.Email is not { Length: > 0 } userEmail || doc.UserSignedAt != null)
                        continue;

                    if (consecutiveFailures >= ConsecutiveEmailFailureLimit)
                    {
                        outcome.AbortedEarly = true;
                        return outcome;
                    }

                    try
                    {
                        var currentRowId = await documentService.GetCurrentTrainingIdForDocumentAsync(doc.Id);
                        var token = await documentSignatureService.GenerateSignatureTokenAsync(
                            userEmail, doc.Id, $"{type} Document", currentRowId);
                        var link = $"{frontendUrl}/sign/{token}";
                        await emailService.SendDocumentSignatureEmailWithLinkAsync(userEmail, $"{type} Document", link);
                        outcome.Sent++;
                        consecutiveFailures = 0;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        consecutiveFailures++;
                        outcome.FirstError ??= ex.Message;
                    }
                }
            }

            return outcome;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateDocument([FromBody] GenerateDocumentDto request)
        {
            try
            {
                var adminEmail = User.GetEmail() ?? "admin@syncapp26.com";

                var user = await _userService.GetUserByIdAsync(request.UserId);
                if (user == null) return NotFound(new { message = "User not found." });

                // The officer for this document's type can generate for anyone; a line manager is
                // restricted to their own direct reports. Admin has no standing here at all.
                bool canInitiate = User.CanInitiateFor(request.DocumentType)
                    || (User.IsInRole(Roles.LineManager) && user.AssignedToId == User.GetUserId());

                if (!canInitiate)
                    return Forbid();

                var document = await _documentService.GenerateDocumentAsync(request.UserId, request.DocumentType, adminEmail);

                // Now we need to send the signature request to the user.
                // Assuming we get the user's email from the generated document...
                var fullDocument = await _documentService.GetDocumentByIdAsync(document.Id);
                var userEmail = fullDocument?.User?.Email;

                if (!string.IsNullOrEmpty(userEmail))
                {
                    var currentRowId = await _documentService.GetCurrentTrainingIdForDocumentAsync(document.Id);
                    var token = await _documentSignatureService.GenerateSignatureTokenAsync(userEmail, document.Id, $"{document.DocumentType} Document", currentRowId);
                    var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                    var secureLink = $"{frontendUrl}/sign/{token}";

                    await _emailService.SendDocumentSignatureEmailWithLinkAsync(userEmail, $"{document.DocumentType} Document", secureLink);
                }

                return Ok(new { message = "Document generated successfully and signature requested.", documentId = document.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserDocuments(Guid userId)
        {
            if (User.GetUserId() is not { } callerId)
                return Unauthorized();

            // Same reach as ViewPdf: this leaks signature images and signer IPs, so it needs the
            // same owner/manager/officer/admin check rather than the plain [Authorize] it had before.
            bool isSelf = userId == callerId;
            bool isAdmin = User.IsInRole(Roles.Admin);
            bool isOfficer = User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);

            if (!isSelf && !isAdmin && !isOfficer)
            {
                var targetUser = await _userService.GetUserByIdAsync(userId);
                if (targetUser == null) return NotFound();
                if (targetUser.AssignedToId != callerId) return Forbid();
            }

            var documents = await _documentService.GetUserDocumentsAsync(userId);
            return Ok(await MapDocumentsAsync(documents));
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllDocuments()
        {
            var allDocs = await _documentService.GetAllDocumentsAsync();
            var documents = allDocs.AsEnumerable();

            // Admin and SSM/SU officers see every document — an officer's duty spans all employees,
            // not just their own reports. Everyone else (line managers, basic users) is scoped down.
            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything && User.GetUserId() is { } currentUserId)
            {
                documents = documents.Where(d => d.User?.AssignedToId == currentUserId || d.UserId == currentUserId);
            }

            return Ok(await MapDocumentsAsync(documents));
        }

        [HttpGet("my-pending-signatures")]
        public async Task<IActionResult> GetMyPendingSignatures()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            // Fetch documents where user is the employee and status is PendingUser
            var myDocuments = await _documentService.GetUserDocumentsAsync(userId);
            var pendingAsUser = myDocuments.Where(d => d.Status == "PendingUser");

            return Ok(await MapDocumentsAsync(pendingAsUser));
        }

        [HttpGet("manager-pending-signatures")]
        public async Task<IActionResult> GetManagerPendingSignatures()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var pendingAsManager = await _documentService.GetManagerPendingSignaturesAsync(userId);

            return Ok(await MapDocumentsAsync(pendingAsManager));
        }

        [HttpGet("my-signed-documents")]
        public async Task<IActionResult> GetMySignedDocuments()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var myDocuments = await _documentService.GetUserDocumentsAsync(userId);
            var signedAsUser = myDocuments.Where(d => d.UserSignedAt != null);

            return Ok(await MapDocumentsAsync(signedAsUser));
        }

        [HttpGet("manager-signed-documents")]
        public async Task<IActionResult> GetManagerSignedDocuments()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var signedAsManager = await _documentService.GetManagerSignedDocumentsAsync(userId);

            return Ok(await MapDocumentsAsync(signedAsManager));
        }

        [HttpGet("instructor-pending-signatures")]
        public async Task<IActionResult> GetInstructorPendingSignatures()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var pendingAsInstructor = await _documentService.GetInstructorPendingSignaturesAsync(userId);

            return Ok(await MapDocumentsAsync(pendingAsInstructor));
        }

        [HttpGet("instructor-signed-documents")]
        public async Task<IActionResult> GetInstructorSignedDocuments()
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var signedAsInstructor = await _documentService.GetInstructorSignedDocumentsAsync(userId);

            return Ok(await MapDocumentsAsync(signedAsInstructor));
        }

        /// <summary>
        /// Returns SSM documents pending admin signature (PendingAdmin status — signed by both employee and LM).
        /// </summary>
        [HttpGet("admin-pending-signatures")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetAdminPendingSignatures()
        {
            var docs = await _documentService.GetAdminPendingDocumentsAsync();
            return Ok(await MapDocumentsAsync(docs));
        }

        /// <summary>
        /// Returns SSM documents already signed by admin (Completed status).
        /// </summary>
        [HttpGet("admin-signed-documents")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetAdminSignedDocuments()
        {
            var docs = await _documentService.GetAdminSignedDocumentsAsync();
            return Ok(await MapDocumentsAsync(docs));
        }

        [HttpPost("regenerate-documents")]
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
        public async Task<IActionResult> RegenerateDocuments()
        {
            var count = await _documentService.RegenerateDocumentsAsync();
            return Ok(new
            {
                message = $"S-au regenerat {count} document(e) în folderul GeneratedDocuments.",
                regenerated = count
            });
        }

        /// <summary>
        /// One-off repair for SignatureRecords created before the Version column existed.
        /// Safe to run more than once.
        /// </summary>
        [HttpPost("backfill-signature-versions")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> BackfillSignatureVersions()
        {
            var count = await _documentService.BackfillSignatureRecordVersionsAsync();
            return Ok(new
            {
                message = $"S-au recalculat {count} versiune(i) de semnătură.",
                updated = count
            });
        }

        [HttpGet("token-for-document/{documentId}")]
        public async Task<IActionResult> GetSignTokenForDocument(Guid documentId)
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var document = await _documentService.GetDocumentByIdAsync(documentId);
            if (document == null) return NotFound(new { message = "Document not found." });

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            var result = await _documentSigningService.RequestSigningTokenAsync(document, user);

            if (result.Forbidden) return Forbid();
            if (!result.Success) return BadRequest(new { message = result.ErrorMessage });

            return Ok(new { token = result.Token });
        }

        /// <summary>
        /// Generates and streams the PDF for a document on-the-fly (includes embedded
        /// digital signatures if already signed). Accessible by the document owner,
        /// their line manager, or an admin.
        /// </summary>
        [HttpGet("{documentId}/view-pdf")]
        public async Task<IActionResult> ViewPdf(Guid documentId)
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            var document = await _documentService.GetDocumentByIdAsync(documentId);
            if (document == null) return NotFound(new { message = "Document not found." });

            bool isDocOwner = document.UserId == userId;
            bool isManager = document.User?.AssignedToId == userId;
            bool isSsm = document.DocumentType?.ToUpperInvariant() == "SSM";
            // Historical match (whoever actually signed as Instructor) OR the current officer for this
            // document's type, so an officer can preview a document before signing it too.
            bool signedAsInstructor = document.User?.PeriodicTrainings?
                .Where(pt => pt.UserDocumentId == document.Id)
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.CreatedAt)
                .FirstOrDefault()?.InstructorId == userId;
            bool isInstructor = signedAsInstructor || await _userService.IsInRoleAsync(userId, isSsm ? Roles.SsmOfficer : Roles.SuOfficer);
            bool isAdmin = User.IsInRole(Roles.Admin);

            if (!isDocOwner && !isManager && !isInstructor && !isAdmin)
                return Forbid();

            var docUser = document.User;
            if (docUser == null) return NotFound(new { message = "Associated user not found." });

            var safeFirst = string.Concat(docUser.FirstName.Where(char.IsLetterOrDigit));
            var safeLast = string.Concat(docUser.LastName.Where(char.IsLetterOrDigit));
            var fileName = $"{document.DocumentType}_{safeFirst}_{safeLast}.pdf";

            // Always generate on-the-fly so highlight logic adapts to the viewer's role
            var pdfBytes = await _documentService.GeneratePdfBytesAsync(docUser, document, viewerIsAdmin: isAdmin);
            return File(pdfBytes, "application/pdf", fileName);
        }
    }

    public class BulkGenerateProgress
    {
        public Guid OwnerUserId { get; set; }
        public int Total { get; set; }
        public int Generated { get; set; }
        public int Skipped { get; set; }

        /// <summary>"generating" → "emailing" → "done". Lets the client say what it is waiting on.</summary>
        public string Phase { get; set; } = "generating";
        public Dictionary<string, int> GeneratedByType { get; } = new();
        public int EmailsSent { get; set; }
        public int EmailsFailed { get; set; }
        public string? EmailError { get; set; }
        public bool EmailsAborted { get; set; }
        public bool Completed { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
}
