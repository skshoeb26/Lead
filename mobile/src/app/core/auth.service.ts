import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from './config.service';
import { AuthResponse, User } from './models';
import { StorageService } from './storage.service';

const REFRESH_KEY = 'auth.refreshToken';
const USER_KEY = 'auth.user';

/**
 * Access token lives in memory only. The refresh token is kept in secure (Keystore-backed) storage and rotated on
 * every refresh. Refreshes are single-flight so concurrent 401s trigger one refresh.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http: HttpClient;
  private accessToken: string | null = null;
  private accessExpires = 0;
  private refreshing: Promise<boolean> | null = null;

  readonly user = signal<User | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly sessionExpired = signal(false);

  constructor(backend: HttpBackend, private config: ConfigService, private storage: StorageService, private router: Router) {
    // Bypass interceptors for auth calls to avoid refresh loops.
    this.http = new HttpClient(backend);
  }

  async restore(): Promise<void> {
    const user = await this.storage.get<User>(USER_KEY);
    const refresh = await this.storage.secureGet(REFRESH_KEY);
    if (user && refresh) {
      this.user.set(user);
      // Try to obtain a fresh access token; if offline, keep the cached user so cached data stays viewable.
      await this.refresh().catch(() => undefined);
    }
  }

  async login(email: string, password: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<AuthResponse>(this.config.api('/auth/login'), { email, password, deviceName: 'Android app' }));
    await this.apply(res);
  }

  async register(email: string, password: string, displayName: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<AuthResponse>(this.config.api('/auth/register'), { email, password, displayName }));
    await this.apply(res);
  }

  getAccessToken(): string | null {
    return this.accessToken && Date.now() < this.accessExpires - 15_000 ? this.accessToken : null;
  }

  /** Returns true when a new access token was obtained. */
  refresh(): Promise<boolean> {
    if (!this.refreshing) {
      this.refreshing = (async () => {
        const refreshToken = await this.storage.secureGet(REFRESH_KEY);
        if (!refreshToken) return false;
        try {
          const res = await firstValueFrom(this.http.post<AuthResponse>(this.config.api('/auth/refresh'), { refreshToken }));
          await this.apply(res);
          return true;
        } catch (err: unknown) {
          const status = (err as { status?: number })?.status;
          if (status === 401) await this.expire();
          throw err;
        }
      })().finally(() => (this.refreshing = null));
    }
    return this.refreshing;
  }

  async logout(): Promise<void> {
    const refreshToken = await this.storage.secureGet(REFRESH_KEY);
    if (refreshToken) {
      try { await firstValueFrom(this.http.post(this.config.api('/auth/logout'), { refreshToken })); } catch { /* offline: token expires server-side */ }
    }
    await this.clear();
    await this.router.navigateByUrl('/login');
  }

  private async expire(): Promise<void> {
    await this.clear();
    this.sessionExpired.set(true);
    await this.router.navigateByUrl('/login');
  }

  private async clear(): Promise<void> {
    this.accessToken = null;
    this.accessExpires = 0;
    this.user.set(null);
    await this.storage.secureRemove(REFRESH_KEY);
    await this.storage.remove(USER_KEY);
    // Remove cached prospect data from the device on logout.
    for (const key of await this.storage.keys()) if (key.startsWith('cache.') || key.startsWith('sync.')) await this.storage.remove(key);
  }

  private async apply(res: AuthResponse): Promise<void> {
    this.accessToken = res.accessToken;
    this.accessExpires = new Date(res.accessTokenExpiresAt).getTime();
    await this.storage.secureSet(REFRESH_KEY, res.refreshToken);
    await this.storage.set(USER_KEY, res.user);
    this.user.set(res.user);
    this.sessionExpired.set(false);
  }
}
