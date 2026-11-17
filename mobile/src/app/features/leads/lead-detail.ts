import { Component, OnDestroy, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Activity, ActivityType, ContactType, LeadDetail, PIPELINE_STAGES, Paged, PipelineStage } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-lead-detail',
  imports: [FormsModule, RouterLink, MatTabsModule, MatButtonModule, MatIconModule, MatMenuModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatExpansionModule, MatProgressBarModule, ...SHARED],
  template: `
    <lp-offline-banner [show]="fromCache()" [cachedAt]="cachedAt()" />
    <lp-state [loading]="!lead() && !error()" [error]="error()" />
    @if (lead(); as l) {
      <div class="head">
        <lp-score [score]="l.score" [priority]="l.priority" />
        <div class="grow">
          <h1 class="name">{{ l.businessName }}</h1>
          <div class="small muted">{{ l.categoryName || 'Uncategorised' }} · {{ l.city || '—' }}{{ l.state ? ', ' + l.state : '' }}</div>
        </div>
        <button mat-icon-button [matMenuTriggerFor]="menu" aria-label="Lead actions"><mat-icon>more_vert</mat-icon></button>
        <mat-menu #menu="matMenu">
          <button mat-menu-item (click)="rescore()"><mat-icon>calculate</mat-icon>Recalculate score</button>
          <button mat-menu-item (click)="remove()"><mat-icon>delete</mat-icon>Delete lead</button>
          <button mat-menu-item (click)="erase()"><mat-icon>person_remove</mat-icon>Erase & opt out</button>
        </mat-menu>
      </div>

      <div class="quick">
        @if (l.phone) { <a mat-stroked-button [href]="'tel:' + l.phone"><mat-icon>call</mat-icon>Call</a> }
        @if (l.websiteUrl) { <a mat-stroked-button [href]="l.websiteUrl" target="_blank" rel="noopener"><mat-icon>language</mat-icon>Website</a> }
        <a mat-flat-button [routerLink]="['/leads', l.id, 'pitch']"><mat-icon>auto_awesome</mat-icon>Pitch</a>
        <button mat-stroked-button (click)="analyse()" [disabled]="analysing()"><mat-icon>troubleshoot</mat-icon>Analyse</button>
      </div>

      <div class="row wrap stage">
        <mat-form-field class="grow"><mat-label>Stage</mat-label>
          <mat-select [ngModel]="l.stage" (ngModelChange)="changeStage($event)">
            @for (s of stages; track s) { <mat-option [value]="s">{{ s | stage }}</mat-option> }
          </mat-select></mat-form-field>
        @if (l.nextFollowUpAt) { <span class="chip warn"><mat-icon>event</mat-icon>Follow-up {{ l.nextFollowUpAt | date: 'd MMM' }}</span> }
      </div>

      <mat-tab-group mat-stretch-tabs="false" mat-align-tabs="start" animationDuration="150ms">
        <mat-tab label="Overview">
          <div class="stack">
            <div class="card stack">
              <h2 class="section nm">Contacts</h2>
              @for (c of l.contacts; track c.id) {
                <div class="contact">
                  <mat-icon>{{ contactIcon(c.type) }}</mat-icon>
                  <div class="grow">
                    <div class="ellipsis">{{ c.value }} @if (c.suppressed) {<span class="chip bad">Opted out</span>}</div>
                    <div class="row wrap verif">
                      <span class="chip" [class.ok]="c.sourceVerified" title="Where it was found">Source {{ c.sourceVerified ? '✓' : '?' }}</span>
                      <span class="chip" [class.ok]="c.syntaxVerified">Format {{ c.syntaxVerified ? '✓' : '✗' }}</span>
                      <span class="chip" [class.ok]="c.deliveryVerified">Delivery {{ c.deliveryVerified ? '✓' : 'unverified' }}</span>
                      <span class="chip" [class.ok]="c.consentVerified">Consent {{ c.consentVerified ? '✓' : 'none recorded' }}</span>
                    </div>
                    <div class="small muted ellipsis">From {{ c.sourceProvider }}{{ c.sourceUrl ? ' — ' + c.sourceUrl : '' }}</div>
                  </div>
                  <button mat-icon-button (click)="removeContact(c.id)" aria-label="Remove contact"><mat-icon>close</mat-icon></button>
                </div>
              } @empty { <p class="muted small">No contacts recorded.</p> }
              <mat-expansion-panel class="flat">
                <mat-expansion-panel-header><mat-panel-title>Add contact</mat-panel-title></mat-expansion-panel-header>
                <div class="stack">
                  <mat-form-field><mat-label>Type</mat-label><mat-select [(ngModel)]="newContact.type">
                    <mat-option value="Email">Email</mat-option><mat-option value="Phone">Phone</mat-option><mat-option value="WhatsApp">WhatsApp</mat-option><mat-option value="LinkedIn">LinkedIn</mat-option>
                  </mat-select></mat-form-field>
                  <mat-form-field><mat-label>Value</mat-label><input matInput [(ngModel)]="newContact.value" /></mat-form-field>
                  <mat-form-field><mat-label>Where was it found? *</mat-label><input matInput [(ngModel)]="newContact.sourceUrl" placeholder="URL or description" /></mat-form-field>
                  <button mat-flat-button (click)="addContact()">Add</button>
                </div>
              </mat-expansion-panel>
            </div>

            <div class="card stack">
              <h2 class="section nm">Business</h2>
              <div class="kv"><span>Address</span><span>{{ l.addressLine || '—' }}</span></div>
              <div class="kv"><span>PIN</span><span>{{ l.pinCode || '—' }}</span></div>
              @if (l.rating) { <div class="kv"><span>Rating</span><span>{{ l.rating }} ★ ({{ l.reviewCount }} reviews)</span></div> }
              <div class="kv"><span>Expected value</span><span>{{ l.expectedDealValue | inr }}</span></div>
              <div class="kv"><span>Next action</span><span>{{ l.nextAction || '—' }}</span></div>
              <div class="kv"><span>Discovered</span><span>{{ l.discoveredAt | date: 'mediumDate' }} via {{ l.sourceProvider }}</span></div>
              @for (s of l.sources; track $index) { <div class="small muted">{{ s.attribution || s.provider }} @if (s.sourceUrl) { — <a [href]="s.sourceUrl" target="_blank" rel="noopener">source</a> }</div> }
            </div>
          </div>
        </mat-tab>

        <mat-tab label="Analysis">
          <div class="stack">
            @if (analysing() || l.websiteAnalysisStatus === 'Running' || l.aiAnalysisStatus === 'Running' || l.websiteAnalysisStatus === 'Queued' || l.aiAnalysisStatus === 'Queued') {
              <div class="card"><div class="small">Analysis in progress — runs on the server.</div><mat-progress-bar mode="indeterminate" /></div>
            }
            <div class="card stack">
              <h2 class="section nm">Opportunities</h2>
              @for (o of l.opportunities; track o.id) {
                <div class="opp">
                  <div class="row between"><b>{{ o.title }}</b><span class="chip" [class.ok]="o.confidence === 'High'" [class.info]="o.confidence === 'Medium'">{{ o.confidence }}</span></div>
                  <div class="small">{{ o.problem }}</div>
                  <div class="small"><b>Evidence:</b> {{ o.evidence }}</div>
                  @if (o.assumptions) { <div class="small muted"><b>Caveat:</b> {{ o.assumptions }}</div> }
                  <div class="row between"><span class="chip info">{{ o.recommendedService }}</span>
                    <span class="small muted">{{ o.source === 'AI' ? 'AI' : 'Rule' }} <button mat-button (click)="dismissOpp(o.id)">Dismiss</button></span></div>
                </div>
              } @empty { <p class="muted small">No opportunities yet. Run analysis to find evidence-based gaps.</p> }
            </div>

            @if (l.latestAIAnalysis; as a) {
              <div class="card stack">
                <div class="row between"><h2 class="section nm">AI insight</h2>@if (a.isMock) {<span class="chip warn">Mock AI</span>}</div>
                @if (a.status === 'Failed') { <div class="notice bad">{{ a.error }}</div> }
                <p>{{ a.businessSummary }}</p>
                @if (a.suggestedApproach) { <p class="small"><b>Approach:</b> {{ a.suggestedApproach }}</p> }
                <div class="row wrap">@for (s of a.recommendedServices; track s) { <span class="chip info">{{ s }}</span> }</div>
                @if (a.estimatedValueMin) { <p class="small">Internal estimate {{ a.estimatedValueMin | inr }}–{{ a.estimatedValueMax | inr }} <span class="muted">(not a verified budget)</span></p> }
                @if (a.unknowns.length) { <p class="small muted">Unknown: {{ a.unknowns.join(', ') }}</p> }
                <p class="small muted">Confidence {{ a.confidenceScore }}/100 · {{ a.model }} · {{ a.promptVersion }} · {{ '$' + (a.costUsd | number: '1.4-4') }}</p>
              </div>
            }

            @if (l.latestWebsiteAnalysis; as w) {
              <div class="card stack">
                <h2 class="section nm">Website checks</h2>
                <div class="small muted">{{ w.finalUrl || w.requestedUrl }} · {{ w.analysedAt | date: 'short' }} · {{ w.pagesInspected.length }} page(s)</div>
                @if (w.pageSpeedPerformance != null) {
                  <div class="grid3">
                    <div class="stat"><span class="value">{{ w.pageSpeedPerformance }}</span><span class="label">Performance</span></div>
                    <div class="stat"><span class="value">{{ w.pageSpeedAccessibility }}</span><span class="label">Accessibility</span></div>
                    <div class="stat"><span class="value">{{ w.pageSpeedSeo }}</span><span class="label">SEO</span></div>
                  </div>
                }
                @for (e of w.evidence; track $index) {
                  <div class="ev"><mat-icon [class]="e.result">{{ evIcon(e.result) }}</mat-icon><div><b class="small">{{ e.check | humanize }}</b><div class="small">{{ e.observation }}</div></div></div>
                }
              </div>
            }
          </div>
        </mat-tab>

        <mat-tab label="Score">
          @if (l.latestScore; as s) {
            <div class="card stack">
              <div class="row between"><h2 class="section nm">{{ s.total }}/100 · {{ s.priority | humanize }}</h2><span class="small muted">{{ s.calculatedAt | date: 'short' }}</span></div>
              @for (c of s.components; track c.criterion) {
                <div class="comp">
                  <div class="row between"><span>{{ c.criterion }}</span><b>{{ c.points }}/{{ c.weight }}</b></div>
                  <div class="bar"><span [style.width.%]="c.fraction * 100"></span></div>
                  @for (r of c.reasons; track $index) { <div class="small muted">• {{ r }}</div> }
                </div>
              }
              <p class="small muted">Scores are calculated deterministically from evidence. Weights are configurable in Settings.</p>
            </div>
          } @else { <lp-state [empty]="true" icon="calculate" emptyText="Not scored yet." /> }
        </mat-tab>

        <mat-tab label="Activity">
          <div class="card stack">
            <mat-form-field><mat-label>Add a note / log a call</mat-label><textarea matInput rows="2" [(ngModel)]="note"></textarea></mat-form-field>
            <div class="actions">
              <button mat-flat-button (click)="addActivity('Note')" [disabled]="!note.trim()">Save note</button>
              <button mat-stroked-button (click)="addActivity('Call')" [disabled]="!note.trim()"><mat-icon>call</mat-icon>Log call</button>
              <button mat-stroked-button (click)="addActivity('EmailReceived')" [disabled]="!note.trim()"><mat-icon>reply</mat-icon>Record reply</button>
            </div>
            <div class="row">
              <mat-form-field class="grow"><mat-label>Schedule follow-up</mat-label><input matInput type="datetime-local" [(ngModel)]="followUpAt" /></mat-form-field>
              <button mat-stroked-button (click)="scheduleFollowUp()" [disabled]="!followUpAt">Add</button>
            </div>
            <mat-expansion-panel class="flat">
              <mat-expansion-panel-header><mat-panel-title><mat-icon>groups</mat-icon>&nbsp;Schedule meeting</mat-panel-title></mat-expansion-panel-header>
              <div class="stack">
                <mat-form-field><mat-label>When</mat-label><input matInput type="datetime-local" [(ngModel)]="meeting.at" /></mat-form-field>
                <mat-form-field><mat-label>Where / link</mat-label><input matInput [(ngModel)]="meeting.location" placeholder="Google Meet link or address" /></mat-form-field>
                <mat-form-field><mat-label>Agenda</mat-label><input matInput [(ngModel)]="meeting.agenda" /></mat-form-field>
                <button mat-flat-button (click)="addMeeting()" [disabled]="!meeting.at">Save meeting</button>
              </div>
            </mat-expansion-panel>
            <mat-expansion-panel class="flat">
              <mat-expansion-panel-header><mat-panel-title><mat-icon>request_quote</mat-icon>&nbsp;Record proposal / deal</mat-panel-title></mat-expansion-panel-header>
              <div class="stack">
                <mat-form-field><mat-label>Title</mat-label><input matInput [(ngModel)]="proposal.title" placeholder="Enquiry management system" /></mat-form-field>
                <mat-form-field><mat-label>Amount (₹)</mat-label><input matInput type="number" [(ngModel)]="proposal.amount" /></mat-form-field>
                <div class="actions">
                  <button mat-flat-button (click)="addProposal('Sent')" [disabled]="!proposal.title || !proposal.amount">Proposal sent</button>
                  <button mat-stroked-button (click)="addProposal('Draft')" [disabled]="!proposal.title">Save draft</button>
                  <button mat-stroked-button (click)="markWon()" [disabled]="!proposal.amount"><mat-icon>emoji_events</mat-icon>Mark won</button>
                </div>
              </div>
            </mat-expansion-panel>
          </div>
          <div class="timeline">
            @for (a of activities(); track a.id) {
              <div class="tl"><mat-icon>{{ actIcon(a.type) }}</mat-icon><div class="grow"><b class="small">{{ a.title }}</b>
                @if (a.description) {<div class="small pre">{{ a.description }}</div>}<div class="small muted">{{ a.occurredAt | date: 'medium' }}</div></div></div>
            } @empty { <p class="muted small">No activity yet.</p> }
          </div>
        </mat-tab>
      </mat-tab-group>
    }
  `,
  styles: `
    .head{display:flex;gap:12px;align-items:center;margin-bottom:12px} .name{font-size:20px;font-weight:700;margin:0;line-height:1.2}
    .quick{display:flex;gap:8px;overflow-x:auto;padding-bottom:4px;margin-bottom:8px} .quick > *{flex:none}
    .stage{margin-top:4px} .nm{margin:0}
    .contact{display:flex;gap:10px;align-items:flex-start;padding:8px 0;border-bottom:1px solid var(--lp-border)} .verif{gap:4px;margin:4px 0}
    .kv{display:flex;justify-content:space-between;gap:12px;font-size:14px} .kv span:first-child{color:var(--lp-muted)} .kv span:last-child{text-align:right}
    .opp{display:flex;flex-direction:column;gap:6px;padding:10px 0;border-bottom:1px solid var(--lp-border)}
    .ev{display:flex;gap:10px;align-items:flex-start;padding:6px 0} .Pass{color:var(--lp-ok)} .Fail{color:var(--lp-bad)} .Warning{color:var(--lp-warn)} .Unknown,.NotApplicable{color:var(--lp-muted)}
    .comp{display:flex;flex-direction:column;gap:4px} .bar{height:8px;border-radius:4px;background:var(--lp-chip);overflow:hidden} .bar span{display:block;height:100%;background:var(--mat-sys-primary)}
    .timeline{display:flex;flex-direction:column;gap:12px;margin-top:12px} .tl{display:flex;gap:10px} .tl mat-icon{color:var(--mat-sys-primary)}
    .flat{box-shadow:none !important;border:1px solid var(--lp-border)}
  `,
})
export class LeadDetailPage implements OnInit, OnDestroy {
  readonly id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  private router = inject(Router);
  readonly stages = PIPELINE_STAGES;
  readonly lead = signal<LeadDetail | null>(null);
  readonly activities = signal<Activity[]>([]);
  readonly error = signal<string | null>(null);
  readonly analysing = signal(false);
  readonly fromCache = signal(false);
  readonly cachedAt = signal<string | undefined>(undefined);
  note = '';
  followUpAt = '';
  newContact: { type: ContactType; value: string; sourceUrl: string } = { type: 'Email', value: '', sourceUrl: '' };
  meeting = { at: '', location: '', agenda: '' };
  proposal: { title: string; amount: number | null } = { title: '', amount: null };
  private poll?: ReturnType<typeof setInterval>;

