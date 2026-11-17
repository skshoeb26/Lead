import { Component, inject } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { PlatformService, ThemeMode } from '../../core/platform.service';

@Component({
  selector: 'lp-more',
  imports: [RouterLink, MatIconModule, MatButtonToggleModule],
  template: `
    <h1 class="page">More</h1>
    <div class="list">
      @for (i of items; track i.path) {
        <a class="list-item" [routerLink]="i.path"><mat-icon>{{ i.icon }}</mat-icon><span class="grow">{{ i.label }}</span><mat-icon class="muted">chevron_right</mat-icon></a>
      }
    </div>
    <h2 class="section">Theme</h2>
    <mat-button-toggle-group [value]="platform.theme()" (change)="setTheme($event.value)" aria-label="Theme">
      <mat-button-toggle value="system"><mat-icon>brightness_auto</mat-icon> Auto</mat-button-toggle>
      <mat-button-toggle value="light"><mat-icon>light_mode</mat-icon> Light</mat-button-toggle>
      <mat-button-toggle value="dark"><mat-icon>dark_mode</mat-icon> Dark</mat-button-toggle>
    </mat-button-toggle-group>
    <h2 class="section">Account</h2>
    <div class="list">
      <div class="list-item"><mat-icon>person</mat-icon><div class="grow"><b>{{ auth.user()?.displayName }}</b><div class="small muted">{{ auth.user()?.email }}</div></div></div>
      <button class="list-item btn" (click)="auth.logout()"><mat-icon>logout</mat-icon><span class="grow left">Sign out</span></button>
    </div>
    <p class="small muted center">LeadPilot AI v{{ version }}</p>
  `,
  styles: `.btn{width:100%;font:inherit;cursor:pointer;border:1px solid var(--lp-border)} .left{text-align:left} .center{text-align:center;margin-top:24px}`,
})
export class MorePage {
  readonly auth = inject(AuthService);
  readonly platform = inject(PlatformService);
  readonly version = environment.appVersion;
  readonly items = [
    { path: '/crm', icon: 'view_kanban', label: 'CRM pipeline' },
    { path: '/followups', icon: 'event_upcoming', label: 'Follow-ups' },
    { path: '/reports', icon: 'monitoring', label: 'Reports' },
    { path: '/notifications', icon: 'notifications', label: 'Notifications' },
    { path: '/settings', icon: 'settings', label: 'Settings' },
    { path: '/settings/integrations', icon: 'hub', label: 'Integrations (Gmail, AI, providers)' },
    { path: '/settings/usage', icon: 'payments', label: 'API usage & budget' },
    { path: '/settings/compliance', icon: 'gpp_good', label: 'Opt-outs & consent' },
  ];
  setTheme(v: ThemeMode) { void this.platform.applyTheme(v); }
}
