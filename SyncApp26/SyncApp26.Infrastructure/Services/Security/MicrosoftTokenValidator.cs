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

        private readonly string _clientId;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;

        public MicrosoftTokenValidator(IConfiguration configuration)
        {
            var clientId = configuration["Authentication:Microsoft:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Microsoft sign-in is not configured. Set 'Authentication:Microsoft:ClientId' in appsettings.");

            _clientId = clientId;
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                MetadataAddress, new OpenIdConnectConfigurationRetriever());
        }

        public async Task<MicrosoftTokenPayload?> ValidateAsync(string idToken)
        {
            try
            {
                var config = await _configManager.GetConfigurationAsync();

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = config.SigningKeys,
                    ValidateAudience = true,
                    ValidAudience = _clientId,
                    // "common" accepts sign-ins from any Entra ID tenant and personal Microsoft
                    // accounts, so there is no single fixed issuer to pin - each tenant issues
                    // with its own issuer URI. Signature + audience validation is the security
                    // boundary here, matching Microsoft's documented multi-tenant pattern.
                    ValidateIssuer = false,
                    ValidateLifetime = true
                };

                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(idToken, validationParameters, out var validatedToken);

                // Read the raw "email" claim directly off the token instead of through the
                // validated ClaimsPrincipal, which remaps claim types (e.g. "email" ->
                // ClaimTypes.Email) via JwtSecurityTokenHandler's default inbound claim map.
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
                // Malformed input that isn't even shaped like a JWT (e.g. wrong segment count) -
                // JwtSecurityTokenHandler rejects this before it reaches SecurityToken validation.
                return null;
            }
        }
    }
}
