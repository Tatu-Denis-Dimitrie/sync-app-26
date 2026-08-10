namespace SyncApp26.Domain.Enums
{
    /// <summary>
    /// Role names checked by [Authorize(Roles = ...)] / ClaimsPrincipal.IsInRole(...). These are the
    /// system roles seeded into the Roles table by the AddRoleTables migration — actual role
    /// membership lives in UserRoleAssignment, not on any fixed enum, so a deployment can define
    /// additional custom roles beyond this set without a schema change.
    /// </summary>
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string LineManager = "LineManager";
        public const string BasicUser = "BasicUser";

        // Granted independently via UserRoleAssignment - a person can hold either, both, or neither
        // regardless of their Admin/LineManager/BasicUser role.
        public const string SsmOfficer = "SsmOfficer";
        public const string SuOfficer = "SuOfficer";

        /// <summary>Every role the Roles table is seeded with at migration time. Anything else is a
        /// custom role an admin created later and carries no built-in meaning in code.</summary>
        public static readonly string[] System = { Admin, LineManager, BasicUser, SsmOfficer, SuOfficer };
    }
}
