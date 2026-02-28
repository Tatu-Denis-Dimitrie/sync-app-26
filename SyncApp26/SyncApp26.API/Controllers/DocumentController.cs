using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.API.Services;
using SyncApp26.Domain.Entities;
using System.Security.Claims;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly IEmailService _emailService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;

        public DocumentController(
            IDocumentService documentService,
            IEmailService emailService,
            IDocumentSignatureService documentSignatureService,
            IUserService userService,
            IConfiguration configuration)
        {
            _documentService = documentService;
            _emailService = emailService;
            _documentSignatureService = documentSignatureService;
            _userService = userService;
            _configuration = configuration;
        }

        public class GenerateDocumentDto
        {
            public Guid UserId { get; set; }
            public string DocumentType { get; set; } = string.Empty;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateDocument([FromBody] GenerateDocumentDto request)
        {
            try
            {
                var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "admin@syncapp26.com";

                var document = await _documentService.GenerateDocumentAsync(request.UserId, request.DocumentType, adminEmail);

                // Now we need to send the signature request to the user.
                // Assuming we get the user's email from the generated document...
                var fullDocument = await _documentService.GetDocumentByIdAsync(document.Id);
                var userEmail = fullDocument?.User?.Email;

                if (!string.IsNullOrEmpty(userEmail))
                {
                    var token = await _documentSignatureService.GenerateSignatureTokenAsync(userEmail, document.Id, $"{document.DocumentType} Document");
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
            var documents = await _documentService.GetUserDocumentsAsync(userId);
            return Ok(documents);
        }

        [HttpGet("my-pending-signatures")]
        public async Task<IActionResult> GetMyPendingSignatures()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            // Fetch documents where user is the employee and status is PendingUser
            var myDocuments = await _documentService.GetUserDocumentsAsync(userId);
            var pendingAsUser = myDocuments.Where(d => d.Status == "PendingUser");

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            return Ok(pendingAsUser);
        }

        [HttpGet("manager-pending-signatures")]
        public async Task<IActionResult> GetManagerPendingSignatures()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            // Fetch documents where the current user is the manager and status is PendingManager
            // Since IDocumentService doesn't have this, we can get all users where AssignedToId is this user, then their documents
            // This would be better handled in IDocumentService, but we can do a quick implementation here
            var allManagedUsers = await _userService.GetAllUsersAsync();
            var myEmployees = allManagedUsers.Where(u => u.AssignedToId == userId).Select(u => u.Id).ToList();

            var pendingAsManager = new List<UserDocument>();
            foreach (var empId in myEmployees)
            {
                var empDocs = await _documentService.GetUserDocumentsAsync(empId);
                pendingAsManager.AddRange(empDocs.Where(d => d.Status == "PendingManager"));
            }

            return Ok(pendingAsManager);
        }

        [HttpGet("my-signed-documents")]
        public async Task<IActionResult> GetMySignedDocuments()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            var myDocuments = await _documentService.GetUserDocumentsAsync(userId);
            var signedAsUser = myDocuments.Where(d => d.UserSignedAt != null);

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            return Ok(signedAsUser);
        }

        [HttpGet("manager-signed-documents")]
        public async Task<IActionResult> GetManagerSignedDocuments()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            var allManagedUsers = await _userService.GetAllUsersAsync();
            var myEmployees = allManagedUsers.Where(u => u.AssignedToId == userId).Select(u => u.Id).ToList();

            var signedAsManager = new List<UserDocument>();
            foreach (var empId in myEmployees)
            {
                var empDocs = await _documentService.GetUserDocumentsAsync(empId);
                signedAsManager.AddRange(empDocs.Where(d => d.ManagerSignedAt != null));
            }

            return Ok(signedAsManager);
        }

        [HttpGet("token-for-document/{documentId}")]
        public async Task<IActionResult> GetSignTokenForDocument(Guid documentId)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            var document = await _documentService.GetDocumentByIdAsync(documentId);
            if (document == null) return NotFound(new { message = "Document not found." });

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            bool isUser = document.UserId == userId;
            bool isManager = document.User?.AssignedToId == userId;

            if (!isUser && !isManager)
                return Forbid();

            if (isUser && document.Status != "PendingUser")
                return BadRequest(new { message = "User signature not required at this time." });
            
            if (isManager && document.Status != "PendingManager")
                return BadRequest(new { message = "Manager signature not required at this time." });

            var token = await _documentSignatureService.GenerateSignatureTokenAsync(user.Email, document.Id, $"{document.DocumentType} Document");

            return Ok(new { token });
        }
    }
}
