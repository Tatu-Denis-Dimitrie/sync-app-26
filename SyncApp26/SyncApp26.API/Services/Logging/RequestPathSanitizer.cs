using Microsoft.AspNetCore.Http;

namespace SyncApp26.API.Services.Logging
{
    public static class RequestPathSanitizer
    {
        private static readonly string[] TokenRoutePrefixes =
        {
            "/api/documentsignature/validate-token/"
        };

        public static string Sanitize(PathString path)
        {
            var value = path.Value ?? string.Empty;

            foreach (var prefix in TokenRoutePrefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Concat(value.AsSpan(0, prefix.Length), LogRedaction.Placeholder);
                }
            }

            return value;
        }
    }
}
