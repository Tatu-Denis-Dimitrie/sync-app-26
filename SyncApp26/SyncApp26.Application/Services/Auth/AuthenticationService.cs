using SyncApp26.Application.IServices;
using BCrypt.Net;

namespace SyncApp26.Application.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        public Task<string> HashPasswordAsync(string password)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
            return Task.FromResult(passwordHash);
        }

        public Task<bool> VerifyPasswordAsync(string password, string passwordHash)
        {
            // Implement password verification logic here using the same algorithm as hashing
            var isVerified = BCrypt.Net.BCrypt.Verify(password, passwordHash);
            return Task.FromResult(isVerified);
        }
    }
}