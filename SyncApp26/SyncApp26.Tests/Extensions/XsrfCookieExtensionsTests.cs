using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Moq;
using SyncApp26.API.Extensions;

namespace SyncApp26.Tests.Extensions
{
    public class XsrfCookieExtensionsTests
    {
        private readonly Mock<IAntiforgery> _antiforgeryMock = new();

        [Fact]
        public void IssueXsrfCookie_SetsNonHttpOnlyCookieWithRequestToken()
        {
            var context = new DefaultHttpContext();
            _antiforgeryMock.Setup(a => a.GetAndStoreTokens(context))
                .Returns(new AntiforgeryTokenSet("the-request-token", null, "__RequestVerificationToken", "X-XSRF-TOKEN"));
            var options = new AuthCookieOptions { Secure = true };

            context.IssueXsrfCookie(_antiforgeryMock.Object, options);

            var setCookie = context.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"{XsrfCookieExtensions.XsrfCookieName}=the-request-token", setCookie);
            Assert.DoesNotContain("httponly", setCookie.ToLowerInvariant());
            Assert.Contains("secure", setCookie.ToLowerInvariant());
        }
    }
}
