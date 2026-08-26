using SyncApp26.API.Middleware;

namespace SyncApp26.Tests.Middleware
{
    public class CsrfExemptionTests
    {
        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        [InlineData("TRACE")]
        [InlineData("get")]
        public void IsExempt_SafeMethod_True(string method)
        {
            Assert.True(CsrfExemption.IsExempt(method, "/api/Department", hasAuthorizationHeader: false));
        }

        [Fact]
        public void IsExempt_HasAuthorizationHeader_True()
        {
            // A bearer request isn't CSRF-able - the browser never attaches Authorization
            // automatically - and without this exception every curl/Swagger call would break.
            Assert.True(CsrfExemption.IsExempt("POST", "/api/Department", hasAuthorizationHeader: true));
        }

        [Theory]
        [InlineData("/api/authentication/login")]
        [InlineData("/api/authentication/register")]
        [InlineData("/api/authentication/forgot-password")]
        [InlineData("/api/authentication/reset-password")]
        [InlineData("/API/AUTHENTICATION/LOGIN")]
        public void IsExempt_PreSessionPath_True(string path)
        {
            Assert.True(CsrfExemption.IsExempt("POST", path, hasAuthorizationHeader: false));
        }

        [Theory]
        [InlineData("/api/authentication/refresh")]
        [InlineData("/api/authentication/logout")]
        [InlineData("/API/AUTHENTICATION/REFRESH")]
        public void IsExempt_IdentityMismatchPronePath_True(string path)
        {
            // IAntiforgery binds a token to the identity authenticated at mint time; /refresh and
            // /logout are both called precisely when the access token may already be invalid, so the
            // current request's identity won't match what the token was minted against - CSRF
            // validation would 403 every time, confirmed via live browser testing.
            Assert.True(CsrfExemption.IsExempt("POST", path, hasAuthorizationHeader: false));
        }

        [Theory]
        [InlineData("/hubs/sync/negotiate")]
        [InlineData("/hubs/sync")]
        [InlineData("/HUBS/sync/negotiate")]
        public void IsExempt_HubsPath_True(string path)
        {
            // SignalR's own HTTP client never goes through Angular's HttpClient/interceptors, so it
            // can never attach X-XSRF-TOKEN - every negotiate call would 403 otherwise.
            Assert.True(CsrfExemption.IsExempt("POST", path, hasAuthorizationHeader: false));
        }

        [Theory]
        [InlineData("/api/authentication/google-login")]
        [InlineData("/api/authentication/microsoft-login")]
        [InlineData("/api/authentication/stop-impersonation")]
        [InlineData("/api/authentication/impersonate/00000000-0000-0000-0000-000000000000")]
        [InlineData("/api/Department")]
        public void IsExempt_MutatingCookieAuthRequestOnOtherPath_False(string path)
        {
            Assert.False(CsrfExemption.IsExempt("POST", path, hasAuthorizationHeader: false));
        }

        [Fact]
        public void IsExempt_NullPath_NotExemptOnItsOwn()
        {
            Assert.False(CsrfExemption.IsExempt("POST", null, hasAuthorizationHeader: false));
        }
    }
}
