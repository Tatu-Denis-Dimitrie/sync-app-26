namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// One granted role for one user. The join row itself (not just its existence) carries audit
    /// data — when it was granted and by whom — so role changes are traceable the same way
    /// DataChangeRequest resolutions are.
    /// </summary>
    public class UserRoleAssignment
    {
        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        public Guid RoleId { get; set; }
        public virtual Role Role { get; set; } = null!;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        /// <summary>The admin who granted this role. Null for system-seeded or backfilled assignments
        /// that predate any admin action.</summary>
        public Guid? AssignedByUserId { get; set; }
        public virtual User? AssignedByUser { get; set; }
    }
}
