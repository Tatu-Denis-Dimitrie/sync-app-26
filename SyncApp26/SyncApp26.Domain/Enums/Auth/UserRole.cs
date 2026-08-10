namespace SyncApp26.Domain.Enums
{
    public enum UserRole
    {
        Admin,
        LineManager,
        BasicUser
    }

    public static class Roles
    {
        public const string Admin = nameof(UserRole.Admin);
        public const string LineManager = nameof(UserRole.LineManager);
        public const string BasicUser = nameof(UserRole.BasicUser);

        // Granted independently via UserRoleAssignment, not values of UserRole — a person can hold
        // either, both, or neither regardless of their base Admin/LineManager/BasicUser role.
        public const string SsmOfficer = "SsmOfficer";
        public const string SuOfficer = "SuOfficer";

        /// <summary>Every role the Roles table is seeded with at migration time. Anything else is a
        /// custom role an admin created later and carries no built-in meaning in code.</summary>
        public static readonly string[] System = { Admin, LineManager, BasicUser, SsmOfficer, SuOfficer };
    }
}
