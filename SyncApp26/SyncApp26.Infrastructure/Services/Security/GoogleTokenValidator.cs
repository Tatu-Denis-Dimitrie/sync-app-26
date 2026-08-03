using System;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    public class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly string _clientId;

        public GoogleTokenValidator(IConfiguration configuration)
        {
            var clientId = configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Google sign-in is not configured. Set 'Authentication:Google:ClientId' in appsettings.");

            _clientId = clientId;
        }

        public async Task<GoogleTokenPayload?> ValidateAsync(string idToken)
        {
            try
            {
                // Audience MUST be set: GoogleJsonWebSignature skips audience validation entirely
                // when it is left null, which would accept an ID token issued for any Google app.
                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _clientId }
                });

                if (string.IsNullOrWhiteSpace(payload.Email))
                    return null;

                return new GoogleTokenPayload
                {
                    Email = payload.Email,
                    EmailVerified = payload.EmailVerified
                };
            }
            catch (InvalidJwtException)
            {
                return null;
            }
        }
    }
}
