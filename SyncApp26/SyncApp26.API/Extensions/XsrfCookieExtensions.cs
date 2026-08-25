using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace SyncApp26.API.Extensions
{
    public static class XsrfCookieExtensions
    {
        public const string XsrfCookieName = "XSRF-TOKEN";

        // Non-httpOnly on purpose: Angular's built-in HttpXsrfInterceptor reads this cookie and
        // echoes its value back as the X-XSRF-TOKEN header on unsafe requests, with zero client code
        // needed - but only if JS is allowed to read it. That's safe: the value alone authenticates
        // nothing without the paired, httpOnly antiforgery cookie IAntiforgery sets alongside it.
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
