using System;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class DataChangeRequestDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public string RequestedChangesJson { get; set; } = string.Empty;

        public string? OriginalValuesJson { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public Guid? ResolvedByAdminId { get; set; }

        public Guid? AutoResolvedByImportHistoryId { get; set; }
    }
}
