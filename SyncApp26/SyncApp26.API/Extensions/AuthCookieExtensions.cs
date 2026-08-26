using Microsoft.AspNetCore.Http;

namespace SyncApp26.API.Extensions
{
    // Secure is computed once at startup, not from Request.IsHttps (unreliable behind a proxy).
    public class AuthCookieOptions
    {
        public string Name { get; init; } = "syncapp26_session";
        public bool Secure { get; init; }
        public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;
    }

    public static class AuthCookieExtensions
    {
        // Scoped to the auth controller group, not just /refresh - logout also needs to read this
        // cookie, and a cookie's Path only matches requests whose path starts with it.
        public const string RefreshCookieName = "syncapp26_refresh";
        public const string RefreshCookiePath = "/api/authentication";

        public static void AppendAuthCookie(this HttpResponse response, AuthCookieOptions options, string token, TimeSpan lifetime)
        {
            response.Cookies.Append(options.Name, token, BuildCookieOptions(options, "/", DateTimeOffset.UtcNow.Add(lifetime)));
        }

        public static void AppendRefreshCookie(this HttpResponse response, AuthCookieOptions options, string token, DateTime expiresAtUtc)
        {
            // SQLite round-trips DateTimeKind as Unspecified; force UTC or the expiry shifts by the server's offset.
            var utcExpires = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
            response.Cookies.Append(RefreshCookieName, token, BuildCookieOptions(options, RefreshCookiePath, new DateTimeOffset(utcExpires)));
        }

        // Must reuse the exact same Path/Secure/SameSite the cookie was written with, or the browser
        // won't match it against the existing one and the delete silently does nothing.
        public static void DeleteAuthCookie(this HttpResponse response, AuthCookieOptions options)
        {
            response.Cookies.Delete(options.Name, BuildCookieOptions(options, "/", DateTimeOffset.UnixEpoch));
        }

        public static void DeleteRefreshCookie(this HttpResponse response, AuthCookieOptions options)
        {
            response.Cookies.Delete(RefreshCookieName, BuildCookieOptions(options, RefreshCookiePath, DateTimeOffset.UnixEpoch));
        }

        private static CookieOptions BuildCookieOptions(AuthCookieOptions options, string path, DateTimeOffset expires) => new()
        {
            HttpOnly = true,
            Secure = options.Secure,
            SameSite = options.SameSite,
            Path = path,
            Expires = expires
        };
    }
}
