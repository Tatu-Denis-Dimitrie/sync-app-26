using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.Notification;
using System.Security.Claims;

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

        public NotificationController(
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IPeriodicTrainingService periodicTrainingService)
        {
            _userService = userService;
            _emailService = emailService;
            _documentService = documentService;
            _periodicTrainingService = periodicTrainingService;
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
            var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(currentUserIdStr) || !Guid.TryParse(currentUserIdStr, out var currentUserId))
            {
                return Unauthorized();
            }

            var targetUser = await _userService.GetUserByIdAsync(userId);
            if (targetUser == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            if (currentUserRole != "Admin" && targetUser.AssignedToId != currentUserId)
            {
                return Forbid("You do not have permission to notify this user.");
            }

            // Verify they actually need to sign it
            var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);
            if (signedIds.Contains(userId))
            {
                return BadRequest(new { Message = $"User has already signed the {request.DocumentType} document." });
            }

            // Find training date: try WorkplaceTrainingDate, IntroductoryTrainingDate, or PeriodicTraining
            DateTime? trainingDate = null;
            if (targetUser.WorkplaceTrainingDate.HasValue)
            {
                trainingDate = targetUser.WorkplaceTrainingDate;
            }
            else if (targetUser.IntroductoryTrainingDate.HasValue)
            {
                trainingDate = targetUser.IntroductoryTrainingDate;
            }
            else
            {
                var trainings = await _periodicTrainingService.GetByUserIdAsync(userId);
                var latestTraining = trainings.OrderByDescending(t => t.TrainingDate).FirstOrDefault();
                trainingDate = latestTraining?.TrainingDate;
            }

            await _emailService.SendMissingSignatureToUserEmailAsync(
                targetUser.Email,
                targetUser.FirstName,
                request.DocumentType,
                trainingDate
            );

            return Ok(new { Message = "Notification sent successfully." });
        }

        [HttpPost("notify-manager/{managerId}")]
        [Authorize(Roles = "Admin")]
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
    }
}
