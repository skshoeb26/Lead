import { Injectable } from '@angular/core';
import { Preferences } from '@capacitor/preferences';
import { SecureStorage } from '@aparajita/capacitor-secure-storage';

/**
 * Two stores:
 * - `secure*`: Android Keystore-backed storage for the refresh token only (never plain SharedPreferences).
 * - `get/set`: ordinary preferences for non-sensitive data (server URL, theme, cached lists, offline queue).
 * OAuth refresh tokens for Gmail never reach the app; they stay encrypted on the server.
 */
@Injectable({ providedIn: 'root' })
export class StorageService {
  async get<T>(key: string): Promise<T | null> {
    const { value } = await Preferences.get({ key });
    if (value == null) return null;
    try { return JSON.parse(value) as T; } catch { return null; }
  }

  async set(key: string, value: unknown): Promise<void> {
    await Preferences.set({ key, value: JSON.stringify(value) });
  }

  async remove(key: string): Promise<void> {
    await Preferences.remove({ key });
  }

  async keys(): Promise<string[]> {
    return (await Preferences.keys()).keys;
  }

  async secureGet(key: string): Promise<string | null> {
    try {
      const v = await SecureStorage.get(key);
      return typeof v === 'string' ? v : null;
    } catch {
      return null;
    }
  }

  async secureSet(key: string, value: string): Promise<void> {
    await SecureStorage.set(key, value);
  }

  async secureRemove(key: string): Promise<void> {
    try { await SecureStorage.remove(key); } catch { /* already removed */ }
  }
}
