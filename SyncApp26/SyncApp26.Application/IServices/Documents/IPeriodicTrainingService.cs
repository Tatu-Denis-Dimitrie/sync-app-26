using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
using SyncApp26.Shared.DTOs.Response.PeriodicTraining;

namespace SyncApp26.Application.IServices
{
    public interface IPeriodicTrainingService
    {
        Task<PeriodicTrainingResponseDTO> CreateAsync(CreatePeriodicTrainingDTO dto);
        Task<PeriodicTrainingResponseDTO?> GetByIdAsync(Guid id);
        Task<IEnumerable<PeriodicTrainingResponseDTO>> GetByUserIdAsync(Guid userId);
        Task<PeriodicTrainingResponseDTO> UpdateAsync(Guid id, UpdatePeriodicTrainingDTO dto);
        Task<bool> DeleteAsync(Guid id);
        Task<BulkCreateResultDTO> BulkCreateAsync(BulkCreatePeriodicTrainingDTO dto, Guid? restrictToAssignedToId = null);
    }
}
