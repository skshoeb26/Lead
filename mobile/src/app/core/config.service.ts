import { Injectable, signal } from '@angular/core';
import { environment } from '../../environments/environment';
import { StorageService } from './storage.service';

const KEY = 'config.apiBaseUrl';

export function validateBaseUrl(url: string, allowInsecure: boolean): string | null {
  const trimmed = url.trim().replace(/\/+$/, '');
  if (!trimmed) return 'Server URL is required.';
  let parsed: URL;
  try { parsed = new URL(trimmed); } catch { return 'Enter a valid URL, e.g. https://api.yourdomain.com'; }
  if (parsed.protocol !== 'https:' && !(allowInsecure && parsed.protocol === 'http:')) return 'The server URL must use HTTPS.';
  return null;
}

/** Holds the configurable backend base URL. Production builds only accept HTTPS. */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  readonly apiBaseUrl = signal<string>(environment.apiBaseUrl);
  readonly allowInsecure = environment.allowInsecureHttp;

  constructor(private storage: StorageService) {}

  async load(): Promise<void> {
    const saved = await this.storage.get<string>(KEY);
    if (saved && !validateBaseUrl(saved, this.allowInsecure)) this.apiBaseUrl.set(saved);
  }

  async setBaseUrl(url: string): Promise<void> {
    const error = validateBaseUrl(url, this.allowInsecure);
    if (error) throw new Error(error);
    const clean = url.trim().replace(/\/+$/, '');
    this.apiBaseUrl.set(clean);
    await this.storage.set(KEY, clean);
  }

  api(path: string): string {
    return `${this.apiBaseUrl()}/api/v1${path.startsWith('/') ? path : '/' + path}`;
  }
}
