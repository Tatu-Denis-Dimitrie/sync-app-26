using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.API.Services;
using SyncApp26.Domain.Entities;
using System.Security.Claims;

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

        // Flat DTO — avoids serializing deep User navigation property chains
        private static object MapDocument(UserDocument d) => new
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
            d.AdminSignatureMethod,
            d.AdminSignatureData,
            d.AdminSignatureIpAddress,
            d.AdminSignedAt,
        };

        private static bool IsSsmManagerSignaturePending(UserDocument d)
        {
            if (!string.Equals(d.DocumentType, "SSM", StringComparison.OrdinalIgnoreCase))
                return false;

            var latestTraining = d.User?.PeriodicTrainings
                ?.OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();

            return latestTraining != null && string.IsNullOrEmpty(latestTraining.InstructorSignature);
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

            var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "admin@syncapp26.com";
            var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";

            var types = request.DocumentType.Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? new[] { "SSM", "SU" }
                : new[] { request.DocumentType.ToUpper() };

            var isAdmin = User.IsInRole("Admin");
            var currentUserIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!isAdmin && Guid.TryParse(currentUserIdString, out var currentUserId))
            {
                var allUsers = await _userService.GetAllUsersAsync();
                var myEmployees = allUsers
                    .Where(u => u.AssignedToId == currentUserId)
                    .Select(u => u.Id)
                    .ToList();

                if (request.SelectedUserIds == null || !request.SelectedUserIds.Any())
                {
                    request.SelectedUserIds = myEmployees;
                }
                else
                {
                    request.SelectedUserIds = request.SelectedUserIds.Intersect(myEmployees).ToList();
                }
            }

            int totalGenerated = 0, totalSkipped = 0;

            foreach (var type in types)
            {
                var (generated, skipped) = await _documentService.BulkGenerateDocumentsAsync(type, adminEmail, request.SelectedUserIds);
                totalGenerated += generated;
                totalSkipped += skipped;
            }

            // Send signature request emails to all employees with pending documents
            int emailsSent = 0;
            foreach (var type in types)
            {
                var pendingDocs = await _documentService.GetAllPendingUserDocumentsAsync(type);
                foreach (var doc in pendingDocs)
                {
                    if (doc.User?.Email is { Length: > 0 } userEmail && doc.UserSignedAt == null)
                    {
                        try
                        {
                            var currentRowId = await _documentService.GetCurrentTrainingIdForDocumentAsync(doc.Id);
                            var token = await _documentSignatureService.GenerateSignatureTokenAsync(
                                userEmail, doc.Id, $"{type} Document", currentRowId);
                            var link = $"{frontendUrl}/sign/{token}";
                            await _emailService.SendDocumentSignatureEmailWithLinkAsync(userEmail, $"{type} Document", link);
                            emailsSent++;
                        }
                        catch { /* non-fatal per user */ }
                    }
                }
            }

            var message = $"Bulk generation complete. {totalGenerated} document(s) generated, {totalSkipped} skipped. {emailsSent} signature request(s) sent to employees.";

            return Ok(new
            {
                message,
                generated = totalGenerated,
                skipped = totalSkipped
            });
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateDocument([FromBody] GenerateDocumentDto request)
        {
            try
            {
                var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "admin@syncapp26.com";

                var user = await _userService.GetUserByIdAsync(request.UserId);
                if (user == null) return NotFound(new { message = "User not found." });

                var currentUserIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                bool isAdmin = User.IsInRole("Admin");
                if (!isAdmin && Guid.TryParse(currentUserIdString, out var currentUserId))
                {
                    if (user.AssignedToId != currentUserId)
                    {
                        return Forbid();
                    }
                }

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
            var documents = await _documentService.GetUserDocumentsAsync(userId);
            return Ok(documents.Select(MapDocument));
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllDocuments()
        {
            var allDocs = await _documentService.GetAllDocumentsAsync();
            var documents = allDocs.AsEnumerable();

            var isAdmin = User.IsInRole("Admin");
            var currentUserIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!isAdmin && Guid.TryParse(currentUserIdString, out var currentUserId))
            {
                documents = documents.Where(d => d.User?.AssignedToId == currentUserId || d.UserId == currentUserId);
            }

            return Ok(documents.Select(MapDocument));
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

            return Ok(pendingAsUser.Select(MapDocument));
        }

        [HttpGet("manager-pending-signatures")]
        public async Task<IActionResult> GetManagerPendingSignatures()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            // Fetch documents where the current user is the manager and employee has already signed.
            // Line managers can only sign after employee signature.
            var allManagedUsers = await _userService.GetAllUsersAsync();
            var myEmployees = allManagedUsers.Where(u => u.AssignedToId == userId).Select(u => u.Id).ToList();

            var pendingAsManager = new List<UserDocument>();
            foreach (var empId in myEmployees)
            {
                var empDocs = await _documentService.GetUserDocumentsAsync(empId);
                pendingAsManager.AddRange(empDocs.Where(d =>
                    d.Status == "PendingManager" &&
                    d.UserSignedAt != null &&
                    d.ManagerSignedAt == null));
            }

            return Ok(pendingAsManager.Select(MapDocument));
        }

        [HttpGet("my-signed-documents")]
        public async Task<IActionResult> GetMySignedDocuments()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            var myDocuments = await _documentService.GetUserDocumentsAsync(userId);
            var signedAsUser = myDocuments.Where(d => d.UserSignedAt != null);

            return Ok(signedAsUser.Select(MapDocument));
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

            return Ok(signedAsManager.Select(MapDocument));
        }

        /// <summary>
        /// Returns SSM documents pending admin signature (PendingAdmin status — signed by both employee and LM).
        /// </summary>
        [HttpGet("admin-pending-signatures")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminPendingSignatures()
        {
            var docs = await _documentService.GetAdminPendingDocumentsAsync();
            return Ok(docs.Select(MapDocument));
        }

        /// <summary>
        /// Returns SSM documents already signed by admin (Completed status).
        /// </summary>
        [HttpGet("admin-signed-documents")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminSignedDocuments()
        {
            var docs = await _documentService.GetAdminSignedDocumentsAsync();
            return Ok(docs.Select(MapDocument));
        }

        [HttpPost("regenerate-documents")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RegenerateDocuments()
        {
            var count = await _documentService.RegenerateDocumentsAsync();
            return Ok(new
            {
                message = $"S-au regenerat {count} document(e) în folderul GeneratedDocuments.",
                regenerated = count
            });
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
            bool isAdmin = User.IsInRole("Admin");

            if (!isUser && !isManager && !isAdmin)
                return Forbid();

            if (isUser && document.UserSignedAt != null)
                return BadRequest(new { message = "User already signed this document." });

            if (isManager && document.ManagerSignedAt != null)
                return BadRequest(new { message = "Manager already signed this document." });

            // Enforce sequential signing: employee must sign before manager
            if (isManager && !isAdmin && document.UserSignedAt == null)
                return BadRequest(new { message = "Employee must sign first before manager can countersign." });

            if (isUser && document.Status != "PendingUser")
                return BadRequest(new { message = "User signature not required at this time." });

            if (isManager && !isAdmin && document.Status != "PendingManager")
                return BadRequest(new { message = "Manager signature not required at this time." });

            // Admin can only sign SSM documents in PendingAdmin status
            if (isAdmin && !isUser && !isManager)
            {
                if (document.Status != "PendingAdmin")
                    return BadRequest(new { message = "Admin signature not required at this time." });
                if (document.DocumentType?.ToUpperInvariant() != "SSM")
                    return BadRequest(new { message = "Admin only signs SSM documents." });
            }
            var currentRowId = await _documentService.GetCurrentTrainingIdForDocumentAsync(document.Id);
            var token = await _documentSignatureService.GenerateSignatureTokenAsync(user.Email, document.Id, $"{document.DocumentType} Document", currentRowId);

            return Ok(new { token });
        }

        /// <summary>
        /// Generates and streams the PDF for a document on-the-fly (includes embedded
        /// digital signatures if already signed). Accessible by the document owner,
        /// their line manager, or an admin.
        /// </summary>
        [HttpGet("{documentId}/view-pdf")]
        public async Task<IActionResult> ViewPdf(Guid documentId)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized();

            var document = await _documentService.GetDocumentByIdAsync(documentId);
            if (document == null) return NotFound(new { message = "Document not found." });

            bool isDocOwner = document.UserId == userId;
            bool isManager = document.User?.AssignedToId == userId;
            bool isAdmin = User.IsInRole("Admin");

            if (!isDocOwner && !isManager && !isAdmin)
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
}
