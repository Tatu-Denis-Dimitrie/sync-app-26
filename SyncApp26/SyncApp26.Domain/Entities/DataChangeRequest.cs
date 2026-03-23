using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    public class DataChangeRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid UserId { get; set; }
        public virtual User User { get; set; }

        public string RequestedChangesJson { get; set; }
        
        [MaxLength(1000)]
        public string Reason { get; set; }
        
        [MaxLength(50)]
        public string Status { get; set; } // "Pending", "Approved", "Rejected"
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? ResolvedAt { get; set; }
        
        public Guid? ResolvedByAdminId { get; set; }
        public virtual User ResolvedByAdmin { get; set; }
    }
}
