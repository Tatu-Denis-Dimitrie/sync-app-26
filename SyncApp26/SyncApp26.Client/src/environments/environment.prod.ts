export const environment = {
  production: true,
  apiUrl: '/api', // relative - same-origin through nginx
  // blank disables social login (see login.component.ts)
  googleClientId: '',
  microsoftClientId: '',
  endpoints: {
    users: '/user',
    departments: '/department',
    version: '/version',
    documentSignature: '/documentsignature',
    localization: '/localization'
  }
};
