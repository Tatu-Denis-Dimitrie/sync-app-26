using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    public class HmacSignatureService : IHmacSignatureService
    {
        private readonly ISignatureKeyProvider _keyProvider;

        public HmacSignatureService(ISignatureKeyProvider keyProvider)
        {
            _keyProvider = keyProvider;
        }

        public async Task<string> ComputeHmacAsync(string canonicalInput)
        {
            var key = await _keyProvider.GetCurrentKeyAsync();
            var dataBytes = Encoding.UTF8.GetBytes(canonicalInput);
            using var hmac = new HMACSHA256(key);
            var hashBytes = hmac.ComputeHash(dataBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public async Task<bool> VerifyHmacAsync(string canonicalInput, string expectedHmac)
        {
            var computed = await ComputeHmacAsync(canonicalInput);

            // Constant-time comparison — a HMAC verify that short-circuits on the first
            // mismatched byte leaks how many leading bytes an attacker already guessed right.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(expectedHmac ?? string.Empty));
        }
    }
}
