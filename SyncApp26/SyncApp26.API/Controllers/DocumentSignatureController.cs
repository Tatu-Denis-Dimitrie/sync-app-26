using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncApp26.API.Hubs;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentSignatureController : ControllerBase
    {
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly IDocumentService _documentService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<SyncHub> _hubContext;

        private static readonly ConcurrentDictionary<string, BulkSignProgress> BulkSignJobs = new();

        public DocumentSignatureController(
            IDocumentSignatureService documentSignatureService,
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory,
            IHubContext<SyncHub> hubContext)
        {
            _documentSignatureService = documentSignatureService;
            _userService = userService;
            _emailService = emailService;
            _documentService = documentService;
            _configuration = configuration;
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
        }

        public class RequestSignatureDto
        {
            public string Email { get; set; } = string.Empty;
            public Guid DocumentId { get; set; }
            public string DocumentName { get; set; } = string.Empty;
        }

        [HttpPost("request-signature")]
        public async Task<IActionResult> RequestSignature([FromBody] RequestSignatureDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var normalizedEmail = request.Email.ToLowerInvariant().Trim();
            
            // Check if user has an account
            var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
            
            if (existingUser != null)
            {
                // User has an account. Send them to login.
                var loginUrl = _configuration["Frontend:LoginUrl"] ?? "http://localhost:4200/login";
                await _emailService.SendDocumentSignatureEmailForRegisteredUserAsync(existingUser.Email, request.DocumentName, loginUrl);
            }
            else
            {
                // User does not have an account. Generate secure link and send it.
                var token = await _documentSignatureService.GenerateSignatureTokenAsync(normalizedEmail, request.DocumentId, request.DocumentName);
                
                var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                var secureLink = $"{frontendUrl}/sign/{token}";

                await _emailService.SendDocumentSignatureEmailWithLinkAsync(normalizedEmail, request.DocumentName, secureLink);
            }

            return Ok(new { message = "Signature request processed successfully." });
        }

        [HttpGet("validate-token/{token}")]
        public async Task<IActionResult> ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { message = "Token is required." });
            }

            var signatureToken = await _documentSignatureService.ValidateTokenAsync(token);

            if (signatureToken == null)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            // Determine whether the signer is acting as the employee, the manager, or the admin.
            var document = await _documentService.GetDocumentByIdAsync(signatureToken.DocumentId);
            var signerUser = await _userService.GetUserByEmailAsync(signatureToken.Email);
            bool signerIsAdmin = string.Equals(signerUser?.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
            bool isManagerSigning = !signerIsAdmin && (document?.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, signatureToken.Email, StringComparison.OrdinalIgnoreCase));
            bool isAdminSigning = signerIsAdmin && document?.DocumentType?.ToUpperInvariant() == "SSM";

            // Return the necessary document info for the frontend to render the signing UI
            return Ok(new
            {
                documentId = signatureToken.DocumentId,
                documentName = signatureToken.DocumentName,
                email = signatureToken.Email,
                documentType = document?.DocumentType,
                isManagerSigning = isManagerSigning,
                isAdminSigning = isAdminSigning
            });
        }

        public class ConsumeTokenDto
        {
            public string Token { get; set; } = string.Empty;
            public string SignatureMethod { get; set; } = string.Empty; // Draw, Type
            public string SignatureData { get; set; } = string.Empty; // Base64
            /// <summary>When true the same signature is applied to all other pending documents the signer is responsible for.</summary>
            public bool BulkSign { get; set; }
        }

        [HttpPost("consume-token")]
        public async Task<IActionResult> ConsumeToken([FromBody] ConsumeTokenDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { message = "Token is required." });
            }

            var tokenEntity = await _documentSignatureService.ValidateTokenAsync(request.Token);
            if (tokenEntity == null)
            {
                return BadRequest(new { message = "Token is invalid or expired." });
            }

            var document = await _documentService.GetDocumentByIdAsync(tokenEntity.DocumentId);
            if (document == null)
            {
                return BadRequest(new { message = "Document not found." });
            }

            var signerUserFromToken = await _userService.GetUserByEmailAsync(tokenEntity.Email);
            bool signerIsAdmin = string.Equals(signerUserFromToken?.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
            bool isLineManager = !signerIsAdmin && (document.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, tokenEntity.Email, StringComparison.OrdinalIgnoreCase));
            bool isUserSignature = !signerIsAdmin && !isLineManager;

            // Enforce sequential signing
            if (isUserSignature && document.UserSignedAt != null)
            {
                return BadRequest(new { message = "User already signed this document." });
            }

            if (isLineManager && document.UserSignedAt == null)
            {
                return BadRequest(new { message = "Employee must sign this document first." });
            }

            if (isLineManager && document.ManagerSignedAt != null)
            {
                return BadRequest(new { message = "Manager already signed this document." });
            }

            if (signerIsAdmin)
            {
                if (document.UserSignedAt == null || document.ManagerSignedAt == null)
                    return BadRequest(new { message = "Both employee and manager must sign before admin can verify." });
                if (document.DocumentType?.ToUpperInvariant() != "SSM")
                    return BadRequest(new { message = "Admin only signs SSM documents." });
                if (document.Status != "PendingAdmin")
                    return BadRequest(new { message = "Document is not pending admin signature." });
            }

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);
            if (!isValidAndConsumed)
            {
                return BadRequest(new { message = "Token could not be consumed." });
            }

            // Record signature
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _documentService.UpdateDocumentSignatureAsync(
                document.Id,
                isUserSignature,
                request.SignatureMethod,
                request.SignatureData,
                ipAddress,
                signerIsAdmin
            );

            // If employee signed, generate link for the manager and send email
            if (isUserSignature && document.User?.AssignedTo != null)
            {
                var manager = document.User.AssignedTo;
                var managerToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                    manager.Email, 
                    document.Id, 
                    $"{document.DocumentType} Document (Manager Approval)");

                var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                var managerSecureLink = $"{frontendUrl}/sign/{managerToken}";

                try
                {
                    await _emailService.SendDocumentSignatureEmailWithLinkAsync(
                        manager.Email, 
                        $"{document.DocumentType} Document (Manager Approval)", 
                        managerSecureLink);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the signing operation
                    Console.WriteLine($"Warning: Failed to send email to manager {manager.Email}: {ex.Message}");
                }
            }

            // Bulk sign: apply the same signature to all other pending docs this signer is responsible for
            int bulkCount = 0;
            if (request.BulkSign && (isLineManager || signerIsAdmin))
            {
                if (signerUserFromToken != null)
                {
                    var ipAddress2 = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    bulkCount = await _documentService.BulkSignDocumentsAsync(
                        signerIsAdmin, signerUserFromToken.Id,
                        request.SignatureMethod, request.SignatureData, ipAddress2);
                }
            }

            var msg = bulkCount > 0
                ? $"Document successfully signed. {bulkCount} additional document(s) were signed with the same signature."
                : "Document successfully signed using secure link.";

            // Notify all connected clients that a signature was recorded so dashboards can refresh
            await _hubContext.Clients.All.SendAsync("SignatureUpdated");

            return Ok(new { message = msg });
        }

        public class BulkSignDto
        {
            public string SignatureMethod { get; set; } = string.Empty;
            public string SignatureData { get; set; } = string.Empty;
        }

        /// <summary>
        /// Applies a manager/instructor signature to all documents that are currently
        /// awaiting the caller's countersignature (Status == "PendingManager").
        /// Admins sign all pending documents; Line Managers sign only their employees' documents.
        /// </summary>
        [HttpPost("bulk-sign")]
        [Authorize]
        public async Task<IActionResult> BulkSign([FromBody] BulkSignDto request)
        {
            if (string.IsNullOrWhiteSpace(request.SignatureData))
                return BadRequest(new { message = "Signature data is required." });

            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            bool isAdmin = User.IsInRole("Admin");
            bool isLineManager = User.IsInRole("Line Manager");

            if (!isAdmin && !isLineManager)
                return Forbid();

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var count = await _documentService.BulkSignDocumentsAsync(
                isAdmin, userId, request.SignatureMethod, request.SignatureData, ipAddress);

            await _hubContext.Clients.All.SendAsync("SignatureUpdated");

            return Ok(new { message = $"Successfully signed {count} document(s).", count });
        }

        [HttpPost("bulk-sign-async")]
        [Authorize(Roles = "Admin,Line Manager")]
        public async Task<IActionResult> BulkSignAsync([FromBody] BulkSignDto request)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            bool isAdmin = User.IsInRole("Admin");
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            // Obține totalul documentelor de semnat
            int total = await _documentService.GetPendingSsmDocumentsForAdminAsync();
            if (total == 0)
                return Ok(new { message = "No documents to sign.", jobId = (string?)null });

            string jobId = Guid.NewGuid().ToString();
            var progress = new BulkSignProgress { Total = total, Signed = 0, Completed = false };
            BulkSignJobs[jobId] = progress;

            // Rulează semnarea în fundal cu scope nou
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var docService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
                try
                {
                    var baseQuery = await docService.GetPendingSsmDocumentsForAdminListAsync();
                    int idx = 0;
                    foreach (var doc in baseQuery)
                    {
                        await docService.SignSingleDocumentAsAdminAsync(doc, request.SignatureMethod, request.SignatureData, ipAddress);
                        progress.Signed++;
                        idx++;
                        await Task.Delay(250); // delay vizibil între semnături
                    }
                    progress.Completed = true;
                }
                catch (Exception ex)
                {
                    progress.Error = ex.Message;
                    progress.Completed = true;
                }
            });

            return Ok(new { jobId, total });
        }

        [HttpGet("bulk-sign-status/{jobId}")]
        [Authorize(Roles = "Admin,Line Manager")]
        public IActionResult GetBulkSignStatus(string jobId)
        {
            if (BulkSignJobs.TryGetValue(jobId, out var progress))
            {
                return Ok(new { total = progress.Total, signed = progress.Signed, completed = progress.Completed, error = progress.Error });
            }
            return NotFound(new { message = "Job not found" });
        }

        public class AdminSignAndSendDto
        {
            public string DocumentType { get; set; } = string.Empty; // "SSM", "SU", "Both"
            public string SignatureMethod { get; set; } = string.Empty; // "Draw" or "Type"
            public string SignatureData { get; set; } = string.Empty; // Base64 signature
        }

        /// <summary>
        /// Admin signs all recently generated documents and sends signature links to employees.
        /// This endpoint streamlines the workflow: generate → admin signs → send to employees.
        /// </summary>
        [HttpPost("admin-sign-and-send-generated-documents")]
        [Authorize]
        public async Task<IActionResult> AdminSignAndSendGeneratedDocuments([FromBody] AdminSignAndSendDto request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType))
                return BadRequest(new { message = "DocumentType is required (SSM, SU, or Both)." });

            if (string.IsNullOrWhiteSpace(request.SignatureData))
                return BadRequest(new { message = "SignatureData is required." });

            var types = request.DocumentType.Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? new[] { "SSM", "SU" }
                : new[] { request.DocumentType.ToUpperInvariant() };

            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            if (!User.IsInRole("Admin"))
                return Forbid();

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";

            int signedCount = 0;
            int emailsSent = 0;

            foreach (var type in types)
            {
                signedCount += await _documentService.BulkSignAndSendGeneratedDocumentsAsync(
                    type,
                    request.SignatureMethod,
                    request.SignatureData,
                    ipAddress);

                var pendingDocuments = await _documentService.GetAllPendingUserDocumentsAsync(type);
                foreach (var doc in pendingDocuments)
                {
                    if (doc.User?.Email is { Length: > 0 } userEmail && doc.UserSignedAt == null)
                    {
                        try
                        {
                            var token = await _documentSignatureService.GenerateSignatureTokenAsync(
                                userEmail, doc.Id, $"{type} Document");
                            var link = $"{frontendUrl}/sign/{token}";
                            await _emailService.SendDocumentSignatureEmailWithLinkAsync(userEmail, $"{type} Document", link);
                            emailsSent++;
                        }
                        catch { /* non-fatal per user */ }
                    }
                }
            }

            return Ok(new
            {
                message = $"Successfully signed {signedCount} document(s) and sent {emailsSent} signature request(s).",
                documentsSigned = signedCount,
                emailsSent = emailsSent
            });
        }

        [HttpGet("pending-ssm-admin-count")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetPendingSsmAdminCount()
        {
            var count = await _documentService.GetPendingSsmDocumentsForAdminAsync();
            return Ok(new { count });
        }
    }

    public class BulkSignProgress
    {
        public int Total { get; set; }
        public int Signed { get; set; }
        public bool Completed { get; set; }
        public string? Error { get; set; }
    }
}
