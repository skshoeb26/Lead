import { Injectable, NgZone, signal } from '@angular/core';
import { Router } from '@angular/router';
import { App } from '@capacitor/app';
import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { StatusBar, Style } from '@capacitor/status-bar';
import { environment } from '../../environments/environment';
import { ApiService } from './api.service';
import { StorageService } from './storage.service';

export type ThemeMode = 'system' | 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class PlatformService {
  readonly theme = signal<ThemeMode>('system');
  readonly pushStatus = signal<'unavailable' | 'disabled' | 'denied' | 'registered'>('unavailable');
  readonly isNative = Capacitor.isNativePlatform();

  constructor(private router: Router, private zone: NgZone, private api: ApiService, private storage: StorageService) {}

  async init(): Promise<void> {
    await this.applyTheme((await this.storage.get<ThemeMode>('ui.theme')) ?? 'system');
    if (!this.isNative) return;

    // Deep links: leadpilot://settings/integrations?gmail=connected  ->  /settings/integrations
    await App.addListener('appUrlOpen', e => {
      try {
        const url = new URL(e.url);
        const path = (url.host ? '/' + url.host : '') + url.pathname;
        this.zone.run(() => void this.router.navigateByUrl((path || '/') + url.search));
      } catch { /* ignore malformed */ }
    });
    await App.addListener('backButton', ({ canGoBack }) => {
      if (canGoBack) window.history.back();
      else void App.minimizeApp();
    });
  }

  async applyTheme(mode: ThemeMode): Promise<void> {
    this.theme.set(mode);
    await this.storage.set('ui.theme', mode);
    const root = document.documentElement;
    root.style.colorScheme = mode === 'system' ? 'light dark' : mode;
    root.dataset['theme'] = mode;
    if (this.isNative) {
      const dark = mode === 'dark' || (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
      try { await StatusBar.setStyle({ style: dark ? Style.Dark : Style.Light }); } catch { /* not supported */ }
    }
  }

  /** Registers for FCM when the build includes Firebase config. Notification taps open the route carried in the data payload. */
  async initPush(): Promise<void> {
    if (!this.isNative || !environment.pushEnabled) { this.pushStatus.set('unavailable'); return; }
    if ((await this.storage.get<boolean>('push.optOut')) === true) { this.pushStatus.set('disabled'); return; }
    let perm = await PushNotifications.checkPermissions();
    if (perm.receive === 'prompt') perm = await PushNotifications.requestPermissions();
    if (perm.receive !== 'granted') { this.pushStatus.set('denied'); return; }

    await PushNotifications.createChannel({ id: 'leadpilot_default', name: 'LeadPilot', importance: 4, visibility: 0 }).catch(() => undefined);
    await PushNotifications.addListener('registration', async t => {
      try {
        await this.api.post('/devices/register', { token: t.value, platform: 'Android', deviceName: 'Android phone' });
        this.pushStatus.set('registered');
      } catch { /* retried on next launch */ }
    });
    await PushNotifications.addListener('pushNotificationActionPerformed', a => {
      const route = (a.notification.data as Record<string, string> | undefined)?.['route'] ?? '/notifications';
      this.zone.run(() => void this.router.navigateByUrl(route));
    });
    await PushNotifications.register();
  }

  async setPushOptOut(optOut: boolean): Promise<void> {
    await this.storage.set('push.optOut', optOut);
    if (optOut && this.isNative && environment.pushEnabled) { await PushNotifications.unregister().catch(() => undefined); this.pushStatus.set('disabled'); }
    else await this.initPush();
  }
}
