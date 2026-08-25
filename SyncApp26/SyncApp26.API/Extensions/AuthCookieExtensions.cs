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
        // Scoped to the authentication controller group, not just /refresh: logout also needs to
        // read this cookie to revoke it server-side, and a cookie's Path only matches request paths
        // that start with it - Path=/api/authentication/refresh would never be sent on a request to
        // the sibling /api/authentication/logout. Still far narrower than "/", so it never rides
        // along on unrelated API calls.
        public const string RefreshCookieName = "syncapp26_refresh";
        public const string RefreshCookiePath = "/api/authentication";

        public static void AppendAuthCookie(this HttpResponse response, AuthCookieOptions options, string token, TimeSpan lifetime)
        {
            response.Cookies.Append(options.Name, token, BuildCookieOptions(options, "/", DateTimeOffset.UtcNow.Add(lifetime)));
        }

        public static void AppendRefreshCookie(this HttpResponse response, AuthCookieOptions options, string token, DateTime expiresAtUtc)
        {
            // EF Core + SQLite doesn't round-trip DateTimeKind - a value read back from the
            // RefreshTokens table (e.g. after RotateAsync) lands as Kind=Unspecified even though it's
            // always UTC in practice, and DateTimeOffset's constructor treats Unspecified as local
            // time. Without this, a rotated refresh cookie's expiry silently shifts by the server's
            // local UTC offset.
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
