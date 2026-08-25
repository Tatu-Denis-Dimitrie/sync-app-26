using Microsoft.AspNetCore.Http;
using SyncApp26.API.Extensions;

namespace SyncApp26.Tests.Extensions
{
    public class AuthCookieExtensionsTests
    {
        private readonly AuthCookieOptions _options = new();

        private static string GetSetCookieHeader(HttpContext context) =>
            context.Response.Headers["Set-Cookie"].ToString();

        [Fact]
        public void AppendRefreshCookie_ExpiresAtKindUnspecified_StillTreatedAsUtc()
        {
            // Regression: a DateTime read back from EF Core + SQLite loses its Kind (comes back as
            // Unspecified even though the stored value is always UTC). DateTimeOffset's constructor
            // treats Unspecified as local time, which would silently shift the cookie's Expires by
            // the server's local UTC offset - this is exactly what RefreshTokenService.RotateAsync
            // hands back after reading an existing token from the database.
            var context = new DefaultHttpContext();
            var expiresAtUnspecified = DateTime.SpecifyKind(new DateTime(2026, 8, 26, 1, 22, 11), DateTimeKind.Unspecified);

            context.Response.AppendRefreshCookie(_options, "raw-token", expiresAtUnspecified);

            Assert.Contains("26 Aug 2026 01:22:11 GMT", GetSetCookieHeader(context));
        }

        [Fact]
        public void AppendRefreshCookie_ExpiresAtKindUtc_ProducesSameResultAsUnspecified()
        {
            var context = new DefaultHttpContext();
            var expiresAtUtc = new DateTime(2026, 8, 26, 1, 22, 11, DateTimeKind.Utc);

            context.Response.AppendRefreshCookie(_options, "raw-token", expiresAtUtc);

            Assert.Contains("26 Aug 2026 01:22:11 GMT", GetSetCookieHeader(context));
        }

        [Fact]
        public void AppendRefreshCookie_SetsPathScopedToAuthenticationController()
        {
            var context = new DefaultHttpContext();

            context.Response.AppendRefreshCookie(_options, "raw-token", DateTime.UtcNow.AddHours(8));

            Assert.Contains($"path={AuthCookieExtensions.RefreshCookiePath}", GetSetCookieHeader(context));
        }
    }
}
