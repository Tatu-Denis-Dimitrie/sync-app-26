namespace SyncApp26.Domain.Enums
{
    public static class LocalizationScopes
    {
        public const string Auth = "Auth";
        public const string Users = "Users";
        public const string Documents = "Documents";
        public const string Requests = "Requests";
        public const string Organization = "Organization";
        public const string Sync = "Sync";
        public const string Validation = "Validation";
        public const string Common = "Common";
        public const string Emails = "Emails";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Auth, Users, Documents, Requests, Organization, Sync, Validation, Common, Emails
        };
    }
}
