using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.DataChange;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using SyncApp26.API.Services;
using SyncApp26.Domain.IRepositories;
using System.Text.Json;
using System.Collections.Generic;

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

        public DataChangeRequestController(
            IDataChangeRequestService service,
            IEmailService emailService,
            IDataChangeRequestRepository repository)
        {
            _service = service;
            _emailService = emailService;
            _repository = repository;
        }

        private Guid GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdStr, out var userId) ? userId : Guid.Empty;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var requests = await _service.GetAllRequestsAsync();
            return Ok(requests);
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
                if (changes != null && changes.ContainsKey("Email")) 
                {
                    // Explicitly block email changes from being requested
                    changes.Remove("Email");
                    dto.RequestedChangesJson = JsonSerializer.Serialize(changes);
                    
                    // If Email was the only change, we might want to return early or just let the service handle it (it will likely fail validation if empty)
                    if (changes.Count == 0)
                    {
                        return BadRequest(new { message = "Email changes are no longer allowed. Please provide other fields to change." });
                    }
                }
            } 
            catch { }

            var result = await _service.CreateRequestAsync(userId, dto, status);
            return Ok(result);
        }

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
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveDataChangeRequestDTO dto)
        {
            var adminId = GetUserId();
            try
            {
                var result = await _service.ResolveRequestAsync(id, adminId, dto);
                
                if (dto.Status == "Approved")
                {
                    var user = await _repository.GetUserByIdAsync(result.UserId);
                    if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        var emailHtml = $"<p>Hello {user.FirstName},</p><p>Your data change request submitted on {result.CreatedAt:d} has been approved and applied to your profile by our administrators.</p>";
                        await _emailService.SendEmailAsync(user.Email, "Data Change Request Approved", emailHtml);
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
