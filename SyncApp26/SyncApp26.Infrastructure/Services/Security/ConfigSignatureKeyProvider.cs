using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    /// <summary>
    /// Reads the signing key from application configuration. Suitable for local development
    /// only — a key stored alongside the code/config it's meant to protect data against isn't
    /// meaningfully separate from that data. Production deployments should use a provider backed
    /// by a dedicated secrets store instead, behind the same ISignatureKeyProvider contract.
    /// </summary>
    public class ConfigSignatureKeyProvider : ISignatureKeyProvider
    {
        private readonly byte[] _key;

        public ConfigSignatureKeyProvider(IConfiguration configuration, ILogger<ConfigSignatureKeyProvider> logger)
        {
            var devKey = configuration["SignatureHmac:DevKey"];
            if (string.IsNullOrWhiteSpace(devKey))
                throw new InvalidOperationException(
                    "Signature HMAC key is missing. Configure 'SignatureHmac:DevKey' in appsettings.");

            logger.LogWarning(
                "Using a configuration-based signature HMAC key. This is a development-only fallback and must not be used in production.");

            _key = Encoding.UTF8.GetBytes(devKey);
        }

        public Task<byte[]> GetCurrentKeyAsync() => Task.FromResult(_key);
    }
}
