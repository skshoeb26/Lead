import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Meeting, PIPELINE_STAGES, PipelineCard, PipelineColumn, PipelineStage, Proposal } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-pipeline',
  imports: [FormsModule, RouterLink, MatButtonToggleModule, MatButtonModule, MatIconModule, MatMenuModule, ...SHARED],
  template: `
    <div class="row between"><h1 class="page">CRM pipeline</h1>
      <mat-button-toggle-group [(ngModel)]="view" aria-label="View"><mat-button-toggle value="kanban"><mat-icon>view_kanban</mat-icon></mat-button-toggle><mat-button-toggle value="table"><mat-icon>table_rows</mat-icon></mat-button-toggle></mat-button-toggle-group>
    </div>
    <lp-offline-banner [show]="fromCache()" />
    <lp-state [loading]="loading()" />

    @if (view === 'kanban') {
      <div class="board">
        @for (col of columns(); track col.stage) {
          <section class="col">
            <header><b>{{ col.stage | stage }}</b><span class="chip">{{ col.count }}</span></header>
            @if (col.expectedValue) { <div class="small muted">{{ col.expectedValue | inr }}</div> }
            @for (c of col.leads; track c.id) {
              <div class="card tight cardi">
                <a [routerLink]="['/leads', c.id]" class="row"><lp-score [score]="c.score" [priority]="c.priority" /><div class="grow"><div class="ellipsis"><b>{{ c.businessName }}</b></div><div class="small muted">{{ c.city }}</div></div></a>
                <div class="row between small"><span>{{ c.expectedDealValue | inr }}</span>
                  <button mat-icon-button [matMenuTriggerFor]="move" aria-label="Move stage"><mat-icon>swap_horiz</mat-icon></button></div>
                <mat-menu #move="matMenu">@for (s of stages; track s) { <button mat-menu-item [disabled]="s === col.stage" (click)="moveTo(c, s)">{{ s | stage }}</button> }</mat-menu>
              </div>
            }
          </section>
        }
      </div>
    } @else {
      <div class="table">
        <div class="tr th"><span>Lead</span><span>Stage</span><span>Score</span><span>Value</span></div>
        @for (col of columns(); track col.stage) {
          @for (c of col.leads; track c.id) {
            <a class="tr" [routerLink]="['/leads', c.id]"><span class="ellipsis">{{ c.businessName }}</span><span>{{ col.stage | stage }}</span><span>{{ c.score ?? '–' }}</span><span>{{ c.expectedDealValue | inr }}</span></a>
          }
        }
      </div>
    }

    <h2 class="section">Upcoming meetings</h2>
    <div class="list">
      @for (m of meetings(); track m.id) {
        <a class="list-item" [routerLink]="['/leads', m.leadId]"><mat-icon>groups</mat-icon><div class="grow"><b>{{ m.leadName }}</b><div class="small muted">{{ m.scheduledAt | date: 'EEE d MMM, h:mm a' }} · {{ m.location }}</div></div></a>
      } @empty { <p class="small muted">No upcoming meetings.</p> }
    </div>
    <h2 class="section">Proposals</h2>
    <div class="list">
      @for (p of proposals(); track p.id) {
        <a class="list-item" [routerLink]="['/leads', p.leadId]"><mat-icon>request_quote</mat-icon><div class="grow"><b>{{ p.title }}</b><div class="small muted">{{ p.leadName }} · {{ p.amount | inr }}</div></div><span class="chip">{{ p.status }}</span></a>
      } @empty { <p class="small muted">No proposals yet.</p> }
    </div>
  `,
  styles: `
    .board{display:flex;gap:12px;overflow-x:auto;padding-bottom:12px;scroll-snap-type:x mandatory;margin:0 -16px;padding-left:16px;padding-right:16px}
    .col{flex:0 0 78%;max-width:300px;scroll-snap-align:start;background:var(--lp-chip);border-radius:16px;padding:10px;display:flex;flex-direction:column;gap:8px;max-height:60vh;overflow-y:auto}
    .col header{display:flex;justify-content:space-between;align-items:center} .cardi a{color:inherit;text-decoration:none}
    .table{display:flex;flex-direction:column;border:1px solid var(--lp-border);border-radius:12px;overflow:hidden}
    .tr{display:grid;grid-template-columns:2fr 1.2fr .6fr 1fr;gap:8px;padding:10px 12px;border-bottom:1px solid var(--lp-border);color:inherit;text-decoration:none;font-size:14px;background:var(--lp-card)}
    .th{font-weight:600;background:var(--lp-chip)}
  `,
})
export class PipelinePage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly stages = PIPELINE_STAGES;
  readonly columns = signal<PipelineColumn[]>([]);
  readonly meetings = signal<Meeting[]>([]);
  readonly proposals = signal<Proposal[]>([]);
  readonly loading = signal(true);
  readonly fromCache = signal(false);
  view: 'kanban' | 'table' = 'kanban';

  async ngOnInit() { await this.load(); }

  async load() {
    try {
      const r = await this.api.getCached<PipelineColumn[]>('/crm/pipeline', { perStage: 30 });
      this.columns.set(r.data.filter(c => c.count > 0 || ['New', 'Qualified', 'Contacted', 'Interested', 'Won'].includes(c.stage)));
      this.fromCache.set(r.fromCache);
      this.meetings.set(await this.api.get<Meeting[]>('/crm/meetings', { upcoming: true }));
      this.proposals.set(await this.api.get<Proposal[]>('/crm/proposals'));
    } catch { /* offline handled by banner */ }
    finally { this.loading.set(false); }
  }

  async moveTo(card: PipelineCard, stage: PipelineStage) {
    const r = await this.api.mutateOrQueue('POST', `/leads/${card.id}/stage`, { stage }, `Stage → ${stage}`).catch(e => { this.toast.error(e); return null; });
    if (r?.queued) this.toast.ok('Offline — will sync.'); else if (r) await this.load();
  }
}
