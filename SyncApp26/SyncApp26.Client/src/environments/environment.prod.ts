export const environment = {
  production: true,
  apiUrl: '/api', // relative - same-origin through nginx
  // Baked in at build time (angular.json replaces environment.ts with this file), so changing these
  // requires rebuilding the frontend image - they are not read from the container's environment.
  // Must match Authentication__Google__ClientId / __Microsoft__ClientId in the API's .env.
  // Blank disables social login (see login.component.ts).
  googleClientId: '497290497367-8ejhffm98u1bjvks1m9isc2o8vkmm3lr.apps.googleusercontent.com',
  microsoftClientId: '8ede3c76-3466-4d33-a067-b51fe144c46a',
  endpoints: {
    users: '/user',
    departments: '/department',
    version: '/version',
    documentSignature: '/documentsignature',
    localization: '/localization'
  }
};
