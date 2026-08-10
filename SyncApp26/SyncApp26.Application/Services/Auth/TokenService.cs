using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<string> GenerateTokenAsync(Guid userId, string email, IEnumerable<string> roleNames)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required for token generation.", nameof(email));
            }

            var secretKey = _configuration["JwtSettings:SecretKey"]
                            ?? _configuration["Jwt:SecretKey"];

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException("JWT secret key is missing. Configure 'JwtSettings:SecretKey' in appsettings.");
            }

            // One role claim per held role — ASP.NET's ClaimsPrincipal.IsInRole/[Authorize(Roles=...)]
            // both already treat multiple ClaimTypes.Role claims as "any of these", so a user holding
            // several roles at once (e.g. LineManager + SsmOfficer) needs no special-casing here.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email)
            };
            claims.AddRange(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(8),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return Task.FromResult(tokenHandler.WriteToken(token));
        }
    }
}