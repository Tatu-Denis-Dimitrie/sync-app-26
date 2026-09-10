using Microsoft.Extensions.Configuration;

namespace SyncApp26.API.Services.Logging
{
    public sealed class LogRedactionOptions
    {
        public const string SectionName = "Logging:Redaction";
        public bool Enabled { get; init; } = true;

        public bool RedactGuids { get; init; } = true;

        public bool MaskEmails { get; init; } = true;

        public bool MaskIpAddresses { get; init; } = true;

        public bool SanitizeControlCharacters { get; init; } = true;

        public int MaxStringLength { get; init; } = 2048;

        public static LogRedactionOptions FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);
            var bound = section.Get<LogRedactionOptions>();
            return bound ?? new LogRedactionOptions();
        }
    }
}
