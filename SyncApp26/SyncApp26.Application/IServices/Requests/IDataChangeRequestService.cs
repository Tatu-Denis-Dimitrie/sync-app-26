using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Shared.DTOs.DataChange;

namespace SyncApp26.Application.IServices
{
    public interface IDataChangeRequestService
    {
        Task<IEnumerable<DataChangeRequestDTO>> GetAllRequestsAsync();
        Task<IEnumerable<DataChangeRequestDTO>> GetRequestsByUserAsync(Guid userId);
        Task<DataChangeRequestDTO> GetRequestByIdAsync(Guid id);
        Task<DataChangeRequestDTO> CreateRequestAsync(Guid userId, CreateDataChangeRequestDTO dto, string initialStatus = "Pending");
        Task<DataChangeRequestDTO> ChangeStatusAsync(Guid id, string status);
        Task<DataChangeRequestDTO> ResolveRequestAsync(Guid id, Guid adminId, ResolveDataChangeRequestDTO dto);
    }
}
