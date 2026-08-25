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
        [InlineData("/api/authentication/google-login")]
        [InlineData("/api/authentication/microsoft-login")]
        [InlineData("/api/authentication/logout")]
        [InlineData("/api/authentication/refresh")]
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
