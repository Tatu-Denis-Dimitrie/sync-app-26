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
        private readonly IConfiguration _configuration;

        public DocumentSignatureController(
            IDocumentSignatureService documentSignatureService,
            IUserService userService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _documentSignatureService = documentSignatureService;
            _userService = userService;
            _emailService = emailService;
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

            // Return the necessary document info for the frontend to render the signing UI
            return Ok(new
            {
                documentId = signatureToken.DocumentId,
                documentName = signatureToken.DocumentName,
                email = signatureToken.Email
            });
        }

        public class ConsumeTokenDto
        {
            public string Token { get; set; } = string.Empty;
            // The actual payload containing form data (like checkmarks, base64 signature, etc)
            // will be added here once the SSM/SU documents are fully modeled.
            // public DocumentSignaturePayload Payload { get; set; }
        }

        [HttpPost("consume-token")]
        public async Task<IActionResult> ConsumeToken([FromBody] ConsumeTokenDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { message = "Token is required." });
            }

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);

            if (!isValidAndConsumed)
            {
                return BadRequest(new { message = "Token is invalid, expired, or already used." });
            }

            // Future logic to save the document form answers/signature to the DB using the request payload
            
            return Ok(new { message = "Document successfully signed using secure link." });
        }
    }
}
