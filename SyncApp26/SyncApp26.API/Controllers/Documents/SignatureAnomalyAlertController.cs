using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.API.Extensions;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/signature-anomaly-alerts")]
    [Authorize(Roles = Roles.Admin)]
    public class SignatureAnomalyAlertController : ControllerBase
    {
        private readonly ISignatureAnomalyAlertRepository _alertRepository;

        public SignatureAnomalyAlertController(ISignatureAnomalyAlertRepository alertRepository)
        {
            _alertRepository = alertRepository;
        }

        /// <summary>Every sweep run with anomalies the caller hasn't dismissed yet, newest first — lets an admin see alerts a live SignalR push would have missed (e.g. logging in after the sweep already fired).</summary>
        [HttpGet("unread")]
        public async Task<IActionResult> GetUnread()
        {
            var unread = await _alertRepository.GetUnreadAsync();

            var result = unread.Select(a => new SignatureAnomalyAlertDTO
            {
                Id = a.Id,
                RecordsChecked = a.RecordsChecked,
                AnomaliesFound = a.AnomaliesFound,
                OccurredAt = a.OccurredAt
            });

            return Ok(result);
        }

        /// <summary>Marks every currently-unread alert as read by the caller — mirrors the single "Got it" dismissal in the header UI.</summary>
        [HttpPost("dismiss-all")]
        public async Task<IActionResult> DismissAll()
        {
            if (User.GetUserId() is not { } callerId)
                return Unauthorized();

            await _alertRepository.MarkAllAsReadAsync(callerId);
            return NoContent();
        }
    }
}
