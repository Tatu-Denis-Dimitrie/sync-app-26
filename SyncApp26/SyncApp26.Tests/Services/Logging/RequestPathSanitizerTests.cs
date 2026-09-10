using Microsoft.AspNetCore.Http;
using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class RequestPathSanitizerTests
    {
        [Fact]
        public void Sanitize_ReplacesTheTokenSegmentOnValidateToken()
        {
            var result = RequestPathSanitizer.Sanitize(
                new PathString("/api/documentsignature/validate-token/JGx7q9K2mZ...opaque-token"));

            Assert.Equal($"/api/documentsignature/validate-token/{LogRedaction.Placeholder}", result);
        }

        [Fact]
        public void Sanitize_MatchesTheRouteCaseInsensitively()
        {
            var result = RequestPathSanitizer.Sanitize(
                new PathString("/api/DocumentSignature/validate-token/abc123"));

            Assert.Equal($"/api/DocumentSignature/validate-token/{LogRedaction.Placeholder}", result);
        }

        [Fact]
        public void Sanitize_LeavesOrdinaryRoutesUnchanged()
        {
            const string path = "/api/authentication/login";

            Assert.Equal(path, RequestPathSanitizer.Sanitize(new PathString(path)));
        }

        [Fact]
        public void Sanitize_LeavesAnUnrelatedRouteWithASimilarPrefixUnchanged()
        {
            const string path = "/api/documentsignature/validate-token-count";

            Assert.Equal(path, RequestPathSanitizer.Sanitize(new PathString(path)));
        }

        [Fact]
        public void Sanitize_HandlesAnEmptyPath()
        {
            Assert.Equal(string.Empty, RequestPathSanitizer.Sanitize(new PathString(null)));
        }

        [Fact]
        public void Sanitize_NeverContainsTheRawTokenEvenWhenTheTokenLooksLikeAPath()
        {
            var result = RequestPathSanitizer.Sanitize(
                new PathString("/api/documentsignature/validate-token/../../etc/passwd"));

            Assert.Equal($"/api/documentsignature/validate-token/{LogRedaction.Placeholder}", result);
        }
    }
}
