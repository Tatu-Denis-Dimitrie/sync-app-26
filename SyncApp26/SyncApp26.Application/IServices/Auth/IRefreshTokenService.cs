namespace SyncApp26.Application.IServices
{
    public class IssuedRefreshToken
    {
        public string RawToken { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
    }

    public enum RefreshOutcome
    {
        Success,
        NotFound,
        Expired,
        Revoked,
        Reused
    }

    public class RefreshResult
    {
        public RefreshOutcome Outcome { get; init; }
        public Guid? UserId { get; init; }
        public IssuedRefreshToken? Token { get; init; }
    }

    public interface IRefreshTokenService
    {
        Task<IssuedRefreshToken> IssueAsync(Guid userId, DateTime expiresAt);

        /// <summary>
        /// Consumes a token and mints its successor. Reuse of an already-consumed token within a
        /// short grace window is treated as a benign race and mints a sibling; outside it, as theft,
        /// revoking every session the user has.
        /// </summary>
        Task<RefreshResult> RotateAsync(string rawToken);

        Task RevokeAsync(string rawToken);

        Task RevokeAllForUserAsync(Guid userId);
    }
}
