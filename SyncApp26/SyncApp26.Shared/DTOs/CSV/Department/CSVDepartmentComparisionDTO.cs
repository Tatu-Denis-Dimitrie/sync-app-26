using SyncApp26.Shared.DTOs.Response.Department;

namespace SyncApp26.Shared.DTOs.CSV.Department
{
    public class CSVDepartmentComparisionDTO
    {
        public CSVDepartmentDTO? CsvDepartment { get; set; }
        public DepartmentGETResponseDTO? DbDepartment { get; set; }
        public string Status { get; set; } // "New", "Updated", "Unchanged", "Deleted"
    }
}