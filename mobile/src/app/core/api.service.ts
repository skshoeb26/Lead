import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Network } from '@capacitor/network';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from './config.service';
import { StorageService } from './storage.service';

type Params = Record<string, string | number | boolean | string[] | undefined | null>;

export interface CachedResult<T> { data: T; fromCache: boolean; cachedAt?: string; }

/** Safe, replayable mutations that may be queued offline. Approvals and sends are NEVER queued. */
export interface QueuedChange { id: string; method: 'POST' | 'PUT'; path: string; body: unknown; label: string; createdAt: string; }

const QUEUE_KEY = 'sync.queue';

@Injectable({ providedIn: 'root' })
export class ApiService {
  readonly online = signal(true);
  readonly pending = signal<QueuedChange[]>([]);

  constructor(private http: HttpClient, private config: ConfigService, private storage: StorageService) {}

  async init(): Promise<void> {
    try {
      this.online.set((await Network.getStatus()).connected);
      await Network.addListener('networkStatusChange', s => {
        this.online.set(s.connected);
        if (s.connected) void this.flushQueue();
      });
    } catch { /* web without plugin */ }
    this.pending.set((await this.storage.get<QueuedChange[]>(QUEUE_KEY)) ?? []);
  }

  private params(p?: Params): HttpParams {
    let hp = new HttpParams();
    for (const [k, v] of Object.entries(p ?? {})) {
      if (v === undefined || v === null || v === '') continue;
      if (Array.isArray(v)) v.forEach(x => (hp = hp.append(k, x)));
      else hp = hp.set(k, String(v));
    }
    return hp;
  }

  get<T>(path: string, params?: Params): Promise<T> {
    return firstValueFrom(this.http.get<T>(this.config.api(path), { params: this.params(params) }));
  }

  post<T>(path: string, body: unknown = {}): Promise<T> {
    return firstValueFrom(this.http.post<T>(this.config.api(path), body));
  }

  put<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(this.http.put<T>(this.config.api(path), body));
  }

  delete<T>(path: string): Promise<T> {
    return firstValueFrom(this.http.delete<T>(this.config.api(path)));
  }

  upload<T>(path: string, form: FormData): Promise<T> {
    return firstValueFrom(this.http.post<T>(this.config.api(path), form));
  }

  download(path: string, params?: Params): Promise<Blob> {
    return firstValueFrom(this.http.get(this.config.api(path), { params: this.params(params), responseType: 'blob' }));
  }

  /** Network-first GET with offline fallback to the last cached copy (lead lists, lead details, drafts). */
  async getCached<T>(path: string, params?: Params): Promise<CachedResult<T>> {
    const key = `cache.${path}?${this.params(params).toString()}`;
    try {
      const data = await this.get<T>(path, params);
      await this.storage.set(key, { data, cachedAt: new Date().toISOString() });
      return { data, fromCache: false };
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 0) {
        const cached = await this.storage.get<{ data: T; cachedAt: string }>(key);
        if (cached) return { data: cached.data, fromCache: true, cachedAt: cached.cachedAt };
      }
      throw err;
    }
  }

  /** Executes a safe change now, or queues it if the device is offline. */
  async mutateOrQueue<T>(method: 'POST' | 'PUT', path: string, body: unknown, label: string): Promise<{ queued: boolean; result?: T }> {
    try {
      const result = method === 'POST' ? await this.post<T>(path, body) : await this.put<T>(path, body);
      return { queued: false, result };
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 0) {
        const change: QueuedChange = { id: crypto.randomUUID(), method, path, body, label, createdAt: new Date().toISOString() };
        const queue = [...this.pending(), change];
        this.pending.set(queue);
        await this.storage.set(QUEUE_KEY, queue);
        return { queued: true };
      }
      throw err;
    }
  }

  async flushQueue(): Promise<number> {
    let done = 0;
    for (const change of [...this.pending()]) {
      try {
        if (change.method === 'POST') await this.post(change.path, change.body);
        else await this.put(change.path, change.body);
      } catch (err) {
        if (err instanceof HttpErrorResponse && err.status === 0) break; // still offline
        // Server rejected (e.g. validation/conflict): drop it so the queue never blocks.
      }
      const remaining = this.pending().filter(c => c.id !== change.id);
      this.pending.set(remaining);
      await this.storage.set(QUEUE_KEY, remaining);
      done++;
    }
    return done;
  }
}
