import { Component, OnDestroy, OnInit, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DiscoveryJob, DiscoveryResult, Paged } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-job-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, ...SHARED],
  template: `
    <lp-state [loading]="!job()" />
    @if (job(); as j) {
      <h1 class="page">{{ j.name }}</h1>
      <div class="card stack">
        <div class="row between"><span class="chip" [class.ok]="j.status === 'Completed'" [class.info]="j.status === 'Running'" [class.bad]="j.status === 'Failed'">{{ j.status }}</span>
          <span class="small muted">{{ j.provider }}</span></div>
        <mat-progress-bar [mode]="j.status === 'Running' && !j.progress ? 'indeterminate' : 'determinate'" [value]="j.progress * 100" />
        <div class="grid3">
          <div class="stat"><span class="value">{{ j.createdCount }}</span><span class="label">New leads</span></div>
          <div class="stat"><span class="value">{{ j.duplicateCount }}</span><span class="label">Duplicates</span></div>
          <div class="stat"><span class="value">{{ j.skippedCount }}</span><span class="label">Skipped</span></div>
        </div>
        <div class="small muted">Segments {{ j.completedSegments }}/{{ j.totalSegments }} · Found {{ j.foundCount }}/{{ j.maxResults }} · Cost {{ '$' + (j.actualCostUsd | number: '1.2-3') }} (est. {{ '$' + (j.estimatedCostUsd | number: '1.2-3') }})</div>
        @if (j.error) { <div class="notice bad"><mat-icon>error</mat-icon>{{ j.error }}</div> }
        <div class="actions">
          @if (j.status === 'Running' || j.status === 'Pending') { <button mat-stroked-button (click)="act('pause')"><mat-icon>pause</mat-icon>Pause</button> }
          @if (j.status === 'Paused' || j.status === 'Failed') { <button mat-flat-button (click)="act('resume')"><mat-icon>play_arrow</mat-icon>Resume</button> }
          @if (j.status !== 'Completed' && j.status !== 'Cancelled') { <button mat-button (click)="act('cancel')"><mat-icon>close</mat-icon>Cancel</button> }
          <a mat-button routerLink="/leads" [queryParams]="{ sort: '-discovered' }"><mat-icon>groups</mat-icon>View leads</a>
        </div>
      </div>

      <h2 class="section">Results</h2>
      <div class="list">
        @for (r of results(); track r.id) {
          <a class="list-item" [routerLink]="r.leadId ? ['/leads', r.leadId] : null">
            <mat-icon [class]="r.outcome">{{ icon(r.outcome) }}</mat-icon>
            <div class="grow"><div class="ellipsis">{{ r.businessName }}</div>@if (r.message) {<div class="small muted">{{ r.message }}</div>}</div>
            <span class="chip">{{ r.outcome }}</span>
          </a>
        } @empty { <lp-state [empty]="true" emptyText="No results yet." /> }
      </div>
    }
  `,
  styles: `.Created{color:var(--lp-ok)} .Duplicate{color:var(--lp-muted)} .Skipped{color:var(--lp-warn)} .Error{color:var(--lp-bad)}`,
})
export class JobDetailPage implements OnInit, OnDestroy {
  readonly id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly job = signal<DiscoveryJob | null>(null);
  readonly results = signal<DiscoveryResult[]>([]);
  private timer?: ReturnType<typeof setInterval>;

  async ngOnInit() {
    await this.load();
    this.timer = setInterval(() => { const s = this.job()?.status; if (s === 'Running' || s === 'Pending') void this.load(); }, 3000);
  }
  ngOnDestroy() { clearInterval(this.timer); }

  async load() {
    try {
      this.job.set(await this.api.get<DiscoveryJob>(`/discovery/jobs/${this.id()}`));
      this.results.set((await this.api.get<Paged<DiscoveryResult>>(`/discovery/jobs/${this.id()}/results`, { pageSize: 100 })).items);
    } catch (e) { this.toast.error(e); }
  }

  async act(action: 'pause' | 'resume' | 'cancel') {
    try { this.job.set(await this.api.post<DiscoveryJob>(`/discovery/jobs/${this.id()}/${action}`)); }
    catch (e) { this.toast.error(e); }
  }

  icon(o: string) { return ({ Created: 'add_circle', Duplicate: 'content_copy', Skipped: 'filter_alt_off', Error: 'error' } as Record<string, string>)[o] ?? 'circle'; }
}
