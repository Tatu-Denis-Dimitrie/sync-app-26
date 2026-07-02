using System.Security.Cryptography;
using System.Text;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class UserSignatureService : IUserSignatureService
    {
        private readonly IUserSignatureRepository _repository;
        private readonly ICryptographyService _cryptographyService;

        public UserSignatureService(IUserSignatureRepository repository, ICryptographyService cryptographyService)
        {
            _repository = repository;
            _cryptographyService = cryptographyService;
        }

        public Task<UserSignature?> GetUserSignatureAsync(Guid userId)
            => _repository.GetByUserIdAsync(userId);

        public async Task SaveUserSignatureAsync(
            Guid userId,
            string signatureData,
            string signatureMethod,
            string ipAddress,
            Guid performedByUserId,
            string performedByEmail)
        {
            if (string.IsNullOrWhiteSpace(signatureData))
                throw new ArgumentException("Signature data must not be empty.", nameof(signatureData));

            if (string.IsNullOrWhiteSpace(signatureMethod))
                throw new ArgumentException("Signature method must not be empty.", nameof(signatureMethod));

            // Integrity: SHA-256 of the raw signature payload
            var hash = ComputeHash(signatureData);
            var timestampTicks = DateTime.UtcNow.Ticks;

            // Cryptographic proof: server signs the canonical string so it can be verified later
            var canonical = $"{hash}|{userId}|{timestampTicks}";
            var cryptographicProof = await _cryptographyService.SignDataAsync(canonical);

            var existing = await _repository.GetByUserIdAsync(userId);
            string action;

            if (existing == null)
            {
                action = "Created";
                var newSignature = new UserSignature
                {
                    UserId = userId,
                    SignatureData = signatureData,
                    SignatureMethod = signatureMethod,
                    SignatureHash = hash,
                    CryptographicProof = cryptographicProof,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddAsync(newSignature);
            }
            else
            {
                action = "Updated";
                existing.SignatureData = signatureData;
                existing.SignatureMethod = signatureMethod;
                existing.SignatureHash = hash;
                existing.CryptographicProof = cryptographicProof;
                existing.IpAddress = ipAddress;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.RevokedAt = null; // re-activates a previously revoked signature
                await _repository.UpdateAsync(existing);
            }

            await _repository.AddHistoryAsync(new UserSignatureHistory
            {
                UserId = userId,
                SignatureData = signatureData,
                SignatureMethod = signatureMethod,
                SignatureHash = hash,
                CryptographicProof = cryptographicProof,
                IpAddress = ipAddress,
                Action = action,
                PerformedByUserId = performedByUserId,
                PerformedByEmail = performedByEmail,
                CreatedAt = DateTime.UtcNow
            });
        }

        public async Task RevokeUserSignatureAsync(
            Guid userId,
            string ipAddress,
            Guid performedByUserId,
            string performedByEmail)
        {
            var existing = await _repository.GetByUserIdAsync(userId);
            if (existing == null)
                throw new InvalidOperationException("No active signature found for this user.");

            existing.RevokedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(existing);

            await _repository.AddHistoryAsync(new UserSignatureHistory
            {
                UserId = userId,
                SignatureData = existing.SignatureData,
                SignatureMethod = existing.SignatureMethod,
                SignatureHash = existing.SignatureHash,
                CryptographicProof = existing.CryptographicProof,
                IpAddress = ipAddress,
                Action = "Revoked",
                PerformedByUserId = performedByUserId,
                PerformedByEmail = performedByEmail,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<IEnumerable<UserSignatureHistory>> GetUserSignatureHistoryAsync(Guid userId)
            => _repository.GetHistoryByUserIdAsync(userId);

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string ComputeHash(string data)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
