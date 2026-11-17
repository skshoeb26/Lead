import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { DashboardSummary, LeadListItem, Paged } from '../../core/models';
import { SHARED } from '../../shared/ui';

@Component({
  selector: 'lp-dashboard',
  imports: [RouterLink, MatIconModule, MatButtonModule, ...SHARED],
  template: `
    <h1 class="page">Hi {{ firstName() }} 👋</h1>
    <lp-offline-banner [show]="fromCache()" [cachedAt]="cachedAt()" />
    <lp-state [loading]="loading() && !s()" [error]="error()" />

    @if (s(); as s) {
      @if (s.emailTestMode) {
        <div class="notice info"><mat-icon>science</mat-icon><span><b>Test mode:</b> emails go through the full approval workflow but are not delivered. Disable on the server when ready.</span></div>
      }
      @if (s.sendingPaused) { <div class="notice warn"><mat-icon>pause_circle</mat-icon>All sending is paused.</div> }
      @if (s.outcomeUnknownEmails) {
        <a class="notice bad" routerLink="/approvals"><mat-icon>help</mat-icon>{{ s.outcomeUnknownEmails }} email(s) need reconciliation — check Gmail Sent.</a>
      }

      <div class="quick">
        <a class="card qa" routerLink="/discover"><mat-icon>travel_explore</mat-icon><span>Discover</span></a>
        <a class="card qa" routerLink="/approvals"><mat-icon>mark_email_unread</mat-icon><span>Approvals</span>@if (s.pendingApprovals) {<b class="dot">{{ s.pendingApprovals }}</b>}</a>
        <a class="card qa" routerLink="/followups"><mat-icon>event_upcoming</mat-icon><span>Follow-ups</span>@if (s.followUpsDueToday + s.followUpsOverdue) {<b class="dot">{{ s.followUpsDueToday + s.followUpsOverdue }}</b>}</a>
        <a class="card qa" routerLink="/crm"><mat-icon>view_kanban</mat-icon><span>Pipeline</span></a>
      </div>

      <h2 class="section">Overview</h2>
      <div class="grid2 wide-4">
        <a class="card stat" routerLink="/leads"><span class="value">{{ s.totalLeads }}</span><span class="label">Total leads</span></a>
        <a class="card stat" routerLink="/leads" [queryParams]="{ stage: 'New' }"><span class="value">{{ s.newLeads }}</span><span class="label">New</span></a>
        <a class="card stat" routerLink="/leads" [queryParams]="{ priority: 'High' }"><span class="value hi">{{ s.highPriorityLeads }}</span><span class="label">High priority</span></a>
        <div class="card stat"><span class="value">{{ s.analysedLeads }}</span><span class="label">Analysed</span></div>
        <a class="card stat" routerLink="/approvals"><span class="value">{{ s.pendingApprovals }}</span><span class="label">Pending approvals</span></a>
        <div class="card stat"><span class="value">{{ s.emailsSent }}</span><span class="label">Emails sent</span></div>
        <div class="card stat"><span class="value">{{ s.repliesRecorded }}</span><span class="label">Replies recorded</span></div>
        <div class="card stat"><span class="value">{{ s.meetingsUpcoming }}</span><span class="label">Upcoming meetings</span></div>
        <div class="card stat"><span class="value">{{ s.proposalsSent }}</span><span class="label">Proposals</span></div>
        <div class="card stat"><span class="value">{{ s.projectsWon }}</span><span class="label">Won projects</span></div>
        <div class="card stat span2"><span class="value">{{ s.revenueWon | inr }}</span><span class="label">Revenue won</span></div>
      </div>
    }

    <div class="row between"><h2 class="section">Top opportunities</h2><a mat-button routerLink="/leads" [queryParams]="{ sort: '-score' }">See all</a></div>
    <div class="list">
      @for (l of top(); track l.id) {
        <a class="list-item" [routerLink]="['/leads', l.id]">
          <lp-score [score]="l.score" [priority]="l.priority" />
          <div class="grow"><div class="ellipsis"><b>{{ l.businessName }}</b></div><div class="small muted ellipsis">{{ l.categoryName }} · {{ l.city }}</div></div>
          <span class="chip">{{ l.stage | stage }}</span>
        </a>
      } @empty {
        <lp-state [empty]="!loading()" icon="travel_explore" emptyText="No scored leads yet. Start a discovery or import a CSV.">
          <a action mat-flat-button routerLink="/discover">Discover leads</a>
        </lp-state>
      }
    </div>
  `,
  styles: `
    .quick{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:8px}
    .qa{display:flex;flex-direction:column;align-items:center;gap:4px;padding:12px 4px;font-size:12px;position:relative;text-align:center}
    .qa mat-icon{color:var(--mat-sys-primary)}
    .dot{position:absolute;top:6px;right:10px;background:var(--mat-sys-error);color:var(--mat-sys-on-error);border-radius:10px;font-size:11px;padding:0 6px}
    .hi{color:var(--lp-ok)} .span2{grid-column:span 2} a.notice{text-decoration:none;margin-top:8px}
    .notice{margin-bottom:8px}
  `,
})
export class DashboardPage implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);
  readonly s = signal<DashboardSummary | null>(null);
  readonly top = signal<LeadListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly fromCache = signal(false);
  readonly cachedAt = signal<string | undefined>(undefined);

  firstName() { return (this.auth.user()?.displayName ?? '').split(' ')[0] || 'there'; }

  async ngOnInit() {
    try {
      const [summary, leads] = await Promise.all([
        this.api.getCached<DashboardSummary>('/dashboard/summary'),
        this.api.getCached<Paged<LeadListItem>>('/leads', { sort: '-score', minScore: 1, pageSize: 5 }),
      ]);
      this.s.set(summary.data);
      this.top.set(leads.data.items);
      this.fromCache.set(summary.fromCache);
      this.cachedAt.set(summary.cachedAt);
    } catch (e) {
      this.error.set('Could not load the dashboard.');
    } finally {
      this.loading.set(false);
    }
  }
}
