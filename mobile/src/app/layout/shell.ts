import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { ApiService } from '../core/api.service';
import { DashboardSummary } from '../core/models';
import { PlatformService } from '../core/platform.service';

/** App chrome: top bar + bottom navigation optimised for one-handed phone use. */
@Component({
  selector: 'lp-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatIconModule, MatButtonModule, MatBadgeModule],
  template: `
    <mat-toolbar class="top">
      @if (isRoot()) {
        <span class="brand"><mat-icon class="logo">rocket_launch</mat-icon>LeadPilot<span class="ai">AI</span></span>
      } @else {
        <button mat-icon-button (click)="back()" aria-label="Back"><mat-icon>arrow_back</mat-icon></button>
        <span class="title">{{ title() }}</span>
      }
      <span class="spacer"></span>
      @if (!api.online()) { <mat-icon class="offline" title="Offline">cloud_off</mat-icon> }
      @if (api.pending().length) { <span class="sync" title="Changes waiting to sync">{{ api.pending().length }} queued</span> }
      <button mat-icon-button routerLink="/notifications" aria-label="Notifications">
        <mat-icon [matBadge]="unread() || null" matBadgeSize="small" matBadgeColor="warn">notifications</mat-icon>
      </button>
    </mat-toolbar>

    <main class="content"><router-outlet /></main>

    <nav class="bottom" aria-label="Main">
      @for (item of nav; track item.path) {
        <a [routerLink]="item.path" routerLinkActive="active" [attr.aria-label]="item.label">
          <mat-icon [matBadge]="item.badge ? badge(item.badge) : null" matBadgeSize="small" matBadgeColor="warn">{{ item.icon }}</mat-icon>
          <span>{{ item.label }}</span>
        </a>
      }
    </nav>
  `,
  styles: `
    :host{display:flex;flex-direction:column;height:100dvh}
    .top{position:sticky;top:0;z-index:5;padding-top:env(safe-area-inset-top);background:var(--lp-surface);border-bottom:1px solid var(--lp-border);height:auto;min-height:56px}
    .brand{display:flex;align-items:center;gap:6px;font-weight:700;letter-spacing:-.3px}
    .logo{color:var(--mat-sys-primary)} .ai{color:var(--mat-sys-primary);margin-left:2px}
    .title{font-size:18px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .spacer{flex:1} .offline{color:#d97706;margin-right:4px}
    .sync{font-size:12px;padding:2px 8px;border-radius:12px;background:var(--lp-chip);margin-right:4px}
    .content{flex:1;overflow-y:auto;padding:16px 16px 88px;max-width:960px;width:100%;margin:0 auto;box-sizing:border-box}
    .bottom{position:fixed;bottom:0;left:0;right:0;display:flex;justify-content:space-around;background:var(--lp-surface);
      border-top:1px solid var(--lp-border);padding:6px 0 calc(6px + env(safe-area-inset-bottom));z-index:5}
    .bottom a{display:flex;flex-direction:column;align-items:center;gap:2px;color:var(--lp-muted);text-decoration:none;font-size:11px;min-width:64px;padding:4px 0;border-radius:16px}
    .bottom a.active{color:var(--mat-sys-primary);font-weight:600}
    .bottom a.active mat-icon{background:var(--mat-sys-secondary-container);border-radius:16px;width:56px;font-variation-settings:'FILL' 1}
  `,
})
export class Shell implements OnInit {
  readonly api = inject(ApiService);
  private router = inject(Router);
  private platform = inject(PlatformService);
  readonly summary = signal<DashboardSummary | null>(null);
  readonly url = signal('');
  readonly unread = computed(() => this.summary()?.unreadNotifications ?? 0);
  readonly nav = [
    { path: '/dashboard', icon: 'space_dashboard', label: 'Home' },
    { path: '/leads', icon: 'groups', label: 'Leads' },
    { path: '/discover', icon: 'travel_explore', label: 'Discover' },
    { path: '/approvals', icon: 'mark_email_unread', label: 'Approvals', badge: 'approvals' },
    { path: '/more', icon: 'menu', label: 'More' },
  ];
  private readonly roots = ['/dashboard', '/leads', '/discover', '/approvals', '/more'];
  readonly isRoot = computed(() => this.roots.some(r => this.url().split('?')[0] === r));
  readonly title = signal('');

  ngOnInit() {
    this.url.set(this.router.url);
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(e => {
      this.url.set((e as NavigationEnd).urlAfterRedirects);
      this.title.set(document.title);
      void this.refreshBadges();
    });
    void this.refreshBadges();
    void this.platform.initPush();
    if (this.api.online()) void this.api.flushQueue();
  }

  badge(kind: string): number | null {
    return kind === 'approvals' ? this.summary()?.pendingApprovals || null : null;
  }

  async refreshBadges() {
    try { this.summary.set(await this.api.get<DashboardSummary>('/dashboard/summary')); } catch { /* offline */ }
  }

  back() { window.history.length > 1 ? window.history.back() : void this.router.navigateByUrl('/dashboard'); }
}
