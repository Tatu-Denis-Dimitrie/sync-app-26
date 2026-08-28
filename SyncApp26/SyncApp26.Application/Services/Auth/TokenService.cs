using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        // Short-lived on purpose: RefreshTokenService is what keeps a session alive for its full 8h.
        private const int AccessTokenMinutes = 15;
        private const int ImpersonationTokenMinutes = 30;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<string> GenerateTokenAsync(Guid userId, string email, IEnumerable<string> roleNames)
        {
            var claims = BaseClaims(userId, email, roleNames);
            return Task.FromResult(BuildToken(claims, TimeSpan.FromMinutes(AccessTokenMinutes)));
        }

        public Task<string> GenerateImpersonationTokenAsync(
            Guid targetUserId, string targetEmail, IEnumerable<string> targetRoleNames, Guid impersonatorUserId)
        {
            var claims = BaseClaims(targetUserId, targetEmail, targetRoleNames);
            claims.Add(new Claim(CustomClaimTypes.ImpersonatorId, impersonatorUserId.ToString()));
            return Task.FromResult(BuildToken(claims, TimeSpan.FromMinutes(ImpersonationTokenMinutes)));
        }

        // One role claim per held role - ASP.NET already treats multiple ClaimTypes.Role claims as "any of these".
        private static List<Claim> BaseClaims(Guid userId, string email, IEnumerable<string> roleNames)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required for token generation.", nameof(email));
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email)
            };
            claims.AddRange(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));
            return claims;
        }

        private string BuildToken(List<Claim> claims, TimeSpan lifetime)
        {
            var secretKey = _configuration["JwtSettings:SecretKey"]
                            ?? _configuration["Jwt:SecretKey"];

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException("JWT secret key is missing. Configure 'JwtSettings:SecretKey' in appsettings.");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.Add(lifetime),
                Issuer = _configuration["JwtSettings:Issuer"],
                Audience = _configuration["JwtSettings:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}