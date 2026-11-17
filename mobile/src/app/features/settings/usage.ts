import { Component, OnInit, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../core/api.service';
import { UsageSummary } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-usage',
  imports: [MatIconModule, MatProgressBarModule, ...SHARED],
  template: `
    <h1 class="page">API usage & budget</h1>
    <lp-state [loading]="!u()" />
    @if (u(); as u) {
      @if (u.aiBudgetReached) { <div class="notice bad"><mat-icon>block</mat-icon>AI budget reached — AI analysis is paused.</div> }
      @if (u.discoveryBudgetReached) { <div class="notice bad"><mat-icon>block</mat-icon>Discovery budget reached.</div> }
      <div class="card stack">
        <b>AI this month</b>
        <mat-progress-bar mode="determinate" [value]="pct(u.aiMonthUsd, u.monthlyAIBudgetUsd)" />
        <div class="small">{{ '$' + (u.aiMonthUsd | number: '1.2-2') }} of {{ '$' + (u.monthlyAIBudgetUsd | number: '1.0-0') }} · today {{ '$' + (u.aiDayUsd | number: '1.2-2') }} of {{ '$' + (u.dailyAIBudgetUsd | number: '1.0-0') }}</div>
        <div class="small muted">{{ u.aiRequestsMonth }} requests · {{ u.aiInputTokensMonth | number }} input / {{ u.aiOutputTokensMonth | number }} output tokens</div>
      </div>
      <div class="card stack">
        <b>Discovery this month</b>
        <mat-progress-bar mode="determinate" [value]="pct(u.discoveryMonthUsd, u.monthlyDiscoveryBudgetUsd)" />
        <div class="small">{{ '$' + (u.discoveryMonthUsd | number: '1.2-2') }} of {{ '$' + (u.monthlyDiscoveryBudgetUsd | number: '1.0-0') }} · {{ u.discoveryRequestsMonth }} requests</div>
      </div>
      <div class="card stack">
        <b>Estimated monthly total</b>
        <div class="value">{{ '$' + (u.aiMonthUsd + u.discoveryMonthUsd + u.otherMonthUsd + u.hostingMonthUsd | number: '1.2-2') }}</div>
        <div class="small muted">Includes {{ '$' + (u.hostingMonthUsd | number: '1.0-0') }} hosting (set in Settings → Budgets). Provider costs are estimates; check your provider dashboards for invoices.</div>
      </div>
      <h2 class="section">By provider</h2>
      <div class="list">
        @for (p of u.byProvider; track p.provider) {
          <div class="list-item"><b class="grow">{{ p.provider }}</b><span class="small">{{ p.requests }} req{{ p.failures ? ' · ' + p.failures + ' failed' : '' }}</span><b>{{ '$' + (p.costUsd | number: '1.2-3') }}</b></div>
        } @empty { <p class="small muted">No usage this month.</p> }
      </div>
    }
  `,
  styles: `.card{margin-bottom:12px} .value{font-size:26px;font-weight:700}`,
})
export class UsagePage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly u = signal<UsageSummary | null>(null);
  async ngOnInit() { try { this.u.set(await this.api.get<UsageSummary>('/usage')); } catch (e) { this.toast.error(e); } }
  pct(v: number, max: number) { return max ? Math.min(100, (v / max) * 100) : 0; }
}
