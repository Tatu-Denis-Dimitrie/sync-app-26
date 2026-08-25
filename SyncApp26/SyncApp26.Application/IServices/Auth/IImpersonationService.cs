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
        /// <summary>Starts a view-only session on the target's identity.</summary>
        Task<ImpersonationResult> StartAsync(Guid impersonatorUserId, Guid targetUserId, string? ipAddress);

        /// <summary>Ends impersonation by minting a fresh token for the original admin.</summary>
        Task<ImpersonationResult> StopAsync(Guid impersonatorUserId);
    }
}
