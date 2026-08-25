namespace SyncApp26.Application.IServices
{
    public enum ImpersonationStatus
    {
        Success,
        TargetNotFound,
        TargetIsAdmin,
        SelfImpersonation,
        ImpersonatorNotFound,
        ImpersonatorNotAdmin
    }

    public class ImpersonationResult
    {
        public ImpersonationStatus Status { get; init; }
        public string? Token { get; init; }
        public Guid UserId { get; init; }
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    }

    public interface IImpersonationService
    {
        /// <summary>
        /// Starts a view-only session on the target's identity. Writes an ImpersonationLog row before
        /// issuing the token — if the audit write fails, no token is produced.
        /// </summary>
        Task<ImpersonationResult> StartAsync(Guid impersonatorUserId, Guid targetUserId, string? ipAddress);

        /// <summary>
        /// Ends an impersonation session by minting a fresh token for the original admin. This mints a
        /// brand new token rather than restoring a cached one, so it re-verifies the admin still exists
        /// and still holds the Admin role — a check StartAsync already does for the target, but that
        /// the old client-side localStorage restore never needed since it replayed a token, not issued
        /// one.
        /// </summary>
        Task<ImpersonationResult> StopAsync(Guid impersonatorUserId);
    }
}