  async ngOnInit() { await this.load(); }
  ngOnDestroy() { clearInterval(this.poll); }

  async load() {
    try {
      const r = await this.api.getCached<LeadDetail>(`/leads/${this.id()}`);
      this.lead.set(r.data);
      this.fromCache.set(r.fromCache);
      this.cachedAt.set(r.cachedAt);
      const acts = await this.api.getCached<Paged<Activity>>(`/leads/${this.id()}/activities`, { pageSize: 50 });
      this.activities.set(acts.data.items);
      const busy = ['Queued', 'Running'];
      if (!busy.includes(r.data.websiteAnalysisStatus) && !busy.includes(r.data.aiAnalysisStatus)) { this.analysing.set(false); clearInterval(this.poll); this.poll = undefined; }
    } catch { this.error.set('Could not load this lead.'); }
  }

  async analyse() {
    try {
      const r = await this.api.post<{ message?: string }>(`/leads/${this.id()}/analyse`, { website: true, ai: true, usePageSpeed: true });
      this.analysing.set(true);
      this.toast.ok(r.message ?? 'Analysis started on the server.');
      clearInterval(this.poll);
      this.poll = setInterval(() => void this.load(), 4000);
    } catch (e) { this.toast.error(e); }
  }

  async rescore() { try { await this.api.post(`/leads/${this.id()}/score`); await this.load(); } catch (e) { this.toast.error(e); } }

