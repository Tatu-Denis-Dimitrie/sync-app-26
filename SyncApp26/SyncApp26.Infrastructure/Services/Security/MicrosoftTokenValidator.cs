using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    public class MicrosoftTokenValidator : IMicrosoftTokenValidator
    {
        private const string MetadataAddress = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

        private readonly IConfiguration _configuration;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;

        public MicrosoftTokenValidator(IConfiguration configuration)
        {
            _configuration = configuration;
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                MetadataAddress, new OpenIdConnectConfigurationRetriever());
        }

        public async Task<MicrosoftTokenPayload?> ValidateAsync(string idToken)
        {
            // Checked here, not in the constructor: throwing there would break every
            // auth endpoint when Microsoft sign-in simply isn't configured.
            var clientId = _configuration["Authentication:Microsoft:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Microsoft sign-in is not configured. Set 'Authentication:Microsoft:ClientId' in appsettings.");

            try
            {
                var config = await _configManager.GetConfigurationAsync();

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = config.SigningKeys,
                    ValidateAudience = true,
                    ValidAudience = clientId,
                    // No fixed issuer to pin on "common" - every tenant issues its own.
                    ValidateIssuer = false,
                    ValidateLifetime = true
                };

                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(idToken, validationParameters, out var validatedToken);

                // Read the raw claim - the ClaimsPrincipal remaps "email" to ClaimTypes.Email.
                var jwt = (JwtSecurityToken)validatedToken;
                var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

                if (string.IsNullOrWhiteSpace(email))
                    return null;

                return new MicrosoftTokenPayload { Email = email };
            }
            catch (SecurityTokenException)
            {
                // Expired / bad signature / wrong audience.
                return null;
            }
            catch (ArgumentException)
            {
                // Input isn't shaped like a JWT at all.
                return null;
            }
        }
    }
}
