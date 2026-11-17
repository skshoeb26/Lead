import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { FollowUp } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

const BUCKETS = ['overdue', 'today', 'upcoming', 'completed'] as const;

@Component({
  selector: 'lp-followups',
  imports: [RouterLink, MatTabsModule, MatButtonModule, MatIconModule, MatMenuModule, ...SHARED],
  template: `
    <h1 class="page">Follow-ups</h1>
    <p class="small muted">Reminders and drafts only — follow-up emails are never sent without your approval.</p>
    <mat-tab-group mat-stretch-tabs="false" (selectedIndexChange)="select($event)">
      <mat-tab label="Overdue" /><mat-tab label="Today" /><mat-tab label="Upcoming" /><mat-tab label="Done" />
    </mat-tab-group>
    <lp-state [loading]="loading()" [empty]="!loading() && !items().length" icon="event_available" emptyText="Nothing here — nice!" />
    <div class="list top">
      @for (f of items(); track f.id) {
        <div class="list-item">
          <mat-icon [class.over]="f.isOverdue">{{ f.isOverdue ? 'alarm' : 'event' }}</mat-icon>
          <a class="grow link" [routerLink]="['/leads', f.leadId]">
            <div class="ellipsis"><b>{{ f.leadName }}</b></div>
            <div class="small muted">{{ f.dueAt | date: 'EEE d MMM, h:mm a' }} · {{ f.note || f.channel }}</div>
            @if (f.emailDraftId) { <span class="chip info">Draft awaiting approval</span> }
          </a>
          @if (f.status === 'Pending') {
            <button mat-icon-button [matMenuTriggerFor]="m" aria-label="Actions"><mat-icon>more_vert</mat-icon></button>
            <mat-menu #m="matMenu">
              @if (f.emailDraftId) { <a mat-menu-item [routerLink]="['/approvals', f.emailDraftId]"><mat-icon>mail</mat-icon>Review draft</a> }
              <button mat-menu-item (click)="update(f, { status: 'Completed' })"><mat-icon>check</mat-icon>Mark done</button>
              <button mat-menu-item (click)="update(f, { snoozeHours: 24 })"><mat-icon>snooze</mat-icon>Snooze 1 day</button>
              <button mat-menu-item (click)="update(f, { snoozeHours: 72 })"><mat-icon>snooze</mat-icon>Snooze 3 days</button>
              <button mat-menu-item (click)="update(f, { status: 'Cancelled' })"><mat-icon>cancel</mat-icon>Cancel</button>
            </mat-menu>
          }
        </div>
      }
    </div>
  `,
  styles: `.top{margin-top:12px} .over{color:var(--lp-bad)} .link{color:inherit;text-decoration:none}`,
})
export class FollowUpsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly items = signal<FollowUp[]>([]);
  readonly loading = signal(true);
  private bucket: (typeof BUCKETS)[number] = 'overdue';

  ngOnInit() { void this.select(0); }

  async select(i: number) {
    this.bucket = BUCKETS[i];
    this.loading.set(true);
    try { this.items.set((await this.api.getCached<FollowUp[]>('/followups', { bucket: this.bucket })).data); }
    catch { this.items.set([]); }
    finally { this.loading.set(false); }
  }

  async update(f: FollowUp, body: Record<string, unknown>) {
    const r = await this.api.mutateOrQueue('PUT', `/followups/${f.id}`, body, 'Follow-up update').catch(e => { this.toast.error(e); return null; });
    if (!r) return;
    this.items.set(this.items().filter(x => x.id !== f.id));
    this.toast.ok(r.queued ? 'Saved offline — will sync.' : 'Updated.');
  }
}