  async changeStage(stage: PipelineStage) {
    let lostReason: string | undefined;
    if (stage === 'Lost' || stage === 'NotInterested') lostReason = prompt('Reason (optional)') ?? undefined;
    const r = await this.api.mutateOrQueue('POST', `/leads/${this.id()}/stage`, { stage, lostReason }, `Stage → ${stage}`).catch(e => { this.toast.error(e); return null; });
    if (r?.queued) this.toast.ok('Offline — stage change will sync when online.');
    else if (r) await this.load();
  }

  async addActivity(type: ActivityType) {
    const titles: Record<string, string> = { Note: 'Note', Call: 'Call logged', EmailReceived: 'Reply received' };
    const r = await this.api.mutateOrQueue('POST', '/crm/activities', { leadId: this.id(), type, title: titles[type] ?? type, description: this.note }, titles[type] ?? type)
      .catch(e => { this.toast.error(e); return null; });
    if (!r) return;
    this.note = '';
    if (r.queued) this.toast.ok('Saved offline — will sync.'); else await this.load();
  }

  async scheduleFollowUp() {
    const due = new Date(this.followUpAt).toISOString();
    const r = await this.api.mutateOrQueue('POST', '/followups', { leadId: this.id(), dueAt: due, channel: 'Email' }, 'Follow-up').catch(e => { this.toast.error(e); return null; });
    if (!r) return;
    this.followUpAt = '';
    this.toast.ok(r.queued ? 'Saved offline — will sync.' : 'Follow-up scheduled.');
    if (!r.queued) await this.load();
  }

