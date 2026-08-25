namespace SyncApp26.API.Middleware
{
    /// <summary>
    /// Decides whether a request should skip CSRF validation. Pulled out as a pure function, rather
    /// than inlined in the Program.cs middleware, so the exemption rules are unit-testable without
    /// spinning up the full ASP.NET Core pipeline.
    /// </summary>
    public static class CsrfExemption
    {
        private static readonly HashSet<string> SafeMethods =
            new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

        // No session exists yet at any of these, so there's nothing for CSRF to protect - the rate
        // limiter is what guards them instead. Deliberately narrower than "everything under
        // /api/authentication": google-login/microsoft-login and stop-impersonation act on an
        // existing session and stay covered by the check.
        private static readonly HashSet<string> PreSessionPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentication/login",
            "/api/authentication/register",
            "/api/authentication/forgot-password",
            "/api/authentication/reset-password"
        };

        // Exempt for a different reason than the ones above: IAntiforgery binds a token to whichever
        // identity was authenticated at MINT time (in /me), and validates that it still matches the
        // CURRENT request's identity. /refresh exists specifically for the moment the access token is
        // invalid/expired - at that exact moment the request has no valid identity at all, so a token
        // minted while still authenticated can never match and always 403s. /logout hits the same
        // mismatch whenever it's clicked after the access token has already expired. Skipping CSRF
        // here is safe: neither endpoint does anything a forged cross-site POST could turn to an
        // attacker's advantage - at worst it rotates or ends the victim's own session.
        private static readonly HashSet<string> IdentityMismatchPronePaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentication/refresh",
            "/api/authentication/logout"
        };

        // SignalR's own HTTP client (negotiate, and the long-polling/SSE fallback transports) never
        // goes through Angular's HttpClient or its interceptors, so it can never attach an
        // X-XSRF-TOKEN header - every hub connection attempt (POST /hubs/sync/negotiate) would 403
        // otherwise. The hub itself is already [Authorize]'d and (see SyncHub.cs) only exposes
        // JoinGroup/LeaveGroup keyed on a caller-supplied id, with no data mutation a forged
        // cross-site request could turn to an attacker's advantage - CSRF adds nothing here.
        private const string HubsPathPrefix = "/hubs";

        public static bool IsExempt(string method, string? path, bool hasAuthorizationHeader) =>
            SafeMethods.Contains(method)
            || hasAuthorizationHeader
            || (path != null && path.StartsWith(HubsPathPrefix, StringComparison.OrdinalIgnoreCase))
            || (path != null && (PreSessionPaths.Contains(path) || IdentityMismatchPronePaths.Contains(path)));
    }
}
