import { Component, OnInit, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { EmailDraft, EmailStatus, Paged } from '../../core/models';
import { SHARED } from '../../shared/ui';

export function statusClass(s: EmailStatus): string {
  return ({ Sent: 'ok', Approved: 'info', Queued: 'info', Sending: 'info', PendingApproval: 'warn', Failed: 'bad', OutcomeUnknown: 'bad', Rejected: '', Expired: 'warn', Draft: '' } as Record<string, string>)[s] ?? '';
}

const TABS: { label: string; statuses: EmailStatus[] }[] = [
  { label: 'To review', statuses: ['PendingApproval', 'Draft', 'Expired'] },
  { label: 'Approved', statuses: ['Approved', 'Queued', 'Sending'] },
  { label: 'Attention', statuses: ['Failed', 'OutcomeUnknown'] },
  { label: 'Sent', statuses: ['Sent'] },
  { label: 'Rejected', statuses: ['Rejected'] },
];

@Component({
  selector: 'lp-approval-list',
  imports: [RouterLink, MatTabsModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Email approvals</h1>
    <div class="notice info"><mat-icon>verified_user</mat-icon><span>Nothing is sent without your explicit approval. Editing an approved email requires approving it again.</span></div>
    <mat-tab-group mat-stretch-tabs="false" (selectedIndexChange)="select($event)">
      @for (t of tabs; track t.label) { <mat-tab [label]="t.label" /> }
    </mat-tab-group>
    <lp-offline-banner [show]="fromCache()" [cachedAt]="cachedAt()" />
    <lp-state [loading]="loading()" [empty]="!loading() && !items().length" icon="inbox" emptyText="No emails here." />
    <div class="list">
      @for (e of items(); track e.id) {
        <a class="list-item" [routerLink]="['/approvals', e.id]">
          <mat-icon>{{ e.status === 'Sent' ? 'mark_email_read' : 'mail' }}</mat-icon>
          <div class="grow">
            <div class="ellipsis"><b>{{ e.subject }}</b></div>
            <div class="small muted ellipsis">{{ e.leadName }} · {{ e.toAddress }}</div>
          </div>
          <span class="chip" [class]="statusClass(e.status)">{{ e.status | humanize }}</span>
        </a>
      }
    </div>
  `,
  styles: `.notice{margin-bottom:8px} .list{margin-top:12px}`,
})
export class ApprovalListPage implements OnInit {
  private api = inject(ApiService);
  readonly tabs = TABS;
  readonly items = signal<EmailDraft[]>([]);
  readonly loading = signal(true);
  readonly fromCache = signal(false);
  readonly cachedAt = signal<string | undefined>(undefined);
  readonly statusClass = statusClass;

  ngOnInit() { void this.select(0); }

  async select(i: number) {
    this.loading.set(true);
    try {
      const r = await this.api.getCached<Paged<EmailDraft>>('/emails', { status: TABS[i].statuses, pageSize: 50 });
      this.items.set(r.data.items);
      this.fromCache.set(r.fromCache);
      this.cachedAt.set(r.cachedAt);
    } catch { this.items.set([]); }
    finally { this.loading.set(false); }
  }
}