  async addMeeting() {
    try {
      await this.api.post('/crm/meetings', { leadId: this.id(), scheduledAt: new Date(this.meeting.at).toISOString(), durationMinutes: 30,
        location: this.meeting.location || null, agenda: this.meeting.agenda || null, status: 'Scheduled' });
      this.meeting = { at: '', location: '', agenda: '' };
      this.toast.ok('Meeting scheduled.');
      await this.load();
    } catch (e) { this.toast.error(e); }
  }

  async addProposal(status: 'Draft' | 'Sent') {
    try {
      await this.api.post('/crm/proposals', { leadId: this.id(), title: this.proposal.title, amount: this.proposal.amount ?? 0, status });
      this.toast.ok(status === 'Sent' ? 'Proposal recorded as sent.' : 'Proposal saved.');
      await this.load();
    } catch (e) { this.toast.error(e); }
  }

  async markWon() {
    if (!confirm(`Mark this lead as Won for ₹${this.proposal.amount}?`)) return;
    try {
      await this.api.post(`/leads/${this.id()}/stage`, { stage: 'Won', actualValue: this.proposal.amount, note: this.proposal.title || 'Deal won' });
      this.toast.ok('Congratulations! Marked as won.');
      await this.load();
    } catch (e) { this.toast.error(e); }
  }

