import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { EmailDraft, LeadDetail, Paged, Pitch, PitchLanguage, PitchType } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

const TYPES: { value: PitchType; label: string; email: boolean }[] = [
  { value: 'IntroEmail', label: 'Introduction email', email: true },
  { value: 'FollowUp', label: 'Follow-up email', email: true },
  { value: 'MeetingRequest', label: 'Meeting request', email: true },
  { value: 'WhatsApp', label: 'WhatsApp message', email: false },
  { value: 'LinkedIn', label: 'LinkedIn note', email: false },
  { value: 'ShortIntro', label: 'Short introduction', email: false },
  { value: 'WebsiteProposal', label: 'Website improvement proposal', email: true },
  { value: 'ProposalOutline', label: 'Service proposal outline', email: true },
  { value: 'Quotation', label: 'Quotation draft', email: true },
];

@Component({
  selector: 'lp-pitch-studio',
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatIconModule, MatProgressBarModule, ...SHARED],
  template: `
    <h1 class="page">Pitch Studio</h1>
    @if (lead(); as l) {
      <p class="muted small">For <b>{{ l.businessName }}</b> — drafts reference only observed opportunities.</p>
      <div class="card stack">
        <mat-form-field><mat-label>Draft type</mat-label>
          <mat-select [(ngModel)]="type">@for (t of types; track t.value) { <mat-option [value]="t.value">{{ t.label }}</mat-option> }</mat-select></mat-form-field>
        <mat-button-toggle-group [(ngModel)]="language" aria-label="Language">
          <mat-button-toggle value="English">English</mat-button-toggle>
          <mat-button-toggle value="Hindi">हिन्दी</mat-button-toggle>
          <mat-button-toggle value="Hinglish">Hinglish</mat-button-toggle>
        </mat-button-toggle-group>
        <div>
          <div class="small muted">Reference these opportunities</div>
          @for (o of l.opportunities; track o.id) { <mat-checkbox [checked]="selected.has(o.id)" (change)="toggle(o.id, $event.checked)">{{ o.title }}</mat-checkbox> }
          @empty { <div class="notice warn"><mat-icon>info</mat-icon>No opportunities yet — analyse the lead first so the pitch can be evidence-based.</div> }
        </div>
        <mat-form-field><mat-label>Extra guidance (optional)</mat-label><input matInput [(ngModel)]="extra" maxlength="500" placeholder="e.g. mention I'm based in Pune" /></mat-form-field>
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
        <button mat-flat-button (click)="generate()" [disabled]="busy() || !l.opportunities.length"><mat-icon>auto_awesome</mat-icon>Generate draft</button>
      </div>
    }

    @if (pitch(); as p) {
      <div class="card stack edit">
        <div class="row between"><b>{{ typeLabel(p.type) }} · {{ p.language }}</b>@if (p.isMock) {<span class="chip warn">Mock AI</span>}</div>
        @if (isEmail(p.type)) { <mat-form-field><mat-label>Subject</mat-label><input matInput [(ngModel)]="subject" maxlength="200" /></mat-form-field> }
        <mat-form-field><mat-label>Message</mat-label><textarea matInput rows="12" [(ngModel)]="body"></textarea></mat-form-field>
        <div class="actions">
          <button mat-stroked-button (click)="save()" [disabled]="busy()"><mat-icon>save</mat-icon>Save edits</button>
          @if (isEmail(p.type)) {
            <button mat-flat-button (click)="toApproval()" [disabled]="busy()"><mat-icon>fact_check</mat-icon>Send for approval</button>
          } @else {
            <button mat-stroked-button (click)="copy()"><mat-icon>content_copy</mat-icon>Copy</button>
            @if (p.type === 'WhatsApp' && phone()) { <button mat-stroked-button (click)="openWhatsApp()"><mat-icon>chat</mat-icon>Open WhatsApp</button> }
          }
        </div>
        @if (p.type === 'WhatsApp') {
          <div class="notice warn"><mat-icon>gpp_maybe</mat-icon>Only message businesses that have opted in to WhatsApp contact. A public phone number is not consent. Send manually after review.</div>
        }
        <p class="small muted">{{ p.model }} · {{ p.promptVersion }} · {{ p.updatedAt | date: 'short' }}</p>
      </div>
    }

    @if (history().length) {
      <h2 class="section">Saved drafts</h2>
      <div class="list">
        @for (h of history(); track h.id) {
          <button class="list-item" (click)="open(h)"><mat-icon>description</mat-icon>
            <div class="grow left"><div class="ellipsis">{{ h.subject || typeLabel(h.type) }}</div><div class="small muted">{{ typeLabel(h.type) }} · {{ h.language }} · {{ h.updatedAt | date: 'short' }}{{ h.isEdited ? ' · edited' : '' }}</div></div></button>
        }
      </div>
    }
  `,
  styles: `mat-button-toggle-group{flex-wrap:wrap} .edit{margin-top:12px} button.list-item{border:1px solid var(--lp-border);font:inherit;cursor:pointer;text-align:left;width:100%} .left{text-align:left}`,
})
export class PitchStudioPage implements OnInit {
  readonly id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  private router = inject(Router);
  readonly types = TYPES;
  readonly lead = signal<LeadDetail | null>(null);
  readonly pitch = signal<Pitch | null>(null);
  readonly history = signal<Pitch[]>([]);
  readonly busy = signal(false);
  readonly phone = computed(() => this.lead()?.contacts.find(c => c.type === 'WhatsApp')?.value ?? this.lead()?.phone);
  type: PitchType = 'IntroEmail';
  language: PitchLanguage = 'English';
  extra = '';
  subject = '';
  body = '';
  selected = new Set<string>();

