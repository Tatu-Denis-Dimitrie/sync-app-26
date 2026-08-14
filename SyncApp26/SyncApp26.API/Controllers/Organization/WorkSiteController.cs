using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.WorkSite;
using SyncApp26.Shared.DTOs.Response.WorkSite;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WorkSiteController : ControllerBase
    {
        private readonly IWorkSiteService _workSiteService;

        public WorkSiteController(IWorkSiteService workSiteService)
        {
            _workSiteService = workSiteService;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<WorkSiteGETResponseDTO>> GetWorkSiteById(Guid id)
        {
            var workSite = await _workSiteService.GetWorkSiteByIdAsync(id);
            if (workSite == null)
            {
                return NotFound();
            }
            return Ok(new WorkSiteGETResponseDTO
            {
                Id = workSite.Id,
                Name = workSite.Name,
                IsActive = workSite.IsActive
            });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<WorkSiteGETResponseDTO>>> GetAllWorkSites()
        {
            var workSites = await _workSiteService.GetAllWorkSitesAsync();
            return Ok(workSites.Select(w => new WorkSiteGETResponseDTO
            {
                Id = w.Id,
                Name = w.Name,
                IsActive = w.IsActive
            }));
        }

        [HttpGet("scheduled-for-deletion")]
        public async Task<ActionResult<IEnumerable<WorkSiteGETResponseDTO>>> GetScheduledForDeletionWorkSites()
        {
            var workSites = await _workSiteService.GetDeletedWorkSitesAsync();
            return Ok(workSites.Select(w => new WorkSiteGETResponseDTO
            {
                Id = w.Id,
                Name = w.Name,
                IsActive = w.IsActive,
                DeletedAt = w.DeletedAt
            }));
        }

        [HttpPost("{id}/restore")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<WorkSiteResponseDTO>> RestoreWorkSite(Guid id)
        {
            var existingWorkSite = await _workSiteService.GetDeletedWorkSiteByIdAsync(id);
            if (existingWorkSite == null)
            {
                return new WorkSiteResponseDTO
                {
                    Success = false,
                    Message = "Scheduled for deletion work site not found",
                };
            }

            existingWorkSite.DeletedAt = null;
            existingWorkSite.IsActive = false; // Restore as inactive by default
            existingWorkSite.UpdatedAt = DateTime.UtcNow;

            await _workSiteService.UpdateWorkSiteAsync(existingWorkSite);

            return new WorkSiteResponseDTO
            {
                Success = true,
                Message = "Work site successfully restored"
            };
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<WorkSiteResponseDTO>> AddWorkSite([FromBody] WorkSiteRequestDTO workSiteRequestDTO)
        {
            var workSite = new WorkSite
            {
                Id = Guid.NewGuid(),
                Name = workSiteRequestDTO.Name.Trim(),
                IsActive = workSiteRequestDTO.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await _workSiteService.AddWorkSiteAsync(workSite);

            return new WorkSiteResponseDTO
            {
                Success = true,
                Message = "Work site created successfully",
            };
        }

        [HttpPut("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<WorkSiteResponseDTO>> UpdateWorkSite(Guid id, [FromBody] WorkSiteRequestDTO workSiteRequestDTO)
        {
            if (string.IsNullOrEmpty(workSiteRequestDTO.Name))
            {
                return new WorkSiteResponseDTO
                {
                    Success = false,
                    Message = "Work site name is required",
                };
            }

            var workSite = new WorkSite
            {
                Id = id,
                Name = workSiteRequestDTO.Name.Trim(),
                IsActive = workSiteRequestDTO.IsActive,
                UpdatedAt = DateTime.UtcNow
            };

            await _workSiteService.UpdateWorkSiteAsync(workSite);
            return new WorkSiteResponseDTO
            {
                Success = true,
                Message = "Work site updated successfully",
            };
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<WorkSiteResponseDTO>> DeleteWorkSite(Guid id, [FromServices] IUserService userService)
        {
            var existingWorkSite = await _workSiteService.GetWorkSiteByIdAsync(id);
            if (existingWorkSite == null)
            {
                return new WorkSiteResponseDTO
                {
                    Success = false,
                    Message = "Work site not found",
                };
            }

            // Unlike Department, a user with no work site is a valid state, so assigned users are
            // simply unassigned rather than requiring a mandatory transfer target.
            var usersAtWorkSite = await userService.GetUsersByWorkSiteIdAsync(id);
            foreach (var user in usersAtWorkSite)
            {
                user.WorkSiteId = null;
                await userService.UpdateUserAsync(user);
            }

            existingWorkSite.IsActive = false;
            existingWorkSite.DeletedAt = DateTime.UtcNow;
            await _workSiteService.UpdateWorkSiteAsync(existingWorkSite);

            return new WorkSiteResponseDTO
            {
                Success = true,
                Message = "Work site deleted/deactivated successfully",
            };
        }
    }
}
