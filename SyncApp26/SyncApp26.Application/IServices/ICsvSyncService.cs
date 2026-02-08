using SyncApp26.Shared.DTOs;

namespace SyncApp26.Application.IServices;

public interface ICsvSyncService
{
    Task<List<UserComparisonDTO>> CompareWithDatabase(IEnumerable<CsvUserDTO> csvUsers, int totalRows, string? connectionId = null);
    Task<SyncResultDTO> SyncUsers(SyncRequestDTO syncRequest, string? connectionId = null);
}