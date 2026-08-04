/**
 * Path MSAL popups are redirected back to. Shared by main.ts (which runs the redirect
 * bridge on this path instead of bootstrapping Angular) and the login component (which
 * builds the redirectUri from it), so the two can never drift apart.
 *
 * Must also be registered as a Single-page application redirect URI in the Entra ID
 * app registration.
 */
export const MSAL_REDIRECT_PATH = '/auth-callback';
