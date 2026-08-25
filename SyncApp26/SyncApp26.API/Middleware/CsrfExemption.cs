namespace SyncApp26.API.Middleware
{
    /// <summary>
    /// Decides whether a request should skip CSRF validation. A pure function, not inlined in
    /// Program.cs, so the rules are unit-testable without the full pipeline.
    /// </summary>
    public static class CsrfExemption
    {
        private static readonly HashSet<string> SafeMethods =
            new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

        // No session exists yet here, so there's nothing to protect - the rate limiter guards these
        // instead. google-login/microsoft-login/stop-impersonation act on an existing session and
        // stay covered.
        private static readonly HashSet<string> PreSessionPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentication/login",
            "/api/authentication/register",
            "/api/authentication/forgot-password",
            "/api/authentication/reset-password"
        };

        // IAntiforgery binds a token to the identity active at mint time. /refresh and /logout are
        // called exactly when the access token may be invalid, so identity won't match and CSRF
        // would always 403. Safe to skip: neither endpoint gives an attacker anything beyond
        // rotating or ending the victim's own session.
        private static readonly HashSet<string> IdentityMismatchPronePaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentication/refresh",
            "/api/authentication/logout"
        };

        // SignalR's own HTTP client never goes through Angular's interceptors, so it can never
        // attach X-XSRF-TOKEN. The hub is already [Authorize]'d with no mutable surface, so CSRF
        // adds nothing here anyway.
        private const string HubsPathPrefix = "/hubs";

        public static bool IsExempt(string method, string? path, bool hasAuthorizationHeader) =>
            SafeMethods.Contains(method)
            || hasAuthorizationHeader
            || (path != null && path.StartsWith(HubsPathPrefix, StringComparison.OrdinalIgnoreCase))
            || (path != null && (PreSessionPaths.Contains(path) || IdentityMismatchPronePaths.Contains(path)));
    }
}
