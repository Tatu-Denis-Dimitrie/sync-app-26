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
            /*
             * Read and validate the config here rather than in the constructor: AccountService
             * depends on this validator, so a constructor throw would fail every
             * AuthenticationController endpoint - including plain password login and password
             * reset - on any deployment that doesn't use Google sign-in.
             */
            var clientId = _configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Google sign-in is not configured. Set 'Authentication:Google:ClientId' in appsettings.");

            try
            {
                // Audience MUST be set: GoogleJsonWebSignature skips audience validation entirely
                // when it is left null, which would accept an ID token issued for any Google app.
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
