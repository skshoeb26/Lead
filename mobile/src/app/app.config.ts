import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { MatIconRegistry } from '@angular/material/icon';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { routes } from './app.routes';
import { ApiService } from './core/api.service';
import { AuthService } from './core/auth.service';
import { ConfigService } from './core/config.service';
import { authInterceptor } from './core/http';
import { PlatformService } from './core/platform.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([authInterceptor])),
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline', subscriptSizing: 'dynamic' } },
    provideAppInitializer(() => {
      // Resolve everything synchronously: inject() is unavailable after the first await.
      const icons = inject(MatIconRegistry), config = inject(ConfigService), api = inject(ApiService);
      const platform = inject(PlatformService), auth = inject(AuthService);
      icons.setDefaultFontSetClass('material-symbols-outlined');
      return (async () => {
        await config.load();
        await api.init();
        await platform.init();
        await auth.restore();
      })();
    }),
  ],
};
