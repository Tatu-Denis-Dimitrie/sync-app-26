using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SyncApp26.API.Services.Logging
{
    public static class LogRedaction
    {
        public const string Placeholder = "[redacted]";

        public const string TruncationMarker = "...[truncated]";

        private static readonly Regex GuidPattern = new(
            @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            RegexOptions.Compiled);
        
        private static readonly Regex EmailPattern = new(
            @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        public static string RedactGuids(string value) =>
            string.IsNullOrEmpty(value) ? value : GuidPattern.Replace(value, Placeholder);

        public static string MaskEmails(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return EmailPattern.Replace(value, match =>
            {
                var address = match.Value;
                var at = address.LastIndexOf('@');
                var localPart = address[..at];
                var domain = address[at..];

                return localPart.Length <= 1 ? string.Concat("***", domain) : string.Concat(localPart[0], "***", domain);
            });
        }

        public static string MaskIp(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var octets = address.GetAddressBytes();
                return string.Format(
                    CultureInfo.InvariantCulture, "{0}.{1}.{2}.***", octets[0], octets[1], octets[2]);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                Array.Clear(bytes, 8, 8);
                return string.Concat(new IPAddress(bytes).ToString(), "/64");
            }

            return Placeholder;
        }

        public static string? MaskIpIfAddress(string value) =>
            IPAddress.TryParse(value, out var address) ? MaskIp(address) : null;

        public static string StripControlCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var needsWork = false;
            foreach (var c in value)
            {
                if (char.IsControl(c) && c != '\t')
                {
                    needsWork = true;
                    break;
                }
            }

            if (!needsWork)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append(c);
                        break;
                    default:
                        if (!char.IsControl(c))
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        public static string Truncate(string value, int maxLength)
        {
            if (maxLength <= 0 || string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return string.Concat(value.AsSpan(0, maxLength), TruncationMarker);
        }

        public static string Scrub(string value, LogRedactionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!options.Enabled || string.IsNullOrEmpty(value))
            {
                return value;
            }

            var result = value;

            if (options.SanitizeControlCharacters)
            {
                result = StripControlCharacters(result);
            }

            if (options.MaskIpAddresses)
            {
                result = MaskIpIfAddress(result) ?? result;
            }

            if (options.RedactGuids)
            {
                result = RedactGuids(result);
            }

            if (options.MaskEmails)
            {
                result = MaskEmails(result);
            }

            return Truncate(result, options.MaxStringLength);
        }
    }
}
