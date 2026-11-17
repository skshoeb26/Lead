import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AppNotification, Paged } from '../../core/models';
import { SHARED } from '../../shared/ui';

@Component({
  selector: 'lp-notifications',
  imports: [MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <div class="row between"><h1 class="page">Notifications</h1><button mat-button (click)="readAll()">Mark all read</button></div>
    <lp-state [loading]="loading()" [empty]="!loading() && !items().length" icon="notifications_off" emptyText="No notifications." />
    <div class="list">
      @for (n of items(); track n.id) {
        <button class="list-item btn" [class.unread]="!n.readAt" (click)="open(n)">
          <mat-icon>{{ icon(n.type) }}</mat-icon>
          <div class="grow left"><b>{{ n.title }}</b><div class="small">{{ n.body }}</div><div class="small muted">{{ n.createdAt | date: 'short' }}</div></div>
        </button>
      }
    </div>
  `,
  styles: `.btn{width:100%;font:inherit;cursor:pointer;border:1px solid var(--lp-border)} .left{text-align:left} .unread{border-left:4px solid var(--mat-sys-primary)}`,
})
export class NotificationsPage implements OnInit {
  private api = inject(ApiService);
  private router = inject(Router);
  readonly items = signal<AppNotification[]>([]);
  readonly loading = signal(true);

  async ngOnInit() {
    try { this.items.set((await this.api.get<Paged<AppNotification>>('/notifications', { pageSize: 50 })).items); } catch { /* offline */ }
    finally { this.loading.set(false); }
  }

  async open(n: AppNotification) {
    if (!n.readAt) await this.api.post(`/notifications/${n.id}/read`).catch(() => undefined);
    if (n.deepLink) await this.router.navigateByUrl(n.deepLink);
  }

  async readAll() { await this.api.post('/notifications/read').catch(() => undefined); this.items.set(this.items().map(n => ({ ...n, readAt: n.readAt ?? new Date().toISOString() }))); }

  icon(t: string) {
    return ({ DiscoveryCompleted: 'travel_explore', HighPriorityLeads: 'star', AnalysisCompleted: 'troubleshoot', ApprovalPending: 'mark_email_unread',
      EmailFailed: 'error', FollowUpDue: 'event', ReplyRecorded: 'reply', JobError: 'warning', BudgetWarning: 'payments' } as Record<string, string>)[t] ?? 'notifications';
  }
}
