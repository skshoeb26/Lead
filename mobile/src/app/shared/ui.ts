import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, Injectable, Pipe, PipeTransform, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { errorMessage } from '../core/http';
import { LeadPriority, PipelineStage } from '../core/models';

@Injectable({ providedIn: 'root' })
export class Toast {
  constructor(private snack: MatSnackBar) {}
  ok(message: string) { this.snack.open(message, 'OK', { duration: 3000 }); }
  error(err: unknown) { this.snack.open(errorMessage(err), 'Dismiss', { duration: 6000, panelClass: 'snack-error' }); }
}

const STAGE_LABELS: Record<PipelineStage, string> = {
  New: 'New', Analysed: 'Analysed', Qualified: 'Qualified', PitchReady: 'Pitch ready', Contacted: 'Contacted', Replied: 'Replied',
  Interested: 'Interested', MeetingScheduled: 'Meeting', ProposalSent: 'Proposal sent', Negotiation: 'Negotiation', Won: 'Won',
  Lost: 'Lost', NotInterested: 'Not interested', FollowUpRequired: 'Follow-up',
};

@Pipe({ name: 'stage' })
export class StagePipe implements PipeTransform {
  transform(v: PipelineStage | null | undefined): string { return v ? STAGE_LABELS[v] ?? v : ''; }
}

@Pipe({ name: 'humanize' })
export class HumanizePipe implements PipeTransform {
  transform(v: string | null | undefined): string {
    return (v ?? '').replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase());
  }
}

@Pipe({ name: 'inr' })
export class InrPipe implements PipeTransform {
  transform(v: number | null | undefined): string {
    return v == null ? '—' : '₹' + Math.round(v).toLocaleString('en-IN');
  }
}

/** Circular score badge coloured by priority band. */
@Component({
  selector: 'lp-score',
  template: `<span class="score" [class]="'p-' + (priority() ?? 'none')" [attr.aria-label]="'Score ' + (score() ?? 'not scored')">{{ score() ?? '–' }}</span>`,
  styles: `.score{display:inline-grid;place-items:center;min-width:40px;height:40px;border-radius:50%;font-weight:700;font-size:15px;
    background:var(--lp-chip);color:var(--lp-muted)}
    .p-High{background:#dcfce7;color:#166534}.p-Good{background:#dbeafe;color:#1e40af}.p-NeedsReview{background:#fef3c7;color:#92400e}.p-Low{background:#fee2e2;color:#991b1b}
    :host-context([data-theme=dark]) .p-High{background:#14532d;color:#bbf7d0} :host-context([data-theme=dark]) .p-Good{background:#1e3a8a;color:#bfdbfe}
    :host-context([data-theme=dark]) .p-NeedsReview{background:#78350f;color:#fde68a} :host-context([data-theme=dark]) .p-Low{background:#7f1d1d;color:#fecaca}`,
})
export class ScoreBadge {
  score = input<number | null | undefined>();
  priority = input<LeadPriority | null | undefined>();
}

@Component({
  selector: 'lp-state',
  imports: [MatIconModule, MatProgressSpinnerModule],
  template: `
    @if (loading()) {
      <div class="state"><mat-spinner diameter="36" /></div>
    } @else if (error()) {
      <div class="state"><mat-icon class="big">cloud_off</mat-icon><p>{{ error() }}</p><ng-content select="[retry]" /></div>
    } @else if (empty()) {
      <div class="state"><mat-icon class="big">{{ icon() }}</mat-icon><p>{{ emptyText() }}</p><ng-content select="[action]" /></div>
    }`,
  styles: `.state{display:flex;flex-direction:column;align-items:center;gap:8px;padding:48px 16px;text-align:center;color:var(--lp-muted)}
    .big{font-size:48px;width:48px;height:48px;opacity:.6}`,
})
export class StateView {
  loading = input(false);
  error = input<string | null>(null);
  empty = input(false);
  emptyText = input('Nothing here yet.');
  icon = input('inbox');
}

@Component({
  selector: 'lp-offline-banner',
  imports: [MatIconModule, DatePipe],
  template: `@if (show()) {<div class="banner"><mat-icon>wifi_off</mat-icon><span>Offline — showing saved copy{{ cachedAt() ? ' from ' : '' }}{{ cachedAt() | date: 'short' }}</span></div>}`,
  styles: `.banner{display:flex;gap:8px;align-items:center;padding:8px 12px;margin:0 0 12px;border-radius:10px;background:#fef3c7;color:#92400e;font-size:13px}`,
})
export class OfflineBanner {
  show = input(false);
  cachedAt = input<string | undefined>();
}

export const SHARED = [StagePipe, HumanizePipe, InrPipe, ScoreBadge, StateView, OfflineBanner, DatePipe, DecimalPipe] as const;

