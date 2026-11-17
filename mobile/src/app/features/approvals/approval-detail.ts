import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { EmailAudit, EmailDraft, SendCheck } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';
import { statusClass } from './approval-list';

@Component({
  selector: 'lp-approval-detail',
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatExpansionModule, MatProgressBarModule, ...SHARED],
  template: `
    <lp-offline-banner [show]="fromCache()" [cachedAt]="cachedAt()" />
    <lp-state [loading]="!d()" />
    @if (d(); as d) {
      <div class="row between"><h1 class="page">Review email</h1><span class="chip" [class]="statusClass(d.status)">{{ d.status | humanize }}</span></div>
      @if (d.testMode) { <div class="notice info"><mat-icon>science</mat-icon>Server is in test mode: approved emails are processed but not delivered.</div> }
      @if (d.lastError) { <div class="notice bad"><mat-icon>error</mat-icon>{{ d.lastError }}</div> }

      @if (!editing()) {
        <div class="card stack">
          <div class="kv"><span>Lead</span><a [routerLink]="['/leads', d.leadId]">{{ d.leadName }}</a></div>
          <div class="kv"><span>To</span><b>{{ d.toAddress }}</b></div>
          <div class="kv"><span>Source</span><span class="small" [class.bad]="!d.recipientSourceRecorded">{{ d.recipientSource ?? 'Not a recorded contact of this lead' }}</span></div>
          @if (d.senderAddress) { <div class="kv"><span>From</span><span>{{ d.senderAddress }}</span></div> }
          <div class="kv"><span>Subject</span><b>{{ d.subject }}</b></div>
          <div class="body pre">{{ d.body }}</div>
          <div class="small muted">Version {{ d.version }} · content fingerprint {{ d.contentHash.slice(0, 12) }}</div>
          @if (d.approvalExpiresAt && d.status === 'Approved') { <div class="small muted">Approval valid until {{ d.approvalExpiresAt | date: 'medium' }}</div> }
          @if (d.sentAt) { <div class="small">Accepted by Gmail {{ d.sentAt | date: 'medium' }}{{ d.gmailMessageId ? ' · id ' + d.gmailMessageId : '' }}. <span class="muted">This confirms Gmail accepted the message, not that it was delivered or read.</span></div> }
        </div>
      } @else {
        <div class="card stack">
          <div class="notice warn"><mat-icon>edit</mat-icon>Saving changes invalidates any previous approval.</div>
          <mat-form-field><mat-label>To</mat-label><input matInput [(ngModel)]="to" inputmode="email" /></mat-form-field>
          <mat-form-field><mat-label>Subject</mat-label><input matInput [(ngModel)]="subject" maxlength="200" /></mat-form-field>
          <mat-form-field><mat-label>Body</mat-label><textarea matInput rows="14" [(ngModel)]="body"></textarea></mat-form-field>
          <div class="actions"><button mat-flat-button (click)="saveEdit()" [disabled]="busy()">Save changes</button><button mat-button (click)="editing.set(false)">Cancel</button></div>
        </div>
      }

      @if (check(); as c) {
        @if (!c.canSend && d.status === 'Approved') {
          <div class="notice bad"><mat-icon>block</mat-icon><div>Cannot send yet:<ul>@for (p of c.problems; track $index) {<li>{{ p }}</li>}</ul></div></div>
        }
      }
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }

      <div class="actions sticky">
        @if (canEdit()) { <button mat-stroked-button (click)="startEdit()" [disabled]="busy()"><mat-icon>edit</mat-icon>Edit</button> }
        @if (canApprove()) { <button mat-flat-button (click)="approve()" [disabled]="busy() || editing()"><mat-icon>check</mat-icon>Approve</button> }
        @if (d.status === 'Approved') { <button mat-flat-button class="send" (click)="send()" [disabled]="busy() || !check()?.canSend"><mat-icon>send</mat-icon>Send now</button> }
        @if (canReject()) { <button mat-button (click)="reject()" [disabled]="busy()"><mat-icon>close</mat-icon>Reject</button> }
      </div>

      @if (d.status === 'OutcomeUnknown') {
        <div class="card stack">
          <b>Was this email sent?</b>
          <p class="small">The connection to Gmail was lost during sending, so we cannot know whether Gmail accepted it. Open Gmail → Sent and check for “{{ d.subject }}”. LeadPilot will not resend automatically.</p>
          <div class="actions"><button mat-flat-button (click)="resolve(true)">Yes, it's in Sent</button><button mat-stroked-button (click)="resolve(false)">No, not sent</button></div>
        </div>
      }

      <mat-expansion-panel class="audit" (opened)="loadAudit()">
        <mat-expansion-panel-header><mat-panel-title><mat-icon>history</mat-icon>&nbsp;Audit history</mat-panel-title></mat-expansion-panel-header>
        @for (a of audit(); track $index) {
          <div class="ev"><b class="small">{{ a.action | humanize }}</b> <span class="small muted">v{{ a.version }} · {{ a.at | date: 'medium' }}</span>@if (a.details) {<div class="small">{{ a.details }}</div>}</div>
        }
      </mat-expansion-panel>
    }
  `,
  styles: `
    .kv{display:flex;justify-content:space-between;gap:12px;font-size:14px} .kv>span:first-child{color:var(--lp-muted);flex:none}
    .body{padding:12px;border-radius:12px;background:var(--lp-chip);font-size:14px;line-height:1.5}
    .bad{color:var(--lp-bad)} .sticky{position:sticky;bottom:76px;background:var(--lp-bg);padding:8px 0;z-index:2}
    .send{background:var(--lp-ok) !important;color:#fff !important} .audit{margin-top:12px;border-radius:16px !important} .ev{padding:6px 0;border-bottom:1px solid var(--lp-border)}
    ul{margin:4px 0 0;padding-left:18px}
  `,
})
export class ApprovalDetailPage implements OnInit, OnDestroy {
  readonly id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly d = signal<EmailDraft | null>(null);
  readonly check = signal<SendCheck | null>(null);
  readonly audit = signal<EmailAudit[]>([]);
  readonly busy = signal(false);
  readonly editing = signal(false);
  readonly fromCache = signal(false);
  readonly cachedAt = signal<string | undefined>(undefined);
  readonly statusClass = statusClass;
  readonly canEdit = computed(() => ['Draft', 'PendingApproval', 'Approved', 'Expired', 'Failed', 'Rejected'].includes(this.d()?.status ?? ''));
  readonly canApprove = computed(() => ['Draft', 'PendingApproval', 'Expired', 'Failed'].includes(this.d()?.status ?? ''));
  readonly canReject = computed(() => ['Draft', 'PendingApproval', 'Approved', 'Expired', 'Failed', 'Queued'].includes(this.d()?.status ?? ''));
  to = '';
  subject = '';
  body = '';
  private poll?: ReturnType<typeof setInterval>;

