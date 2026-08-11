namespace SyncApp26.Application.IServices
{
    public interface ITokenService
    {
        Task<string> GenerateTokenAsync(Guid userId, string email, IEnumerable<string> roleNames);

        /// <summary>
        /// A short-lived token issued on the TARGET user's identity, tagged with the acting admin via
        /// the ImpersonatorId claim. Deliberately a separate method from GenerateTokenAsync rather than
        /// an optional parameter on it — the login path must never be able to accidentally mint an
        /// impersonation token (or vice versa), and the two have different lifetimes.
        /// </summary>
        Task<string> GenerateImpersonationTokenAsync(
            Guid targetUserId, string targetEmail, IEnumerable<string> targetRoleNames, Guid impersonatorUserId);
    }
}
