using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// A grantable role. Users hold zero or more roles at once via <see cref="UserRoleAssignment"/>,
    /// so combinations (e.g. LineManager + SsmOfficer) need no schema change — only a new row here.
    /// </summary>
    public class Role
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Stable code identifier checked by [Authorize(Roles = ...)] — never renamed once in use.</summary>
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }

        /// <summary>
        /// Built-in roles that code checks by name (see SyncApp26.Domain.Constants.Roles). The admin
        /// UI must refuse to delete or rename these — dropping one would silently strip authorization
        /// instead of failing loudly.
        /// </summary>
        public bool IsSystem { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<UserRoleAssignment> UserAssignments { get; set; } = new List<UserRoleAssignment>();
    }
}
