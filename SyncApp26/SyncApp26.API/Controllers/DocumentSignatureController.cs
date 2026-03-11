using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using System.Security.Claims;

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

        public DocumentSignatureController(
            IDocumentSignatureService documentSignatureService,
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IConfiguration configuration)
        {
            _documentSignatureService = documentSignatureService;
            _userService = userService;
            _emailService = emailService;
            _documentService = documentService;
            _configuration = configuration;
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

            // Determine whether the signer is acting as the employee or the manager.
            var document = await _documentService.GetDocumentByIdAsync(signatureToken.DocumentId);
            bool isManagerSigning = document?.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, signatureToken.Email, StringComparison.OrdinalIgnoreCase);

            // Return the necessary document info for the frontend to render the signing UI
            return Ok(new
            {
                documentId = signatureToken.DocumentId,
                documentName = signatureToken.DocumentName,
                email = signatureToken.Email,
                isManagerSigning = isManagerSigning
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

            bool isManagerSigning = document.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, tokenEntity.Email, StringComparison.OrdinalIgnoreCase);
            bool isUserSignature = !isManagerSigning;

            if (isUserSignature && document.UserSignedAt != null)
            {
                return BadRequest(new { message = "User already signed this document." });
            }

            if (!isUserSignature && document.ManagerSignedAt != null)
            {
                return BadRequest(new { message = "Manager already signed this document." });
            }

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);
            if (!isValidAndConsumed)
            {
                return BadRequest(new { message = "Token could not be consumed." });
            }

            // Record signature
            // A line manager can sign their employee's document regardless of whether the employee has signed yet.
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            await _documentService.UpdateDocumentSignatureAsync(
                document.Id,
                isUserSignature,
                request.SignatureMethod,
                request.SignatureData,
                ipAddress
            );

            // If user signed, generate link for the manager and send email
            if (isUserSignature && document.User?.AssignedTo != null && document.ManagerSignedAt == null)
            {
                var manager = document.User.AssignedTo;
                var managerToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                    manager.Email, 
                    document.Id, 
                    $"{document.DocumentType} Document (Manager Approval)");

                var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                var managerSecureLink = $"{frontendUrl}/sign/{managerToken}";

                await _emailService.SendDocumentSignatureEmailWithLinkAsync(
                    manager.Email, 
                    $"{document.DocumentType} Document (Manager Approval)", 
                    managerSecureLink);
            }

            // Bulk sign: apply the same signature to all other pending docs this signer is responsible for
            int bulkCount = 0;
            if (request.BulkSign && !isUserSignature)
            {
                var signerUser = await _userService.GetUserByEmailAsync(tokenEntity.Email);
                if (signerUser != null)
                {
                    bool signerIsAdmin = signerUser.Role?.Name == "Admin";
                    var ipAddress2 = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    bulkCount = await _documentService.BulkSignDocumentsAsync(
                        signerIsAdmin, signerUser.Id,
                        request.SignatureMethod, request.SignatureData, ipAddress2);
                }
            }

            var msg = bulkCount > 0
                ? $"Document successfully signed. {bulkCount} additional document(s) were signed with the same signature."
                : "Document successfully signed using secure link.";
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

            return Ok(new { message = $"Successfully signed {count} document(s).", count });
        }
    }
}
