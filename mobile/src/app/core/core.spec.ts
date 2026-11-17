import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { describe, expect, it, beforeEach, vi } from 'vitest';
import { AuthService } from './auth.service';
import { ConfigService, validateBaseUrl } from './config.service';
import { authInterceptor, errorMessage } from './http';

describe('validateBaseUrl', () => {
  it('requires https in production builds', () => {
    expect(validateBaseUrl('http://api.example.com', false)).toContain('HTTPS');
    expect(validateBaseUrl('https://api.example.com', false)).toBeNull();
  });
  it('allows http only when explicitly permitted (development)', () => {
    expect(validateBaseUrl('http://10.0.2.2:5054', true)).toBeNull();
  });
  it('rejects empty and malformed URLs', () => {
    expect(validateBaseUrl('', false)).not.toBeNull();
    expect(validateBaseUrl('not a url', false)).not.toBeNull();
    expect(validateBaseUrl('ftp://x.com', true)).not.toBeNull();
  });
});

describe('errorMessage', () => {
  it('explains network failures', () => {
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach the server');
  });
  it('uses problem details title and validation errors', () => {
    const err = new HttpErrorResponse({ status: 400, error: { title: 'Email cannot be sent', errors: { send: ['Sending is paused.'] } } });
    expect(errorMessage(err)).toContain('Sending is paused.');
    expect(errorMessage(new HttpErrorResponse({ status: 409, error: { title: 'Content changed' } }))).toBe('Content changed');
  });
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  const auth = { token: 'old' as string | null, getAccessToken: vi.fn(), refresh: vi.fn() };

  beforeEach(() => {
    auth.token = 'old';
    auth.getAccessToken.mockImplementation(() => auth.token);
    auth.refresh.mockImplementation(async () => { auth.token = 'new'; return true; });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    TestBed.inject(ConfigService).apiBaseUrl.set('https://api.test');
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  it('adds the bearer token to API requests only', async () => {
    const p = firstValueFrom(http.get('https://api.test/api/v1/leads'));
    const req = ctrl.expectOne('https://api.test/api/v1/leads');
    expect(req.request.headers.get('Authorization')).toBe('Bearer old');
    req.flush({});
    await p;
    const ext = firstValueFrom(http.get('https://other.example/x'));
    const r2 = ctrl.expectOne('https://other.example/x');
    expect(r2.request.headers.has('Authorization')).toBe(false);
    r2.flush({});
    await ext;
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    const p = firstValueFrom(http.get('https://api.test/api/v1/leads'));
    ctrl.expectOne('https://api.test/api/v1/leads').flush({}, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve(); await new Promise(r => setTimeout(r));
    const retry = ctrl.expectOne('https://api.test/api/v1/leads');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: true });
    expect(await p).toEqual({ ok: true });
    expect(auth.refresh).toHaveBeenCalledTimes(1);
  });
});
