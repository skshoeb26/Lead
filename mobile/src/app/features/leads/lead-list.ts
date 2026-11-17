import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { LeadListItem, LeadPriority, PIPELINE_STAGES, Paged, PipelineStage } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-lead-list',
  imports: [FormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatSelectModule, MatChipsModule, MatButtonModule, MatIconModule, MatMenuModule, ...SHARED],
  template: `
    <div class="row between"><h1 class="page">Leads <span class="muted small">{{ total() }}</span></h1>
      <button mat-icon-button [matMenuTriggerFor]="more" aria-label="More"><mat-icon>more_vert</mat-icon></button>
      <mat-menu #more="matMenu">
        <a mat-menu-item routerLink="/leads/import"><mat-icon>upload_file</mat-icon>Import CSV</a>
        <button mat-menu-item (click)="export()"><mat-icon>download</mat-icon>Export CSV</button>
        <a mat-menu-item routerLink="/leads/duplicates"><mat-icon>merge</mat-icon>Review duplicates</a>
      </mat-menu>
    </div>

    <mat-form-field class="search">
      <mat-icon matPrefix>search</mat-icon>
      <input matInput placeholder="Search name, city, email, phone" [(ngModel)]="search" (keyup.enter)="reload()" (blur)="reload()" />
    </mat-form-field>

    <mat-chip-listbox class="filters" aria-label="Filters">
      <mat-chip-option [selected]="priorityF === 'High'" (selectionChange)="toggle('priority', 'High', $event.selected)">High priority</mat-chip-option>
      <mat-chip-option [selected]="hasEmailF" (selectionChange)="hasEmailF = $event.selected; reload()">Has email</mat-chip-option>
      <mat-chip-option [selected]="websiteF === 'false'" (selectionChange)="websiteF = $event.selected ? 'false' : ''; reload()">No website</mat-chip-option>
      <mat-chip-option [selected]="websiteF === 'true'" (selectionChange)="websiteF = $event.selected ? 'true' : ''; reload()">Has website</mat-chip-option>
      <mat-chip-option [selected]="weakSiteF" (selectionChange)="weakSiteF = $event.selected; reload()">Weak website</mat-chip-option>
    </mat-chip-listbox>
    <div class="grid2">
      <mat-form-field><mat-label>Stage</mat-label>
        <mat-select [(ngModel)]="stageF" (selectionChange)="reload()"><mat-option value="">All stages</mat-option>
          @for (s of stages; track s) { <mat-option [value]="s">{{ s | stage }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field><mat-label>Sort</mat-label>
        <mat-select [(ngModel)]="sortF" (selectionChange)="reload()">
          <mat-option value="-score">Highest score</mat-option><mat-option value="-discovered">Newest</mat-option>
          <mat-option value="name">Name A–Z</mat-option><mat-option value="followup">Next follow-up</mat-option><mat-option value="-updated">Recently updated</mat-option>
        </mat-select></mat-form-field>
    </div>
    <mat-form-field><mat-label>City / cities</mat-label><input matInput [(ngModel)]="cityF" (blur)="reload()" (keyup.enter)="reload()" placeholder="Pune, Mumbai" /></mat-form-field>

    <lp-offline-banner [show]="fromCache()" [cachedAt]="cachedAt()" />
    <lp-state [loading]="loading() && !items().length" [error]="error()" [empty]="!loading() && !error() && !items().length" icon="groups" emptyText="No leads match. Try discovery or CSV import.">
      <a action mat-flat-button routerLink="/discover">Discover leads</a>
    </lp-state>

    <div class="list">
      @for (l of items(); track l.id) {
        <a class="list-item" [routerLink]="['/leads', l.id]">
          <lp-score [score]="l.score" [priority]="l.priority" />
          <div class="grow">
            <div class="ellipsis"><b>{{ l.businessName }}</b></div>
            <div class="small muted ellipsis">{{ l.categoryName || 'Uncategorised' }} · {{ l.city || l.state || '—' }}</div>
            <div class="row small icons">
              <mat-icon [class.on]="!!l.websiteUrl" title="Website">language</mat-icon>
              <mat-icon [class.on]="!!l.email" title="Email">mail</mat-icon>
              <mat-icon [class.on]="!!l.phone" title="Phone">call</mat-icon>
              <span class="chip">{{ l.stage | stage }}</span>
              @if (l.nextFollowUpAt) { <span class="chip warn"><mat-icon>event</mat-icon>{{ l.nextFollowUpAt | date: 'd MMM' }}</span> }
            </div>
          </div>
          <mat-icon class="muted">chevron_right</mat-icon>
        </a>
      }
    </div>
    @if (items().length < total()) { <button mat-stroked-button class="full more" (click)="loadMore()" [disabled]="loading()">Load more</button> }

    <a mat-fab extended class="fab" routerLink="/leads/new" aria-label="Add lead"><mat-icon>add</mat-icon>Add lead</a>
  `,
  styles: `.search{margin-top:4px} .filters{display:block;margin:0 0 12px} .icons mat-icon{font-size:16px;width:16px;height:16px;color:var(--lp-border)}
    .icons mat-icon.on{color:var(--mat-sys-primary)} .more{margin-top:12px}`,
})
export class LeadListPage implements OnInit {
  readonly stage = input<string>();
  readonly priority = input<string>();
  readonly sort = input<string>();
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly stages = PIPELINE_STAGES;
  readonly items = signal<LeadListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly fromCache = signal(false);
  readonly cachedAt = signal<string | undefined>(undefined);
  private page = 1;

  search = '';
  stageF: PipelineStage | '' = '';
  priorityF: LeadPriority | '' = '';
  sortF = '-score';
  hasEmailF = false;
  websiteF: '' | 'true' | 'false' = '';
  weakSiteF = false;
  cityF = '';

  ngOnInit() {
    this.stageF = (this.stage() as PipelineStage) ?? '';
    this.priorityF = (this.priority() as LeadPriority) ?? '';
    this.sortF = this.sort() ?? '-score';
    void this.reload();
  }

  toggle(kind: 'priority', value: LeadPriority, on: boolean) { if (kind === 'priority') this.priorityF = on ? value : ''; void this.reload(); }

  private query() {
    return { search: this.search, stage: this.stageF, priority: this.priorityF, sort: this.sortF, hasEmail: this.hasEmailF || undefined,
      hasWebsite: this.websiteF || undefined, maxWebsiteQuality: this.weakSiteF ? 50 : undefined, city: this.cityF, page: this.page, pageSize: 25 };
  }

  async reload() { this.page = 1; this.items.set([]); await this.load(); }
  async loadMore() { this.page++; await this.load(); }

  private async load() {
    this.loading.set(true);
    this.error.set(null);
    try {
      const r = await this.api.getCached<Paged<LeadListItem>>('/leads', this.query());
      this.items.set(this.page === 1 ? r.data.items : [...this.items(), ...r.data.items]);
      this.total.set(r.data.totalCount);
      this.fromCache.set(r.fromCache);
      this.cachedAt.set(r.cachedAt);
    } catch (e) {
      this.error.set('Could not load leads.');
    } finally { this.loading.set(false); }
  }

  async export() {
    try {
      const blob = await this.api.download('/leads/export', this.query());
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = `leadpilot-leads-${new Date().toISOString().slice(0, 10)}.csv`; a.click();
      URL.revokeObjectURL(url);
    } catch (e) { this.toast.error(e); }
  }
}
