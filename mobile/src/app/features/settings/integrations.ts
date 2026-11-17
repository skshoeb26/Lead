import { Component, OnInit, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Browser } from '@capacitor/browser';
import { App } from '@capacitor/app';
import { ApiService } from '../../core/api.service';
import { GoogleStatus, SystemStatus } from '../../core/models';
import { PlatformService } from '../../core/platform.service';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-integrations',
  imports: [MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Integrations</h1>
    @if (gmail() === 'connected') { <div class="notice info"><mat-icon>check_circle</mat-icon>Gmail connected.</div> }
    @if (gmail() === 'failed') { <div class="notice bad"><mat-icon>error</mat-icon>Gmail connection failed. Try again.</div> }

    <div class="card stack">
      <div class="row"><mat-icon class="g">mail</mat-icon><b class="grow">Gmail</b>
        <span class="chip" [class.ok]="google()?.connected" [class.warn]="!google()?.connected">{{ google()?.connected ? 'Connected' : 'Not connected' }}</span></div>
      @if (google(); as g) {
        @if (g.connected) {
          <div class="small">Sending as <b>{{ g.emailAddress }}</b> since {{ g.connectedAt | date: 'mediumDate' }}</div>
          <div class="small muted">Permission: send email only (LeadPilot cannot read your mailbox).</div>
        }
        @if (g.lastError) { <div class="notice bad small">{{ g.lastError }}</div> }
        @if (!g.configured) { <div class="notice warn small"><mat-icon>key_off</mat-icon>Google OAuth client is not configured on the server yet (see docs/SETUP-GMAIL.md).</div> }
        @if (g.testMode) { <div class="notice info small"><mat-icon>science</mat-icon>Server is in test mode — no real emails are delivered.</div> }
        <div class="actions">
          @if (!g.connected) { <button mat-flat-button (click)="connect()" [disabled]="!g.configured"><mat-icon>link</mat-icon>Connect Gmail</button> }
          @else { <button mat-stroked-button (click)="connect()"><mat-icon>sync</mat-icon>Reconnect</button><button mat-button (click)="disconnect()"><mat-icon>link_off</mat-icon>Disconnect</button> }
        </div>
      }
    </div>

    <h2 class="section">Server integrations</h2>
    <div class="list">
      @for (i of status()?.integrations ?? []; track i.key) {
        <div class="list-item"><mat-icon [class.ok]="i.configured" [class.off]="!i.configured">{{ i.configured ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
          <div class="grow"><b>{{ i.name }}</b>@if (i.detail) {<div class="small muted">{{ i.detail }}</div>}</div>
          @if (i.isMock) { <span class="chip warn">Mock</span> } @else { <span class="chip" [class.ok]="i.configured">{{ i.configured ? 'Ready' : 'Awaiting credentials' }}</span> }
        </div>
      }
    </div>
    @if (status(); as s) { <p class="small muted">Server environment: {{ s.environment }} · background jobs {{ s.jobsEnabled ? 'on' : 'off' }} · API v{{ s.version }}</p> }
    <p class="small muted">WhatsApp and LinkedIn: LeadPilot generates drafts for you to send manually. WhatsApp API sending is only enabled after opt-in collection is configured.</p>
  `,
  styles: `.g{color:#ea4335} .ok{color:var(--lp-ok)} .off{color:var(--lp-muted)}`,
})
export class IntegrationsPage implements OnInit {
  readonly gmail = input<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  private platform = inject(PlatformService);
  readonly google = signal<GoogleStatus | null>(null);
  readonly status = signal<SystemStatus | null>(null);

  async ngOnInit() {
    await this.load();
    if (this.platform.isNative) await App.addListener('resume', () => void this.load());
  }

  async load() {
    try {
      this.google.set(await this.api.get<GoogleStatus>('/integrations/google/status'));
      this.status.set(await this.api.get<SystemStatus>('/settings/status'));
    } catch (e) { this.toast.error(e); }
  }

  async connect() {
    try {
      const { authorizationUrl } = await this.api.get<{ authorizationUrl: string }>('/integrations/google/connect');
      // System browser (Custom Tab) — Google blocks OAuth inside embedded WebViews. The server callback deep-links back to the app.
      if (this.platform.isNative) await Browser.open({ url: authorizationUrl });
      else window.open(authorizationUrl, '_blank');
    } catch (e) { this.toast.error(e); }
  }

  async disconnect() {
    if (!confirm('Disconnect Gmail? Approved emails will not be sent until you reconnect.')) return;
    try { await this.api.delete('/integrations/google/disconnect'); await this.load(); } catch (e) { this.toast.error(e); }
  }
}
