using SyncApp26.Shared.DTOs;
using SyncApp26.Shared.DTOs.CSV.Department;

namespace SyncApp26.Application.IServices
{
    public interface ICsvSyncService
    {
        Task<List<UserComparisonDTO>> CompareWithDatabase(List<CsvUserDTO> csvUsers);
        Task<SyncResultDTO> SyncUsers(SyncRequestDTO syncRequest);
        Task<List<CSVDepartmentComparisionDTO>> CompareDepartmentsWithDatabase(List<CSVDepartmentDTO> csvDepartments);
        Task<SyncResultDTO> SyncDepartments(List<CSVDepartmentComparisionDTO> departmentSyncList);
    }
}