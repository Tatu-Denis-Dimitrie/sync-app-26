using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Shared.DTOs.DataChange;

namespace SyncApp26.Application.IServices
{
    public interface IDataChangeRequestService
    {
        /// <summary>Fields the self-service "request a change" flow may ever touch (matches the UI's own field list). Excludes Email, which has its own dedicated endpoint.</summary>
        IReadOnlyCollection<string> AllowedFields { get; }

        Task<IEnumerable<DataChangeRequestDTO>> GetAllRequestsAsync();
        Task<int> GetPendingCountAsync();
        Task<IEnumerable<DataChangeRequestDTO>> GetRequestsByUserAsync(Guid userId);
        Task<DataChangeRequestDTO> GetRequestByIdAsync(Guid id);
        /// <summary>allowEmailField: only RequestEmailChangeAsync's own call should ever pass true.</summary>
        Task<DataChangeRequestDTO> CreateRequestAsync(Guid userId, CreateDataChangeRequestDTO dto, string initialStatus = "Pending", bool allowEmailField = false);
        Task<DataChangeRequestDTO> ChangeStatusAsync(Guid id, string status);
        Task<DataChangeRequestDTO> ResolveRequestAsync(Guid id, Guid adminId, ResolveDataChangeRequestDTO dto);
        Task<AccountActionResult<DataChangeRequestDTO>> RequestEmailChangeAsync(Guid userId, RequestEmailChangeDTO dto);
    }
}
