// Default (development) environment. The production file is generated/overridden at build time.
export const environment = {
  production: false,
  /** Default API base URL. Can be changed at runtime on the login screen (Server URL). */
  apiBaseUrl: 'http://10.0.2.2:5054',
  /** Allow http:// base URLs (development only). Production builds require HTTPS. */
  allowInsecureHttp: true,
  /** Push notifications require google-services.json in the Android project. */
  pushEnabled: false,
  appVersion: '1.0.0',
};
