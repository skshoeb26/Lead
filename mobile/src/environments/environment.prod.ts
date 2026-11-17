// Production environment. CI rewrites apiBaseUrl / pushEnabled from repository variables (see .github/workflows/android-apk.yml).
export const environment = {
  production: true,
  apiBaseUrl: '',
  allowInsecureHttp: false,
  pushEnabled: false,
  appVersion: '1.0.0',
};
