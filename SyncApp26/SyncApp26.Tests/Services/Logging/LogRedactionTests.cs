using System.Net;
using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class LogRedactionTests
    {
        private static LogRedactionOptions AllOn() => new();

        // ───────────────────────── RedactGuids ─────────────────────────

        [Fact]
        public void RedactGuids_ReplacesAStandaloneId()
        {
            var result = LogRedaction.RedactGuids("3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8");

            Assert.Equal(LogRedaction.Placeholder, result);
        }

        [Fact]
        public void RedactGuids_ReplacesIdsEmbeddedInAPath()
        {
            // The request-logging leak: an id lands in the log as part of a longer string, not as a
            // property of its own.
            var result = LogRedaction.RedactGuids("/api/User/3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8/documents");

            Assert.Equal($"/api/User/{LogRedaction.Placeholder}/documents", result);
        }

        [Fact]
        public void RedactGuids_ReplacesEveryIdOnTheLine()
        {
            var result = LogRedaction.RedactGuids(
                "signature 3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8 by signer 11111111-2222-3333-4444-555555555555");

            Assert.DoesNotContain("3f9a2c1b", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("11111111", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RedactGuids_IsCaseInsensitive()
        {
            var result = LogRedaction.RedactGuids("3F9A2C1B-4D5E-6F70-8192-A3B4C5D6E7F8");

            Assert.Equal(LogRedaction.Placeholder, result);
        }

        [Theory]
        [InlineData("no identifiers here")]
        [InlineData("1.45.0.0")]
        [InlineData("3f9a2c1b-4d5e-6f70-8192")] // too short to be a GUID
        public void RedactGuids_LeavesEverythingElseAlone(string input)
        {
            Assert.Equal(input, LogRedaction.RedactGuids(input));
        }

        // ───────────────────────── MaskEmails ─────────────────────────

        [Fact]
        public void MaskEmails_KeepsTheInitialAndTheDomain()
        {
            Assert.Equal("k***@company.com", LogRedaction.MaskEmails("karina@company.com"));
        }

        [Fact]
        public void MaskEmails_MasksASingleCharacterLocalPartEntirely()
        {
            Assert.Equal("***@company.com", LogRedaction.MaskEmails("k@company.com"));
        }

        [Fact]
        public void MaskEmails_HandlesSubdomainsAndPlusAddressing()
        {
            Assert.Equal("k***@mail.corp.co.uk", LogRedaction.MaskEmails("karina.melissa+ssm@mail.corp.co.uk"));
        }

        [Fact]
        public void MaskEmails_MasksAddressesEmbeddedInASentence()
        {
            var result = LogRedaction.MaskEmails("Login failed for karina@company.com: invalid credentials.");

            Assert.Equal("Login failed for k***@company.com: invalid credentials.", result);
        }

        [Fact]
        public void MaskEmails_LeavesNonAddressesAlone()
        {
            Assert.Equal("logs/syncapp-.log", LogRedaction.MaskEmails("logs/syncapp-.log"));
        }

        // ───────────────────────── MaskIp ─────────────────────────

        [Fact]
        public void MaskIp_DropsTheLastOctetOfAnIPv4Address()
        {
            Assert.Equal("192.168.1.***", LogRedaction.MaskIp(IPAddress.Parse("192.168.1.42")));
        }

        [Fact]
        public void MaskIp_DropsTheLowBitsOfAnIPv6Address()
        {
            var result = LogRedaction.MaskIp(IPAddress.Parse("2001:db8:85a3:1:1234:5678:9abc:def0"));

            Assert.EndsWith("/64", result, StringComparison.Ordinal);
            Assert.DoesNotContain("def0", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MaskIp_TreatsAMappedV4AddressAsV4()
        {
            // Arriving over a dual-stack socket must not leave all four octets visible in the suffix.
            var result = LogRedaction.MaskIp(IPAddress.Parse("::ffff:192.168.1.42"));

            Assert.Equal("192.168.1.***", result);
        }

        [Fact]
        public void MaskIpIfAddress_IgnoresDottedDecimalsThatAreNotAddresses()
        {
            // A substring-matching IPv4 regex would mangle a version number; this must not.
            Assert.Null(LogRedaction.MaskIpIfAddress("some text 1.2.3.4 inline"));
        }

        [Fact]
        public void MaskIpIfAddress_MasksAWholeStringAddress()
        {
            Assert.Equal("10.0.0.***", LogRedaction.MaskIpIfAddress("10.0.0.7"));
        }

        // ───────────────────────── StripControlCharacters ─────────────────────────

        [Fact]
        public void StripControlCharacters_NeutralisesAForgedLogLine()
        {
            var forged = "attacker@evil.com\n[2026-09-02 10:00:00.000 +03:00 INF] [Auth] Login succeeded";

            var result = LogRedaction.StripControlCharacters(forged);

            Assert.DoesNotContain("\n", result, StringComparison.Ordinal);
            Assert.Contains("\\n", result, StringComparison.Ordinal);
        }

        [Fact]
        public void StripControlCharacters_KeepsTabs()
        {
            Assert.Equal("a\tb", LogRedaction.StripControlCharacters("a\tb"));
        }

        [Fact]
        public void StripControlCharacters_DropsOtherControlCharacters()
        {
            Assert.Equal("ab", LogRedaction.StripControlCharacters("a" + (char)0 + "b"));
            Assert.Equal("ab", LogRedaction.StripControlCharacters("a" + (char)7 + "b"));
            Assert.Equal("ab", LogRedaction.StripControlCharacters("a" + (char)27 + "b"));
        }

        [Fact]
        public void StripControlCharacters_ReturnsCleanInputUnchanged()
        {
            const string clean = "nothing to do here";

            Assert.Equal(clean, LogRedaction.StripControlCharacters(clean));
        }

        // ───────────────────────── Truncate ─────────────────────────

        [Fact]
        public void Truncate_CapsAnOversizedValue()
        {
            var result = LogRedaction.Truncate(new string('x', 100), 10);

            Assert.Equal(new string('x', 10) + LogRedaction.TruncationMarker, result);
        }

        [Fact]
        public void Truncate_LeavesAShortValueAlone()
        {
            Assert.Equal("short", LogRedaction.Truncate("short", 10));
        }

        [Fact]
        public void Truncate_TreatsANonPositiveLimitAsDisabled()
        {
            var value = new string('x', 100);

            Assert.Equal(value, LogRedaction.Truncate(value, 0));
        }

        // ───────────────────────── Scrub ─────────────────────────

        [Fact]
        public void Scrub_AppliesEveryTransform()
        {
            var result = LogRedaction.Scrub(
                "user karina@company.com id 3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8\nforged",
                AllOn());

            Assert.Contains("k***@company.com", result, StringComparison.Ordinal);
            Assert.Contains(LogRedaction.Placeholder, result, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", result, StringComparison.Ordinal);
        }

        [Fact]
        public void Scrub_RedactsBeforeTruncating()
        {
            var options = new LogRedactionOptions { MaxStringLength = 24 };

            var result = LogRedaction.Scrub("karina@company.com karina@company.com", options);

            Assert.DoesNotContain("karina@", result, StringComparison.Ordinal);
        }

        [Fact]
        public void Scrub_PassesEverythingThroughWhenDisabled()
        {
            const string raw = "karina@company.com 3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8";
            var options = new LogRedactionOptions { Enabled = false };

            Assert.Equal(raw, LogRedaction.Scrub(raw, options));
        }

        [Fact]
        public void Scrub_HonoursIndividualFlags()
        {
            var options = new LogRedactionOptions { MaskEmails = false };

            var result = LogRedaction.Scrub("karina@company.com 3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8", options);

            Assert.Contains("karina@company.com", result, StringComparison.Ordinal);
            Assert.Contains(LogRedaction.Placeholder, result, StringComparison.Ordinal);
        }

        [Fact]
        public void Scrub_IsSafeOnEmptyInput()
        {
            Assert.Equal(string.Empty, LogRedaction.Scrub(string.Empty, AllOn()));
        }
    }
}
