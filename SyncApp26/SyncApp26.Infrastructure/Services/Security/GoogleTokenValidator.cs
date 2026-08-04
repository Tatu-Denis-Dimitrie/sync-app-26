using System;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    public class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly IConfiguration _configuration;

        public GoogleTokenValidator(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<GoogleTokenPayload?> ValidateAsync(string idToken)
        {
            // Checked here, not in the constructor: throwing there would break every
            // auth endpoint when Google sign-in simply isn't configured.
            var clientId = _configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Google sign-in is not configured. Set 'Authentication:Google:ClientId' in appsettings.");

            try
            {
                // Audience must be set - left null, any Google app's token would pass.
                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
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
