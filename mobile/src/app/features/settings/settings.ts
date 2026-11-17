import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ApiService } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { AllSettings } from '../../core/models';
import { PlatformService } from '../../core/platform.service';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-settings',
  imports: [FormsModule, MatExpansionModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Settings</h1>
    <lp-state [loading]="!s()" />
    @if (s(); as s) {
      <div class="card pause row between">
        <div><b>Pause all sending</b><div class="small muted">Global kill switch for every outgoing email.</div></div>
        <mat-slide-toggle [checked]="s.outreach.pauseAllSending" (change)="pause($event.checked)" />
      </div>

      <mat-accordion multi>
        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>badge</mat-icon>&nbsp;My profile & services</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <mat-form-field><mat-label>Your name</mat-label><input matInput [(ngModel)]="s.owner.name" /></mat-form-field>
            <mat-form-field><mat-label>Business name</mat-label><input matInput [(ngModel)]="s.owner.businessName" /></mat-form-field>
            <mat-form-field><mat-label>Email</mat-label><input matInput [(ngModel)]="s.owner.email" /></mat-form-field>
            <mat-form-field><mat-label>Phone</mat-label><input matInput [(ngModel)]="s.owner.phone" /></mat-form-field>
            <mat-form-field><mat-label>Portfolio URL</mat-label><input matInput [(ngModel)]="s.owner.portfolioUrl" /></mat-form-field>
            <mat-form-field><mat-label>Email signature</mat-label><textarea matInput rows="3" [(ngModel)]="s.owner.signature"></textarea></mat-form-field>
            <div class="grid2">
              <mat-form-field><mat-label>Target min (₹)</mat-label><input matInput type="number" [(ngModel)]="s.owner.targetProjectMin" /></mat-form-field>
              <mat-form-field><mat-label>Target max (₹)</mat-label><input matInput type="number" [(ngModel)]="s.owner.targetProjectMax" /></mat-form-field>
            </div>
            <mat-form-field><mat-label>Services you offer (one per line)</mat-label><textarea matInput rows="8" [ngModel]="s.owner.services.join('\\n')" (ngModelChange)="s.owner.services = splitLines($event)"></textarea></mat-form-field>
            <button mat-flat-button (click)="save('owner', s.owner)">Save profile</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>outgoing_mail</mat-icon>&nbsp;Outreach & follow-ups</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <div class="grid2">
              <mat-form-field><mat-label>Daily send limit</mat-label><input matInput type="number" [(ngModel)]="s.outreach.dailySendLimit" /></mat-form-field>
              <mat-form-field><mat-label>Hourly send limit</mat-label><input matInput type="number" [(ngModel)]="s.outreach.hourlySendLimit" /></mat-form-field>
            </div>
            <mat-form-field><mat-label>Approval valid for (hours)</mat-label><input matInput type="number" [(ngModel)]="s.outreach.approvalValidityHours" /></mat-form-field>
            <mat-form-field><mat-label>Follow-up days (comma separated)</mat-label><input matInput [ngModel]="s.outreach.followUpDays.join(', ')" (ngModelChange)="s.outreach.followUpDays = splitNums($event)" /></mat-form-field>
            <mat-slide-toggle [(ngModel)]="s.outreach.autoCreateFollowUps">Schedule follow-ups after sending</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.outreach.autoDraftFollowUps">Prepare follow-up drafts for approval</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.outreach.requireSourceRecordedRecipient">Recipient must be a source-recorded contact</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.outreach.requireEmailConsent">Require recorded consent before emailing</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.outreach.appendOptOutFooter">Add opt-out line to emails</mat-slide-toggle>
            <mat-form-field><mat-label>Opt-out line</mat-label><textarea matInput rows="2" [(ngModel)]="s.outreach.optOutFooter"></textarea></mat-form-field>
            <button mat-flat-button (click)="save('outreach', s.outreach)">Save outreach</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>calculate</mat-icon>&nbsp;Lead scoring weights</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <div class="grid2">
              <mat-form-field><mat-label>Website gaps</mat-label><input matInput type="number" [(ngModel)]="s.scoring.websiteGaps" /></mat-form-field>
              <mat-form-field><mat-label>Service fit</mat-label><input matInput type="number" [(ngModel)]="s.scoring.serviceFit" /></mat-form-field>
              <mat-form-field><mat-label>Contact availability</mat-label><input matInput type="number" [(ngModel)]="s.scoring.contactAvailability" /></mat-form-field>
              <mat-form-field><mat-label>Identified opportunity</mat-label><input matInput type="number" [(ngModel)]="s.scoring.identifiedOpportunity" /></mat-form-field>
              <mat-form-field><mat-label>Business relevance</mat-label><input matInput type="number" [(ngModel)]="s.scoring.businessRelevance" /></mat-form-field>
              <mat-form-field><mat-label>Budget suitability</mat-label><input matInput type="number" [(ngModel)]="s.scoring.budgetSuitability" /></mat-form-field>
            </div>
            <div class="small muted">Total {{ s.scoring.websiteGaps + s.scoring.serviceFit + s.scoring.contactAvailability + s.scoring.identifiedOpportunity + s.scoring.businessRelevance + s.scoring.budgetSuitability }} (normalised to 100)</div>
            <div class="grid3">
              <mat-form-field><mat-label>High ≥</mat-label><input matInput type="number" [(ngModel)]="s.scoring.highPriorityMin" /></mat-form-field>
              <mat-form-field><mat-label>Good ≥</mat-label><input matInput type="number" [(ngModel)]="s.scoring.goodPotentialMin" /></mat-form-field>
              <mat-form-field><mat-label>Review ≥</mat-label><input matInput type="number" [(ngModel)]="s.scoring.needsReviewMin" /></mat-form-field>
            </div>
            <button mat-flat-button (click)="save('scoring', s.scoring, 'Saved. All leads will be re-scored in the background.')">Save & re-score</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>payments</mat-icon>&nbsp;Budgets & cost control</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <div class="grid2">
              <mat-form-field><mat-label>AI monthly ($)</mat-label><input matInput type="number" [(ngModel)]="s.budget.monthlyAIBudgetUsd" /></mat-form-field>
              <mat-form-field><mat-label>AI daily ($)</mat-label><input matInput type="number" [(ngModel)]="s.budget.dailyAIBudgetUsd" /></mat-form-field>
              <mat-form-field><mat-label>Discovery monthly ($)</mat-label><input matInput type="number" [(ngModel)]="s.budget.monthlyDiscoveryBudgetUsd" /></mat-form-field>
              <mat-form-field><mat-label>Confirm jobs above ($)</mat-label><input matInput type="number" [(ngModel)]="s.budget.confirmDiscoveryAboveUsd" /></mat-form-field>
              <mat-form-field><mat-label>Warn at (%)</mat-label><input matInput type="number" [(ngModel)]="s.budget.warnAtPercent" /></mat-form-field>
              <mat-form-field><mat-label>Hosting / month ($)</mat-label><input matInput type="number" [(ngModel)]="s.budget.monthlyHostingCostUsd" /></mat-form-field>
            </div>
            <mat-slide-toggle [(ngModel)]="s.budget.disableAIWhenBudgetReached">Pause AI when budget is reached</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.budget.blockDiscoveryWhenBudgetReached">Block discovery over budget</mat-slide-toggle>
            <button mat-flat-button (click)="save('budget', s.budget)">Save budgets</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>bolt</mat-icon>&nbsp;Automation</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <mat-slide-toggle [(ngModel)]="s.automation.autoWebsiteAnalysisAfterDiscovery">Analyse websites after every discovery</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.automation.autoAIAnalysisAfterWebsiteAnalysis">Run AI after website analysis</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.automation.usePageSpeed">Use PageSpeed Insights</mat-slide-toggle>
            <p class="small muted">Defaults keep costs low: discovery imports first, you choose which leads to analyse.</p>
            <button mat-flat-button (click)="save('automation', s.automation)">Save automation</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>notifications</mat-icon>&nbsp;Notifications</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <div class="small">Push on this device: <b>{{ platform.pushStatus() }}</b></div>
            <mat-slide-toggle [checked]="platform.pushStatus() === 'registered'" (change)="platform.setPushOptOut(!$event.checked)">Push notifications on this phone</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.pushEnabled">Server sends push notifications</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.includeDetailsInPush">Show lead names in push previews</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.discoveryCompleted">Discovery completed</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.highPriorityLeads">High-priority leads</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.analysisCompleted">Analysis completed</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.approvalPending">Drafts awaiting approval</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.emailFailed">Email failures</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.followUpDue">Follow-ups due</mat-slide-toggle>
            <mat-slide-toggle [(ngModel)]="s.notifications.jobErrors">Job errors & budget warnings</mat-slide-toggle>
            <button mat-flat-button (click)="save('notifications', s.notifications)">Save notifications</button>
          </div>
        </mat-expansion-panel>

        <mat-expansion-panel>
          <mat-expansion-panel-header><mat-panel-title><mat-icon>lock</mat-icon>&nbsp;Security & server</mat-panel-title></mat-expansion-panel-header>
          <div class="stack">
            <mat-form-field><mat-label>Server URL</mat-label><input matInput [value]="config.apiBaseUrl()" #url /></mat-form-field>
            <button mat-stroked-button (click)="saveUrl(url.value)">Save server URL</button>
            <mat-form-field><mat-label>Current password</mat-label><input matInput type="password" [(ngModel)]="pw.current" autocomplete="current-password" /></mat-form-field>
            <mat-form-field><mat-label>New password</mat-label><input matInput type="password" [(ngModel)]="pw.next" autocomplete="new-password" /><mat-hint>10+ chars, upper, lower, digit</mat-hint></mat-form-field>
            <button mat-flat-button (click)="changePassword()" [disabled]="!pw.current || !pw.next">Change password</button>
            <p class="small muted">Changing your password signs out other devices.</p>
          </div>
        </mat-expansion-panel>
      </mat-accordion>
    }
  `,
  styles: `.pause{margin-bottom:12px} mat-expansion-panel{border-radius:16px !important;margin-bottom:8px}`,
})
export class SettingsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly config = inject(ConfigService);
  readonly platform = inject(PlatformService);
  readonly s = signal<AllSettings | null>(null);
  pw = { current: '', next: '' };

  async ngOnInit() { try { this.s.set(await this.api.get<AllSettings>('/settings')); } catch (e) { this.toast.error(e); } }

  splitLines(v: string) { return v.split('\n').map(x => x.trim()).filter(Boolean); }
  splitNums(v: string) { return v.split(',').map(x => parseInt(x.trim(), 10)).filter(n => !isNaN(n)); }

  async save(section: string, value: unknown, message = 'Saved.') {
    try { await this.api.put(`/settings/${section}`, value); this.toast.ok(message); } catch (e) { this.toast.error(e); }
  }

  async pause(paused: boolean) {
    try {
      await this.api.post(`/settings/outreach/pause?paused=${paused}`);
      this.s.update(s => s && { ...s, outreach: { ...s.outreach, pauseAllSending: paused } });
      this.toast.ok(paused ? 'All sending paused.' : 'Sending resumed.');
    } catch (e) { this.toast.error(e); }
  }

  async saveUrl(url: string) { try { await this.config.setBaseUrl(url); this.toast.ok('Server URL saved.'); } catch (e) { this.toast.error(e); } }

  async changePassword() {
    try { await this.api.post('/auth/change-password', { currentPassword: this.pw.current, newPassword: this.pw.next }); this.pw = { current: '', next: '' }; this.toast.ok('Password changed.'); }
    catch (e) { this.toast.error(e); }
  }
}
