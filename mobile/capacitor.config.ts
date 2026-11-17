import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.leadpilot.ai',
  appName: 'LeadPilot AI',
  webDir: 'dist/mobile/browser',
  android: {
    // Production APKs never allow cleartext HTTP (see network_security_config.xml). Debug builds can reach a dev server.
    allowMixedContent: false,
  },
  server: {
    androidScheme: 'https',
  },
  plugins: {
    SplashScreen: { launchShowDuration: 800, backgroundColor: '#6750A4', showSpinner: false },
    PushNotifications: { presentationOptions: ['badge', 'sound', 'alert'] },
  },
};

export default config;