  async addContact() {
    if (!this.newContact.value.trim() || !this.newContact.sourceUrl.trim()) { this.toast.error(new Error('Enter the value and where it was found.')); return; }
    try { await this.api.post(`/leads/${this.id()}/contacts`, { ...this.newContact, isPrimary: false }); this.newContact = { type: 'Email', value: '', sourceUrl: '' }; await this.load(); }
    catch (e) { this.toast.error(e); }
  }

  async removeContact(id: string) {
    if (!confirm('Remove this contact?')) return;
    try { await this.api.delete(`/leads/${this.id()}/contacts/${id}`); await this.load(); } catch (e) { this.toast.error(e); }
  }

  async dismissOpp(id: string) { try { await this.api.post(`/leads/${this.id()}/opportunities/${id}/dismiss`); await this.load(); } catch (e) { this.toast.error(e); } }

  async remove() {
    if (!confirm('Delete this lead?')) return;
    try { await this.api.delete(`/leads/${this.id()}`); await this.router.navigateByUrl('/leads'); } catch (e) { this.toast.error(e); }
  }

  async erase() {
    if (!confirm('Permanently erase all data for this prospect and add their contacts to the opt-out list? This cannot be undone.')) return;
    try { await this.api.delete(`/leads/${this.id()}/erase?suppress=true`); this.toast.ok('Erased.'); await this.router.navigateByUrl('/leads'); } catch (e) { this.toast.error(e); }
  }

  contactIcon(t: ContactType) { return ({ Email: 'mail', Phone: 'call', WhatsApp: 'chat', Website: 'language', LinkedIn: 'work', Other: 'contact_page' } as Record<string, string>)[t]; }
  evIcon(r: string) { return ({ Pass: 'check_circle', Fail: 'cancel', Warning: 'warning', Unknown: 'help', NotApplicable: 'info' } as Record<string, string>)[r]; }
  actIcon(t: ActivityType) {
    return ({ Note: 'sticky_note_2', Call: 'call', EmailSent: 'outgoing_mail', EmailReceived: 'reply', StageChange: 'swap_horiz', Meeting: 'groups', Proposal: 'request_quote',
      FollowUp: 'event', Analysis: 'troubleshoot', System: 'info', WhatsApp: 'chat', LinkedIn: 'work' } as Record<string, string>)[t] ?? 'circle';
  }
}
