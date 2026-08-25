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
        // /api/authentication": google-login/microsoft-login and the session endpoints (me/logout/
        // refresh/stop-impersonation) all act on an existing or about-to-exist session and stay
        // covered by the check.
        private static readonly HashSet<string> PreSessionPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/authentication/login",
            "/api/authentication/register",
            "/api/authentication/forgot-password",
            "/api/authentication/reset-password"
        };

        public static bool IsExempt(string method, string? path, bool hasAuthorizationHeader) =>
            SafeMethods.Contains(method)
            || hasAuthorizationHeader
            || (path != null && PreSessionPaths.Contains(path));
    }
}
