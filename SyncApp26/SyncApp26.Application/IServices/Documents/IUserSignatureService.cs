using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IUserSignatureService
    {
        /// <summary>Returns the current active signature for the given user, or null if none exists.</summary>
        Task<UserSignature?> GetUserSignatureAsync(Guid userId);

        /// <summary>
        /// Saves (creates or replaces) the stored signature for a user.
        /// Computes the integrity hash and server-issued cryptographic proof, then writes both
        /// a current-state record and an immutable audit-log entry.
        /// </summary>
        Task SaveUserSignatureAsync(
            Guid userId,
            string signatureData,
            string signatureMethod,
            string ipAddress,
            Guid performedByUserId,
            string performedByEmail);

        /// <summary>
        /// Marks the user's stored signature as revoked.
        /// The data is retained for audit purposes; a "Revoked" history entry is appended.
        /// </summary>
        Task RevokeUserSignatureAsync(
            Guid userId,
            string ipAddress,
            Guid performedByUserId,
            string performedByEmail);

        /// <summary>Returns the full immutable audit trail for the given user's signature, newest first.</summary>
        Task<IEnumerable<UserSignatureHistory>> GetUserSignatureHistoryAsync(Guid userId);
    }
}
