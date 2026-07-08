using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.Notification;
using SyncApp26.Domain.Enums;
using SyncApp26.API.Extensions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly IDocumentService _documentService;
        private readonly IPeriodicTrainingService _periodicTrainingService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IConfiguration _configuration;

        public NotificationController(
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IPeriodicTrainingService periodicTrainingService,
            IDocumentSignatureService documentSignatureService,
            IConfiguration configuration)
        {
            _userService = userService;
            _emailService = emailService;
            _documentService = documentService;
            _periodicTrainingService = periodicTrainingService;
            _documentSignatureService = documentSignatureService;
            _configuration = configuration;
        }

        [HttpPost("notify-user/{userId}")]
        public async Task<IActionResult> NotifyUser(Guid userId, [FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = "DocumentType must be 'SSM' or 'SU'." });
            }

            // Check permissions: Only Admin or the user's AssingedTo (Line Manager) can notify
            var currentUserRole = User.GetRole();

            if (User.GetUserId() is not { } currentUserId)
            {
                return Unauthorized();
            }

            var targetUser = await _userService.GetUserByIdAsync(userId);
            if (targetUser == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            if (currentUserRole != Roles.Admin && targetUser.AssignedToId != currentUserId)
            {
                return Forbid("You do not have permission to notify this user.");
            }

            // Verify they actually need to sign it
            var unsignedIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync(request.DocumentType);
            if (!unsignedIds.Contains(userId))
            {
                var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);
                if (signedIds.Contains(userId))
                {
                    return BadRequest(new { Message = $"User has already signed the {request.DocumentType} document." });
                }
                return BadRequest(new { Message = $"User does not have an unsigned {request.DocumentType} document to sign." });
            }

            // Find training date from InitialTrainings (matching document type) or PeriodicTraining
            DateTime? trainingDate = null;
            var initialTraining = targetUser.InitialTrainings
                ?.FirstOrDefault(t => string.Equals(t.DocumentType, request.DocumentType, StringComparison.OrdinalIgnoreCase));
            if (initialTraining?.WorkplaceTrainingDate.HasValue == true)
            {
                trainingDate = initialTraining.WorkplaceTrainingDate;
            }
            else if (initialTraining?.IntroductoryTrainingDate.HasValue == true)
            {
                trainingDate = initialTraining.IntroductoryTrainingDate;
            }
            else
            {
                var trainings = await _periodicTrainingService.GetByUserIdAsync(userId);
                var latestTraining = trainings.OrderByDescending(t => t.TrainingDate).FirstOrDefault();
                trainingDate = latestTraining?.TrainingDate;
            }

            string? signLink = null;
            if (string.IsNullOrEmpty(targetUser.PasswordHash))
            {
                // Generate a one-time signing link for users without an account
                var userDocs = await _documentService.GetUserDocumentsAsync(userId);
                var pendingDoc = userDocs.FirstOrDefault(d =>
                    d.DocumentType == request.DocumentType && d.Status == "PendingUser");

                if (pendingDoc != null)
                {
                    var token = await _documentSignatureService.GenerateSignatureTokenAsync(
                        targetUser.Email,
                        pendingDoc.Id,
                        $"{request.DocumentType} Document");

                    var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
                    signLink = $"{frontendUrl}/sign/{token}";
                }
            }

            await _emailService.SendMissingSignatureToUserEmailAsync(
                targetUser.Email,
                targetUser.FirstName,
                request.DocumentType,
                trainingDate,
                signLink
            );

            return Ok(new { Message = "Notification sent successfully." });
        }

        [HttpPost("notify-manager/{managerId}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> NotifyManager(Guid managerId, [FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = "DocumentType must be 'SSM' or 'SU'." });
            }

            var manager = await _userService.GetUserByIdAsync(managerId);
            if (manager == null)
            {
                return NotFound(new { Message = "Line Manager not found." });
            }

            var assignedUsers = await _userService.GetUsersAssignedToAsync(managerId);
            if (!assignedUsers.Any())
            {
                return BadRequest(new { Message = "This line manager has no assigned users." });
            }

            var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);

            // Count how many users assigned to this manager are NOT in the signedIds list
            var unsignedCount = assignedUsers.Count(u => !signedIds.Contains(u.Id));

            if (unsignedCount == 0)
            {
                return BadRequest(new { Message = $"All users under this line manager have already signed the {request.DocumentType} document." });
            }

            await _emailService.SendMissingSignatureToManagerEmailAsync(
                manager.Email,
                $"{manager.FirstName} {manager.LastName}",
                request.DocumentType,
                unsignedCount
            );

            return Ok(new { Message = $"Notification sent successfully for {unsignedCount} missing signatures." });
        }

        [HttpPost("notify-all-managers")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> NotifyAllManagers([FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = "DocumentType must be 'SSM' or 'SU'." });
            }

            var allUsers = await _userService.GetAllUsersAsync();
            var managers = allUsers
                .Where(u => u.Role == UserRole.LineManager)
                .ToList();

            if (!managers.Any())
                return BadRequest(new { Message = "No active line managers found." });

            var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);
            int notifiedCount = 0;

            foreach (var manager in managers)
            {
                var assignedUsers = await _userService.GetUsersAssignedToAsync(manager.Id);
                var unsignedCount = assignedUsers.Count(u => !signedIds.Contains(u.Id));
                if (unsignedCount == 0) continue;

                await _emailService.SendMissingSignatureToManagerEmailAsync(
                    manager.Email,
                    $"{manager.FirstName} {manager.LastName}",
                    request.DocumentType,
                    unsignedCount
                );
                notifiedCount++;
            }

            if (notifiedCount == 0)
                return Ok(new { Message = $"All managers' teams have already signed the {request.DocumentType} document. No emails sent." });

            return Ok(new { Message = $"Notifications sent to {notifiedCount} line manager(s) with unsigned {request.DocumentType} documents." });
        }
    }
}
