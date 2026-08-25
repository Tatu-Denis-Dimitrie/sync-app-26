using Microsoft.AspNetCore.Http;

namespace SyncApp26.API.Extensions
{
    // Secure is computed once at startup from the hosting environment (not from Request.IsHttps,
    // which is unreliable behind a TLS-terminating reverse proxy without UseForwardedHeaders).
    public class AuthCookieOptions
    {
        public string Name { get; init; } = "syncapp26_session";
        public bool Secure { get; init; }
        public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;
    }

    public static class AuthCookieExtensions
    {
        public static void AppendAuthCookie(this HttpResponse response, AuthCookieOptions options, string token, TimeSpan lifetime)
        {
            response.Cookies.Append(options.Name, token, BuildCookieOptions(options, DateTimeOffset.UtcNow.Add(lifetime)));
        }

        // Must reuse the exact same Path/Secure/SameSite the cookie was written with, or the browser
        // won't match it against the existing one and the delete silently does nothing.
        public static void DeleteAuthCookie(this HttpResponse response, AuthCookieOptions options)
        {
            response.Cookies.Delete(options.Name, BuildCookieOptions(options, DateTimeOffset.UnixEpoch));
        }

        private static CookieOptions BuildCookieOptions(AuthCookieOptions options, DateTimeOffset expires) => new()
        {
            HttpOnly = true,
            Secure = options.Secure,
            SameSite = options.SameSite,
            Path = "/",
            Expires = expires
        };
    }
}
