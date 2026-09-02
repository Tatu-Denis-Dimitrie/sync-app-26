using Microsoft.Extensions.Configuration;
using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class LogRedactionOptionsTests
    {
        private static IConfiguration ConfigurationWith(params (string Key, string Value)[] entries)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
                .Build();
        }

        [Fact]
        public void FromConfiguration_DefaultsToFullyRedactingWhenTheSectionIsAbsent()
        {
            var options = LogRedactionOptions.FromConfiguration(ConfigurationWith());

            Assert.True(options.Enabled);
            Assert.True(options.RedactGuids);
            Assert.True(options.MaskEmails);
            Assert.True(options.MaskIpAddresses);
            Assert.True(options.SanitizeControlCharacters);
            Assert.Equal(2048, options.MaxStringLength);
        }

        [Fact]
        public void FromConfiguration_BindsTheDevelopmentOverride()
        {
            var options = LogRedactionOptions.FromConfiguration(
                ConfigurationWith(("Logging:Redaction:Enabled", "false")));

            Assert.False(options.Enabled);
        }

        [Fact]
        public void FromConfiguration_BindsEveryFlag()
        {
            var options = LogRedactionOptions.FromConfiguration(ConfigurationWith(
                ("Logging:Redaction:Enabled", "true"),
                ("Logging:Redaction:RedactGuids", "false"),
                ("Logging:Redaction:MaskEmails", "false"),
                ("Logging:Redaction:MaskIpAddresses", "false"),
                ("Logging:Redaction:SanitizeControlCharacters", "false"),
                ("Logging:Redaction:MaxStringLength", "64")));

            Assert.True(options.Enabled);
            Assert.False(options.RedactGuids);
            Assert.False(options.MaskEmails);
            Assert.False(options.MaskIpAddresses);
            Assert.False(options.SanitizeControlCharacters);
            Assert.Equal(64, options.MaxStringLength);
        }

        [Fact]
        public void FromConfiguration_LeavesUnsetFlagsAtTheirSafeDefault()
        {
            var options = LogRedactionOptions.FromConfiguration(
                ConfigurationWith(("Logging:Redaction:MaxStringLength", "128")));

            Assert.True(options.Enabled);
            Assert.True(options.RedactGuids);
            Assert.Equal(128, options.MaxStringLength);
        }
    }
}
