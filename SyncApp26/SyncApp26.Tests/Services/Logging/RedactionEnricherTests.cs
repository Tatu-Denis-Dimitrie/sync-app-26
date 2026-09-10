using System.Net;
using Serilog.Events;
using Serilog.Parsing;
using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class RedactionEnricherTests
    {
        private static LogEvent EventWith(params LogEventProperty[] properties)
        {
            var logEvent = new LogEvent(
                DateTimeOffset.UtcNow,
                LogEventLevel.Warning,
                exception: null,
                new MessageTemplate("test", Array.Empty<MessageTemplateToken>()),
                Array.Empty<LogEventProperty>());

            foreach (var property in properties)
            {
                logEvent.AddOrUpdateProperty(property);
            }

            return logEvent;
        }

        private static LogEvent Enrich(LogEvent logEvent, LogRedactionOptions? options = null)
        {
            new RedactionEnricher(options ?? new LogRedactionOptions())
                .Enrich(logEvent, propertyFactory: null!);

            return logEvent;
        }

        private static string Rendered(LogEvent logEvent, string propertyName) =>
            ((ScalarValue)logEvent.Properties[propertyName]).Value?.ToString() ?? string.Empty;

        // ───────────────────────── scalars ─────────────────────────

        [Fact]
        public void Enrich_ReplacesAGuidProperty()
        {
            var logEvent = EventWith(
                new LogEventProperty("SignerUserId", new ScalarValue(Guid.NewGuid())));

            Enrich(logEvent);

            Assert.Equal(LogRedaction.Placeholder, Rendered(logEvent, "SignerUserId"));
        }

        [Fact]
        public void Enrich_ReplacesEveryGuidIdentically()
        {
            var logEvent = EventWith(
                new LogEventProperty("SignatureId", new ScalarValue(Guid.NewGuid())),
                new LogEventProperty("SignerUserId", new ScalarValue(Guid.NewGuid())));

            Enrich(logEvent);

            Assert.Equal(Rendered(logEvent, "SignatureId"), Rendered(logEvent, "SignerUserId"));
        }

        [Fact]
        public void Enrich_ReplacesAGuidWrittenAsText()
        {
            var logEvent = EventWith(
                new LogEventProperty("Id", new ScalarValue("3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8")));

            Enrich(logEvent);

            Assert.Equal(LogRedaction.Placeholder, Rendered(logEvent, "Id"));
        }

        [Fact]
        public void Enrich_MasksAnEmailProperty()
        {
            var logEvent = EventWith(
                new LogEventProperty("Email", new ScalarValue("karina@company.com")));

            Enrich(logEvent);

            Assert.Equal("k***@company.com", Rendered(logEvent, "Email"));
        }

        [Fact]
        public void Enrich_MasksAnIpAddressProperty()
        {
            var logEvent = EventWith(
                new LogEventProperty("IP", new ScalarValue(IPAddress.Parse("192.168.1.42"))));

            Enrich(logEvent);

            Assert.Equal("192.168.1.***", Rendered(logEvent, "IP"));
        }

        [Fact]
        public void Enrich_RedactsAnIdInsideARequestPath()
        {
            var logEvent = EventWith(
                new LogEventProperty("Path", new ScalarValue("/api/User/3f9a2c1b-4d5e-6f70-8192-a3b4c5d6e7f8/documents")));

            Enrich(logEvent);

            Assert.Equal($"/api/User/{LogRedaction.Placeholder}/documents", Rendered(logEvent, "Path"));
        }

        [Fact]
        public void Enrich_LeavesNonSensitiveScalarsAlone()
        {
            var logEvent = EventWith(
                new LogEventProperty("Count", new ScalarValue(42)),
                new LogEventProperty("Elapsed", new ScalarValue(12.5)),
                new LogEventProperty("Method", new ScalarValue("POST")));

            Enrich(logEvent);

            Assert.Equal("42", Rendered(logEvent, "Count"));
            Assert.Equal("12.5", Rendered(logEvent, "Elapsed"));
            Assert.Equal("POST", Rendered(logEvent, "Method"));
        }

        // ───────────────────────── nested values ─────────────────────────

        [Fact]
        public void Enrich_RedactsInsideASequence()
        {
            var logEvent = EventWith(new LogEventProperty("Ids", new SequenceValue(new[]
            {
                new ScalarValue(Guid.NewGuid()),
                new ScalarValue("karina@company.com")
            })));

            Enrich(logEvent);

            var elements = ((SequenceValue)logEvent.Properties["Ids"]).Elements;
            Assert.Equal(LogRedaction.Placeholder, ((ScalarValue)elements[0]).Value);
            Assert.Equal("k***@company.com", ((ScalarValue)elements[1]).Value);
        }

        [Fact]
        public void Enrich_RedactsInsideAStructure()
        {
            var logEvent = EventWith(new LogEventProperty("Anomaly", new StructureValue(new[]
            {
                new LogEventProperty("SignatureId", new ScalarValue(Guid.NewGuid())),
                new LogEventProperty("Status", new ScalarValue("ChainBroken"))
            })));

            Enrich(logEvent);

            var properties = ((StructureValue)logEvent.Properties["Anomaly"]).Properties;
            Assert.Equal(LogRedaction.Placeholder, ((ScalarValue)properties[0].Value).Value);
            Assert.Equal("ChainBroken", ((ScalarValue)properties[1].Value).Value);
        }

        [Fact]
        public void Enrich_RedactsInsideADictionary()
        {
            var logEvent = EventWith(new LogEventProperty("Map", new DictionaryValue(new[]
            {
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("signer"), new ScalarValue(Guid.NewGuid()))
            })));

            Enrich(logEvent);

            var entry = Assert.Single(((DictionaryValue)logEvent.Properties["Map"]).Elements);
            Assert.Equal("signer", entry.Key.Value);
            Assert.Equal(LogRedaction.Placeholder, ((ScalarValue)entry.Value).Value);
        }

        // ───────────────────────── switches ─────────────────────────

        [Fact]
        public void Enrich_DoesNothingWhenDisabled()
        {
            var id = Guid.NewGuid();
            var logEvent = EventWith(
                new LogEventProperty("SignerUserId", new ScalarValue(id)),
                new LogEventProperty("Email", new ScalarValue("karina@company.com")));

            Enrich(logEvent, new LogRedactionOptions { Enabled = false });

            Assert.Equal(id.ToString(), Rendered(logEvent, "SignerUserId"));
            Assert.Equal("karina@company.com", Rendered(logEvent, "Email"));
        }

        [Fact]
        public void Enrich_HonoursIndividualFlags()
        {
            var logEvent = EventWith(
                new LogEventProperty("SignerUserId", new ScalarValue(Guid.NewGuid())),
                new LogEventProperty("Email", new ScalarValue("karina@company.com")));

            Enrich(logEvent, new LogRedactionOptions { RedactGuids = false });

            Assert.NotEqual(LogRedaction.Placeholder, Rendered(logEvent, "SignerUserId"));
            Assert.Equal("k***@company.com", Rendered(logEvent, "Email"));
        }

        [Fact]
        public void Enrich_NeutralisesAForgedLogLineInAProperty()
        {
            var logEvent = EventWith(new LogEventProperty(
                "Error", new ScalarValue("bad" + (char)10 + "[2026-09-02 10:00:00.000 +03:00 INF] forged")));

            Enrich(logEvent);

            Assert.DoesNotContain(((char)10).ToString(), Rendered(logEvent, "Error"), StringComparison.Ordinal);
        }

        [Fact]
        public void Enrich_IsSafeOnAnEventWithNoProperties()
        {
            var logEvent = EventWith();

            var exception = Record.Exception(() => Enrich(logEvent));

            Assert.Null(exception);
        }

        // ───────────────────────── through a real pipeline ─────────────────────────

        /// <summary>
        /// The unit tests above prove the transform; this proves the wiring. It asserts on the text
        /// a sink actually receives, which is the thing that ends up in syncapp-*.log, and would
        /// catch the enricher being correct but positioned somewhere it never runs.
        /// </summary>
        [Fact]
        public void RenderedOutput_CarriesNoIdentifierAndNoAddress()
        {
            var sink = new CollectingSink();
            using var logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.With(new RedactionEnricher(new LogRedactionOptions()))
                .WriteTo.Sink(sink)
                .CreateLogger();

            var signatureId = Guid.NewGuid();
            var signerUserId = Guid.NewGuid();

            logger.Warning(
                "Signature sweep found {Status} signature {SignatureId} by signer {SignerUserId} for {Email}.",
                "ChainBroken", signatureId, signerUserId, "karina@company.com");

            var rendered = Assert.Single(sink.Rendered);

            Assert.DoesNotContain(signatureId.ToString(), rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(signerUserId.ToString(), rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("karina@company.com", rendered, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("ChainBroken", rendered, StringComparison.Ordinal);
            Assert.Contains("k***@company.com", rendered, StringComparison.Ordinal);
        }

        private sealed class CollectingSink : Serilog.Core.ILogEventSink
        {
            public List<string> Rendered { get; } = new();

            public void Emit(LogEvent logEvent) => Rendered.Add(logEvent.RenderMessage());
        }
    }
}