  async ngOnInit() { await this.load(); }
  ngOnDestroy() { clearInterval(this.poll); }

  async load() {
    try {
      const r = await this.api.getCached<EmailDraft>(`/emails/${this.id()}`);
      this.d.set(r.data);
      this.fromCache.set(r.fromCache);
      this.cachedAt.set(r.cachedAt);
      if (r.data.status === 'Approved' && !r.fromCache) this.check.set(await this.api.get<SendCheck>(`/emails/${this.id()}/send-check`));
      if (!['Queued', 'Sending'].includes(r.data.status)) { clearInterval(this.poll); this.poll = undefined; }
    } catch (e) { this.toast.error(e); }
  }

  startEdit() { const d = this.d()!; this.to = d.toAddress; this.subject = d.subject; this.body = d.body; this.editing.set(true); }

  async saveEdit() {
    await this.run(async () => {
      this.d.set(await this.api.put<EmailDraft>(`/emails/${this.id()}`, { toAddress: this.to, subject: this.subject, body: this.body, concurrencyStamp: this.d()!.concurrencyStamp }));
      this.editing.set(false);
      this.check.set(null);
      this.toast.ok('Saved. Review and approve the new version.');
    });
  }

  async approve() {
    const d = this.d()!;
    if (!confirm(`Approve this email to ${d.toAddress}?`)) return;
    // The hash + version prove the approval is for exactly what is on screen.
    await this.run(async () => {
      this.d.set(await this.api.post<EmailDraft>(`/emails/${this.id()}/approve`, { contentHash: d.contentHash, version: d.version }));
      this.check.set(await this.api.get<SendCheck>(`/emails/${this.id()}/send-check`));
      this.toast.ok('Approved.');
    });
  }

  async send() {
    if (!confirm(`Send this email now to ${this.d()!.toAddress}?`)) return;
    await this.run(async () => {
      this.d.set(await this.api.post<EmailDraft>(`/emails/${this.id()}/send`));
      this.toast.ok('Queued for sending.');
      clearInterval(this.poll);
      this.poll = setInterval(() => void this.load(), 3000);
    });
  }

  async reject() {
    const reason = prompt('Reason for rejecting (optional)');
    if (reason === null) return;
    await this.run(async () => { this.d.set(await this.api.post<EmailDraft>(`/emails/${this.id()}/reject`, { reason })); });
  }

  async resolve(wasSent: boolean) {
    await this.run(async () => { this.d.set(await this.api.post<EmailDraft>(`/emails/${this.id()}/resolve-unknown`, { wasSent, note: wasSent ? 'Found in Gmail Sent' : 'Not in Gmail Sent' })); });
  }

  async loadAudit() { try { this.audit.set(await this.api.get<EmailAudit[]>(`/emails/${this.id()}/audit`)); } catch (e) { this.toast.error(e); } }

  private async run(fn: () => Promise<void>) {
    this.busy.set(true);
    try { await fn(); } catch (e) { this.toast.error(e); await this.load(); } finally { this.busy.set(false); }
  }
}
