namespace SyncApp26.Domain.Enums
{
    /// <summary>
    /// UserDocument.Status values for the signing chain: PendingUser -> PendingManager ->
    /// PendingInstructor -> Completed (the SSM/SU officer occupies the Instructor step for both
    /// document types — see Faza 2 of the roles plan). PendingAdmin is a legacy value: no document
    /// transitions into it anymore, but historical rows already completed under the old admin-signed
    /// flow still carry it and must keep rendering correctly.
    /// </summary>
    public static class DocumentStatuses
    {
        public const string PendingUser = "PendingUser";
        public const string PendingManager = "PendingManager";
        public const string PendingInstructor = "PendingInstructor";
        public const string Completed = "Completed";

        /// <summary>Legacy, no longer reachable by new documents — kept for historical rows only.</summary>
        public const string PendingAdmin = "PendingAdmin";
    }
}
