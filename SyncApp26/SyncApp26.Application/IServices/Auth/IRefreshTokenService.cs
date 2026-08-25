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
        /// Consumes a refresh token and mints its successor, which inherits the same absolute
        /// ExpiresAt - rotation never extends a session. Presenting an already-consumed token again
        /// within a short grace window is treated as a benign concurrent-request race (two tabs
        /// refreshing at once) and mints another sibling successor; outside that window it's treated
        /// as token theft and revokes every active session the user has.
        /// </summary>
        Task<RefreshResult> RotateAsync(string rawToken);

        Task RevokeAsync(string rawToken);

        Task RevokeAllForUserAsync(Guid userId);
    }
}
