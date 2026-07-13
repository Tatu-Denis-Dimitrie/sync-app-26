using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncApp26.API.Hubs;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using System.Collections.Concurrent;
using System.Threading;
using SyncApp26.API.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentSignatureController : ControllerBase
    {
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IDocumentSigningService _documentSigningService;
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly IDocumentService _documentService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<SyncHub> _hubContext;

        private static readonly ConcurrentDictionary<string, BulkSignProgress> BulkSignJobs = new();

        public DocumentSignatureController(
            IDocumentSignatureService documentSignatureService,
            IDocumentSigningService documentSigningService,
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory,
            IHubContext<SyncHub> hubContext)
        {
            _documentSignatureService = documentSignatureService;
            _documentSigningService = documentSigningService;
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
        [AllowAnonymous]
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
        [AllowAnonymous]
        public async Task<IActionResult> ValidateToken(string token)
        {
            var result = await _documentSigningService.GetSigningContextAsync(token);

            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            // Return the necessary document info for the frontend to render the signing UI
            return Ok(new
            {
                documentId = result.DocumentId,
                documentName = result.DocumentName,
                email = result.Email,
                documentType = result.DocumentType,
                isManagerSigning = result.IsManagerSigning,
                isAdminSigning = result.IsAdminSigning,
                periodicTrainingId = result.PeriodicTrainingId
            });
        }

        public class ConsumeTokenDto
        {
            public string Token { get; set; } = string.Empty;
            public string SignatureMethod { get; set; } = string.Empty; // Draw, Type
            public string SignatureData { get; set; } = string.Empty; // Base64
            /// <summary>When true the same signature is applied to all other pending documents the signer is responsible for.</summary>
            public bool BulkSign { get; set; }
            /// <summary>The specific PeriodicTraining row being signed, as communicated by validate-token.</summary>
            public Guid? PeriodicTrainingId { get; set; }
        }

        [HttpPost("consume-token")]
        [AllowAnonymous]
        public async Task<IActionResult> ConsumeToken([FromBody] ConsumeTokenDto request)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var result = await _documentSigningService.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest
            {
                Token = request.Token,
                SignatureMethod = request.SignatureMethod,
                SignatureData = request.SignatureData,
                BulkSign = request.BulkSign,
                PeriodicTrainingId = request.PeriodicTrainingId,
                IpAddress = ipAddress
            });

            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            // If the employee just signed, notify the manager with their own signing link
            if (result.ManagerEmail != null)
            {
                var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                var managerSecureLink = $"{frontendUrl}/sign/{result.ManagerNotificationToken}";

                try
                {
                    await _emailService.SendDocumentSignatureEmailWithLinkAsync(
                        result.ManagerEmail,
                        result.ManagerNotificationDocumentName!,
                        managerSecureLink);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the signing operation
                    Console.WriteLine($"Warning: Failed to send email to manager {result.ManagerEmail}: {ex.Message}");
                }
            }

            var msg = result.TotalSigned > 1
                ? $"Successfully signed {result.TotalSigned} document(s)."
                : "Document successfully signed using secure link.";

            // Notify all connected clients that a signature was recorded so dashboards can refresh
            await _hubContext.Clients.All.SendAsync("SignatureUpdated");

            return Ok(new { message = msg, count = result.TotalSigned });
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

            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            bool isAdmin = User.IsInRole(Roles.Admin);
            bool isLineManager = User.IsInRole(Roles.LineManager);

            if (!isAdmin && !isLineManager)
                return Forbid();

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var count = await _documentService.BulkSignDocumentsAsync(
                isAdmin, userId, request.SignatureMethod, request.SignatureData, ipAddress);

            await _hubContext.Clients.All.SendAsync("SignatureUpdated");

            return Ok(new { message = $"Successfully signed {count} document(s).", count });
        }

        [HttpPost("bulk-sign-async")]
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
        public async Task<IActionResult> BulkSignAsync([FromBody] BulkSignDto request)
        {
            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            bool isAdmin = User.IsInRole(Roles.Admin);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            int total = await _documentService.GetPendingSsmDocumentsForAdminAsync();
            if (total == 0)
                return Ok(new { message = "No documents to sign.", jobId = (string?)null });

            string jobId = Guid.NewGuid().ToString();
            var progress = new BulkSignProgress { Total = total, Signed = 0, Completed = false };
            BulkSignJobs[jobId] = progress;

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
                        await Task.Delay(250); 
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
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
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

            if (User.GetUserId() is not { } userId)
                return Unauthorized();

            if (!User.IsInRole(Roles.Admin))
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
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
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
