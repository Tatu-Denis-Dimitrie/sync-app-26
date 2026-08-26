using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.DataChange;
using System;
using System.Threading.Tasks;
using SyncApp26.API.Services;
using SyncApp26.API.Extensions;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Enums;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DataChangeRequestController : ControllerBase
    {
        private readonly IDataChangeRequestService _service;
        private readonly IEmailService _emailService;
        private readonly IDataChangeRequestRepository _repository;
        private readonly ILogger<DataChangeRequestController> _logger;

        public DataChangeRequestController(
            IDataChangeRequestService service,
            IEmailService emailService,
            IDataChangeRequestRepository repository,
            ILogger<DataChangeRequestController> logger)
        {
            _service = service;
            _emailService = emailService;
            _repository = repository;
            _logger = logger;
        }

        private Guid GetUserId()
        {
            return User.GetUserId() ?? Guid.Empty;
        }

        [HttpGet]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetAll()
        {
            var requests = await _service.GetAllRequestsAsync();
            return Ok(requests);
        }

        [HttpGet("pending-count")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetPendingCount()
        {
            var count = await _service.GetPendingCountAsync();
            return Ok(new { count });
        }

        [HttpGet("my-requests")]
        public async Task<IActionResult> GetMyRequests()
        {
            var userId = GetUserId();
            var requests = await _service.GetRequestsByUserAsync(userId);
            return Ok(requests);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDataChangeRequestDTO dto)
        {
            var userId = GetUserId();
            var status = "Pending";

            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, string>>(dto.RequestedChangesJson);
                if (changes != null)
                {
                    // Friendlier, earlier version of the allowlist check DataChangeRequestService
                    // itself enforces before persisting anything - this just gives a clear 400
                    // instead of the request silently ending up empty.
                    var disallowed = changes.Keys.Where(k => !_service.AllowedFields.Contains(k)).ToList();
                    if (disallowed.Count > 0)
                    {
                        foreach (var key in disallowed) changes.Remove(key);
                        dto.RequestedChangesJson = JsonSerializer.Serialize(changes);
                        if (changes.Count == 0)
                        {
                            return BadRequest(new { message = $"These fields cannot be requested via this flow: {string.Join(", ", disallowed)}." });
                        }
                    }
                }
            }
            catch { }

            var result = await _service.CreateRequestAsync(userId, dto, status);
            return Ok(result);
        }

        [HttpPost("request-email-change")]
        [Authorize(Roles = Roles.BasicUser + "," + Roles.LineManager)]
        public async Task<IActionResult> RequestEmailChange([FromBody] RequestEmailChangeDTO dto)
        {
            var userId = GetUserId();
            var result = await _service.RequestEmailChangeAsync(userId, dto);
            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(result.Data);
        }

        // Dead scaffold from an earlier, abandoned design (self-service email change gated on an
        // emailed confirmation link) - left in place unused. See the "self-service email change"
        // plan doc for why the shipped design (same-domain + admin approval, no inbox verification)
        // doesn't call this.
        [HttpGet("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmailChange([FromQuery] Guid reqId, [FromQuery] string token)
        {
            var req = await _service.GetRequestByIdAsync(reqId);
            if (req == null) return BadRequest(new { message = "Request not found" });
            if (req.Status != "Awaiting Verification") return BadRequest(new { message = "Request is already verified or processed." });

            var user = await _repository.GetUserByIdAsync(req.UserId);
            if (user == null || user.EmailVerificationToken != token || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
                return BadRequest(new { message = "Invalid or expired token." });

            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiresAt = null;
            await _repository.UpdateUserAsync(user);

            await _service.ChangeStatusAsync(reqId, "Pending");

            return Ok(new { message = "Email confirmed successfully. Your request is now pending admin approval." });
        }

        [HttpPut("{id}/resolve")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveDataChangeRequestDTO dto)
        {
            var adminId = GetUserId();
            DataChangeRequestDTO result;
            try
            {
                result = await _service.ResolveRequestAsync(id, adminId, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve data change request {RequestId}.", id);
                return BadRequest(new { message = ex.Message });
            }

            string? emailError = null;
            if (dto.Status == "Approved")
            {
                try
                {
                    var user = await _repository.GetUserByIdAsync(result.UserId);
                    if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        var emailHtml = $"<p>Hello {user.FirstName},</p><p>Your data change request submitted on {result.CreatedAt:d} has been approved and applied to your profile by our administrators.</p>";
                        await _emailService.SendEmailAsync(user.Email, "Data Change Request Approved", emailHtml);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Data change request {RequestId} approved but the notification email failed.", id);
                    emailError = ex.Message;
                }
            }

            return Ok(new { request = result, emailError });
        }
    }
}
