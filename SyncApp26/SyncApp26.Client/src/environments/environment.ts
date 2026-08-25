export const environment = {
  production: false,
  // Relative on purpose: same-origin through the dev proxy (proxy.conf.json) and in production,
  // so the dev and prod code paths are identical. localhost:4200/:5022 are the same site (cookies
  // ignore port), so an absolute URL here would still "work" while silently being cross-site -
  // never point this back at an absolute URL, even temporarily to debug.
  apiUrl: '/api',
  googleClientId: '651123926793-1s1fchku41c2fmf6s2o49rh4avb1f8f3.apps.googleusercontent.com',
  microsoftClientId: '8ede3c76-3466-4d33-a067-b51fe144c46a',
  endpoints: {
    users: '/user',
    departments: '/department',
    version: '/version',
    documentSignature: '/documentsignature'
  }
};
