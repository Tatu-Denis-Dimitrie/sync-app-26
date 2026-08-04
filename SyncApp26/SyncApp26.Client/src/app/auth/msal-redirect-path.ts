/**
 * Path MSAL popups redirect back to. Shared by main.ts and the login component so they
 * can't drift apart. Must also be registered as a SPA redirect URI in Entra ID.
 */
export const MSAL_REDIRECT_PATH = '/auth-callback';
