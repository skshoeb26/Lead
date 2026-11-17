import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { DashboardMetrics, RateMetric } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-reports',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Reports</h1>
    <mat-button-toggle-group [(ngModel)]="days" (change)="load()" aria-label="Period">
      <mat-button-toggle [value]="7">7 days</mat-button-toggle><mat-button-toggle [value]="30">30 days</mat-button-toggle>
      <mat-button-toggle [value]="90">90 days</mat-button-toggle><mat-button-toggle [value]="365">1 year</mat-button-toggle>
    </mat-button-toggle-group>
    <lp-state [loading]="!m()" />
    @if (m(); as m) {
      <h2 class="section">Activity</h2>
      <div class="grid2 wide-4">
        <div class="card stat"><span class="value">{{ m.leadsDiscoveredToday }}</span><span class="label">Discovered today</span></div>
        <div class="card stat"><span class="value">{{ m.leadsDiscoveredInRange }}</span><span class="label">Discovered (period)</span></div>
        <div class="card stat"><span class="value">{{ m.leadsDiscoveredThisMonth }}</span><span class="label">This month</span></div>
        <div class="card stat"><span class="value">{{ m.leadsAnalysed }}</span><span class="label">Analysed</span></div>
        <div class="card stat"><span class="value">{{ m.qualifiedLeads }}</span><span class="label">Qualified (score ≥ 60)</span></div>
        <div class="card stat"><span class="value">{{ m.highPriority }}</span><span class="label">High priority (all)</span></div>
        <div class="card stat"><span class="value">{{ m.pitchDraftsGenerated }}</span><span class="label">Drafts generated</span></div>
        <div class="card stat"><span class="value">{{ m.emailsApproved }}</span><span class="label">Emails approved</span></div>
        <div class="card stat"><span class="value">{{ m.emailsSent }}</span><span class="label">Emails sent</span></div>
        <div class="card stat"><span class="value">{{ m.repliesRecorded }}</span><span class="label">Replies recorded</span></div>
        <div class="card stat"><span class="value">{{ m.meetingsScheduled }}</span><span class="label">Meetings</span></div>
        <div class="card stat"><span class="value">{{ m.proposalsSent }}</span><span class="label">Proposals sent</span></div>
        <div class="card stat"><span class="value">{{ m.projectsWon }}</span><span class="label">Projects won</span></div>
        <div class="card stat"><span class="value">{{ m.totalDealValue | inr }}</span><span class="label">Deal value won</span></div>
        <div class="card stat"><span class="value">{{ m.averageProjectValue | inr }}</span><span class="label">Avg project</span></div>
      </div>

      <h2 class="section">Conversion</h2>
      <div class="stack">
        @for (r of rates(m); track r.label) {
          <div class="card">
            <div class="row between"><b>{{ r.label }}</b><span class="value">{{ r.metric.value != null ? r.metric.value + '%' : 'n/a' }}</span></div>
            <div class="small">{{ r.metric.numerator }} of {{ r.metric.denominator }}</div>
            <div class="small muted">{{ r.metric.definition }}</div>
          </div>
        }
      </div>

      <h2 class="section">Costs (period)</h2>
      <div class="grid2">
        <div class="card stat"><span class="value">{{ '$' + (m.discoveryCostUsd | number: '1.2-2') }}</span><span class="label">Discovery API</span></div>
        <div class="card stat"><span class="value">{{ '$' + (m.aiCostUsd | number: '1.2-2') }}</span><span class="label">AI API</span></div>
        <div class="card stat"><span class="value">{{ '$' + (m.otherCostUsd | number: '1.2-2') }}</span><span class="label">Other APIs</span></div>
        <div class="card stat"><span class="value">{{ m.costPerQualifiedLeadUsd != null ? '$' + (m.costPerQualifiedLeadUsd | number: '1.2-3') : 'n/a' }}</span><span class="label">Est. cost / qualified lead</span></div>
      </div>

      <h2 class="section">Pipeline</h2>
      <div class="card stack">
        @for (p of m.pipeline; track p.stage) {
          <div class="row"><span class="lbl small">{{ p.stage | stage }}</span><div class="bar grow"><span [style.width.%]="pct(p.count, m)"></span></div><b class="small">{{ p.count }}</b></div>
        }
      </div>
      <button mat-stroked-button class="full exp" (click)="exportLeads()"><mat-icon>download</mat-icon>Export leads & CRM (CSV)</button>
    }
  `,
  styles: `.value{font-weight:700;font-size:20px} .lbl{width:110px} .bar{height:10px;border-radius:5px;background:var(--lp-chip);overflow:hidden}
    .bar span{display:block;height:100%;background:var(--mat-sys-primary)} .exp{margin-top:16px} mat-button-toggle-group{flex-wrap:wrap}`,
})
export class ReportsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly m = signal<DashboardMetrics | null>(null);
  days = 30;

  ngOnInit() { void this.load(); }

  async load() {
    const to = new Date();
    const from = new Date(to.getTime() - this.days * 86400000);
    try { this.m.set(await this.api.get<DashboardMetrics>('/dashboard/metrics', { from: from.toISOString(), to: to.toISOString() })); }
    catch (e) { this.toast.error(e); }
  }

  rates(m: DashboardMetrics): { label: string; metric: RateMetric }[] {
    return [{ label: 'Reply rate', metric: m.replyRate }, { label: 'Meeting rate', metric: m.meetingRate }, { label: 'Win rate', metric: m.winRate }];
  }

  pct(n: number, m: DashboardMetrics) { const max = Math.max(1, ...m.pipeline.map(p => p.count)); return (n / max) * 100; }

  async exportLeads() {
    try {
      const blob = await this.api.download('/leads/export');
      const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = 'leadpilot-crm.csv'; a.click();
    } catch (e) { this.toast.error(e); }
  }
}
