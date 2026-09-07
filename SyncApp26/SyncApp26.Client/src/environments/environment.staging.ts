export const environment = {
  production: false,
  apiUrl: '/api', // relative - same-origin through nginx
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