  async ngOnInit() {
    try {
      const l = (await this.api.getCached<LeadDetail>(`/leads/${this.id()}`)).data;
      this.lead.set(l);
      l.opportunities.slice(0, 2).forEach(o => this.selected.add(o.id));
      this.history.set((await this.api.get<Paged<Pitch>>('/pitches', { leadId: this.id(), pageSize: 20 })).items);
    } catch (e) { this.toast.error(e); }
  }

  toggle(id: string, on: boolean) { on ? this.selected.add(id) : this.selected.delete(id); }
  isEmail(t: PitchType) { return TYPES.find(x => x.value === t)?.email ?? false; }
  typeLabel(t: PitchType) { return TYPES.find(x => x.value === t)?.label ?? t; }

  open(p: Pitch) { this.pitch.set(p); this.subject = p.subject ?? ''; this.body = p.body; window.scrollTo({ top: 0 }); }

  async generate() {
    this.busy.set(true);
    try {
      const p = await this.api.post<Pitch>('/pitches/generate', { leadId: this.id(), type: this.type, language: this.language, opportunityIds: [...this.selected], extraInstructions: this.extra || undefined });
      this.open(p);
      this.history.set([p, ...this.history()]);
    } catch (e) { this.toast.error(e); }
    finally { this.busy.set(false); }
  }

  async save(): Promise<Pitch | null> {
    const p = this.pitch();
    if (!p) return null;
    const r = await this.api.mutateOrQueue<Pitch>('PUT', `/pitches/${p.id}`, { subject: this.subject || null, body: this.body }, 'Pitch edit').catch(e => { this.toast.error(e); return null; });
    if (!r) return null;
    if (r.queued) { this.toast.ok('Saved offline — will sync.'); return null; }
    this.pitch.set(r.result!);
    this.toast.ok('Saved.');
    return r.result!;
  }

  async toApproval() {
    const p = this.pitch();
    if (!p) return;
    this.busy.set(true);
    try {
      await this.api.put(`/pitches/${p.id}`, { subject: this.subject || null, body: this.body });
      const draft = await this.api.post<EmailDraft>('/emails', { leadId: this.id(), pitchDraftId: p.id });
      await this.router.navigate(['/approvals', draft.id]);
    } catch (e) { this.toast.error(e); }
    finally { this.busy.set(false); }
  }

  async copy() { await navigator.clipboard.writeText(this.body); this.toast.ok('Copied.'); }

  openWhatsApp() {
    const digits = (this.phone() ?? '').replace(/\D/g, '');
    const number = digits.length === 10 ? '91' + digits : digits;
    window.open(`https://wa.me/${number}?text=${encodeURIComponent(this.body)}`, '_blank');
  }
}
