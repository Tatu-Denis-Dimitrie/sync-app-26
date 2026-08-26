using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace SyncApp26.API.Extensions
{
    public static class XsrfCookieExtensions
    {
        public const string XsrfCookieName = "XSRF-TOKEN";

        // Non-httpOnly on purpose: Angular's HttpXsrfInterceptor reads it and echoes it back as a
        // header. Safe - the value alone authenticates nothing without the paired antiforgery cookie.
        public static void IssueXsrfCookie(this HttpContext context, IAntiforgery antiforgery, AuthCookieOptions authCookieOptions)
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                Secure = authCookieOptions.Secure,
                SameSite = authCookieOptions.SameSite,
                Path = "/"
            });
        }
    }
}
