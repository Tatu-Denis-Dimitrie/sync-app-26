using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
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
        private readonly ILogger<NotificationController> _logger;
        private readonly IStringLocalizer _localizer;

        public NotificationController(
            IUserService userService,
            IEmailService emailService,
            IDocumentService documentService,
            IPeriodicTrainingService periodicTrainingService,
            IDocumentSignatureService documentSignatureService,
            IConfiguration configuration,
            ILogger<NotificationController> logger,
            ILocalizationService localizationService)
        {
            _userService = userService;
            _emailService = emailService;
            _documentService = documentService;
            _periodicTrainingService = periodicTrainingService;
            _documentSignatureService = documentSignatureService;
            _configuration = configuration;
            _logger = logger;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Documents);
        }

        [HttpPost("notify-user/{userId}")]
        public async Task<IActionResult> NotifyUser(Guid userId, [FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = _localizer["api.documentTypeMustBeSsmOrSu"].Value });
            }

            // Check permissions: Only Admin or the user's AssingedTo (Line Manager) can notify
            if (User.GetUserId() is not { } currentUserId)
            {
                return Unauthorized();
            }

            var targetUser = await _userService.GetUserByIdAsync(userId);
            if (targetUser == null)
            {
                return NotFound(new { Message = _localizer["api.userNotFound"].Value });
            }

            if (!User.IsInRole(Roles.Admin) && targetUser.AssignedToId != currentUserId)
            {
                return Forbid(_localizer["notification.noPermission"].Value);
            }

            // Verify they actually need to sign it
            var unsignedIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync(request.DocumentType);
            if (!unsignedIds.Contains(userId))
            {
                var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);
                if (signedIds.Contains(userId))
                {
                    return BadRequest(new { Message = _localizer["notification.alreadySignedDoc", request.DocumentType].Value });
                }
                return BadRequest(new { Message = _localizer["notification.noUnsignedDoc", request.DocumentType].Value });
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

            return Ok(new { Message = _localizer["notification.sent"].Value });
        }

        [HttpPost("notify-manager/{managerId}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> NotifyManager(Guid managerId, [FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = _localizer["api.documentTypeMustBeSsmOrSu"].Value });
            }

            var manager = await _userService.GetUserByIdAsync(managerId);
            if (manager == null)
            {
                return NotFound(new { Message = _localizer["notification.lineManagerNotFound"].Value });
            }

            var assignedUsers = await _userService.GetUsersAssignedToAsync(managerId);
            if (!assignedUsers.Any())
            {
                return BadRequest(new { Message = _localizer["notification.noAssignedUsers"].Value });
            }

            var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);

            // Count how many users assigned to this manager are NOT in the signedIds list
            var unsignedCount = assignedUsers.Count(u => !signedIds.Contains(u.Id));

            if (unsignedCount == 0)
            {
                return BadRequest(new { Message = _localizer["notification.allTeamSigned", request.DocumentType].Value });
            }

            await _emailService.SendMissingSignatureToManagerEmailAsync(
                manager.Email,
                $"{manager.FirstName} {manager.LastName}",
                request.DocumentType,
                unsignedCount
            );

            return Ok(new { Message = _localizer["notification.sentForMissing", unsignedCount].Value });
        }

        [HttpPost("notify-all-managers")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> NotifyAllManagers([FromBody] NotificationRequestDTO request)
        {
            if (string.IsNullOrWhiteSpace(request.DocumentType) ||
                (request.DocumentType != "SSM" && request.DocumentType != "SU"))
            {
                return BadRequest(new { Message = _localizer["api.documentTypeMustBeSsmOrSu"].Value });
            }

            var managers = (await _userService.GetUsersInRoleAsync(Roles.LineManager)).ToList();

            if (!managers.Any())
                return BadRequest(new { Message = _localizer["notification.noActiveLineManagers"].Value });

            var signedIds = await _documentService.GetUserIdsWithDocumentTypeAsync(request.DocumentType);
            int notifiedCount = 0;

            foreach (var manager in managers)
            {
                var assignedUsers = await _userService.GetUsersAssignedToAsync(manager.Id);
                var unsignedCount = assignedUsers.Count(u => !signedIds.Contains(u.Id));
                if (unsignedCount == 0) continue;

                // One manager's email failing (e.g. SMTP daily limit) must not abort notifications
                // to the rest — each send is independent.
                try
                {
                    await _emailService.SendMissingSignatureToManagerEmailAsync(
                        manager.Email,
                        $"{manager.FirstName} {manager.LastName}",
                        request.DocumentType,
                        unsignedCount
                    );
                    notifiedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send missing-signature notification to manager {ManagerId}.", manager.Id);
                }
            }

            if (notifiedCount == 0)
                return Ok(new { Message = _localizer["notification.allManagersTeamsSigned", request.DocumentType].Value });

            return Ok(new { Message = _localizer["notification.sentToManagers", notifiedCount, request.DocumentType].Value });
        }
    }
}
