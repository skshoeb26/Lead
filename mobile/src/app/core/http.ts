import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, from, switchMap, throwError, catchError } from 'rxjs';
import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { ProblemDetails } from './models';

/** Adds the bearer token to API calls and transparently refreshes once on 401. */
export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  const auth = inject(AuthService);
  const config = inject(ConfigService);
  if (!req.url.startsWith(config.apiBaseUrl())) return next(req);

  const withToken = (token: string | null) => (token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
  const token = auth.getAccessToken();
  const first$ = token ? next(withToken(token)) : from(auth.refresh().catch(() => false)).pipe(switchMap(() => next(withToken(auth.getAccessToken()))));

  return first$.pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !req.headers.has('x-retried')) {
        return from(auth.refresh()).pipe(
          switchMap(() => next(withToken(auth.getAccessToken()).clone({ setHeaders: { 'x-retried': '1' } }))),
        );
      }
      return throwError(() => err);
    }),
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login']);
};

/** Turns any HTTP error into a short, user-friendly message. */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return 'Cannot reach the server. Check your connection or server URL.';
    const p = err.error as ProblemDetails | null;
    if (p?.errors) {
      const msgs = Object.values(p.errors).flat();
      if (msgs.length) return `${p.title ?? 'Validation failed'}${p.title?.includes(msgs[0]) ? '' : ': ' + msgs.join(' ')}`;
    }
    if (p?.title) return p.title;
    if (err.status === 429) return 'Too many requests. Please wait a moment.';
    return `Request failed (${err.status}).`;
  }
  return err instanceof Error ? err.message : 'Something went wrong.';
}
