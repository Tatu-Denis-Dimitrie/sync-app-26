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
    }
}
