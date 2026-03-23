using System;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class DataChangeRequestDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserEmail { get; set; }
        public string UserFullName { get; set; }
        public string RequestedChangesJson { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public Guid? ResolvedByAdminId { get; set; }
    }
}
