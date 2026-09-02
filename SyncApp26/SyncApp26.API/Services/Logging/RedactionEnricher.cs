using System.Net;
using Serilog.Core;
using Serilog.Events;

namespace SyncApp26.API.Services.Logging
{
    public sealed class RedactionEnricher : ILogEventEnricher
    {
        private readonly LogRedactionOptions _options;

        public RedactionEnricher(LogRedactionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            ArgumentNullException.ThrowIfNull(logEvent);

            if (!_options.Enabled || logEvent.Properties.Count == 0)
            {
                return;
            }

            List<LogEventProperty>? replacements = null;

            foreach (var property in logEvent.Properties)
            {
                var rewritten = Rewrite(property.Value);
                if (rewritten is not null)
                {
                    replacements ??= new List<LogEventProperty>();
                    replacements.Add(new LogEventProperty(property.Key, rewritten));
                }
            }

            if (replacements is null)
            {
                return;
            }

            foreach (var replacement in replacements)
            {
                logEvent.AddOrUpdateProperty(replacement);
            }
        }

        private LogEventPropertyValue? Rewrite(LogEventPropertyValue value)
        {
            switch (value)
            {
                case ScalarValue scalar:
                    var rewrittenScalar = RewriteScalar(scalar.Value);
                    return rewrittenScalar is null ? null : new ScalarValue(rewrittenScalar);

                case SequenceValue sequence:
                {
                    List<LogEventPropertyValue>? elements = null;
                    for (var i = 0; i < sequence.Elements.Count; i++)
                    {
                        var rewritten = Rewrite(sequence.Elements[i]);
                        if (rewritten is null)
                        {
                            continue;
                        }

                        elements ??= new List<LogEventPropertyValue>(sequence.Elements);
                        elements[i] = rewritten;
                    }

                    return elements is null ? null : new SequenceValue(elements);
                }

                case StructureValue structure:
                {
                    List<LogEventProperty>? properties = null;
                    for (var i = 0; i < structure.Properties.Count; i++)
                    {
                        var rewritten = Rewrite(structure.Properties[i].Value);
                        if (rewritten is null)
                        {
                            continue;
                        }

                        properties ??= new List<LogEventProperty>(structure.Properties);
                        properties[i] = new LogEventProperty(structure.Properties[i].Name, rewritten);
                    }

                    return properties is null ? null : new StructureValue(properties, structure.TypeTag);
                }

                case DictionaryValue dictionary:
                {
                    var changed = false;
                    var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);

                    foreach (var entry in dictionary.Elements)
                    {
                        var rewrittenKey = Rewrite(entry.Key) as ScalarValue;
                        var rewrittenValue = Rewrite(entry.Value);
                        changed |= rewrittenKey is not null || rewrittenValue is not null;

                        elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                            rewrittenKey ?? entry.Key, rewrittenValue ?? entry.Value));
                    }

                    return changed ? new DictionaryValue(elements) : null;
                }

                default:
                    return null;
            }
        }

        private object? RewriteScalar(object? scalar)
        {
            switch (scalar)
            {
                case Guid when _options.RedactGuids:
                    return LogRedaction.Placeholder;

                case IPAddress address when _options.MaskIpAddresses:
                    return LogRedaction.MaskIp(address);

                case string text:
                {
                    var scrubbed = LogRedaction.Scrub(text, _options);
                    return string.Equals(scrubbed, text, StringComparison.Ordinal) ? null : scrubbed;
                }

                default:
                    return null;
            }
        }
    }
}
