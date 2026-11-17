import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSliderModule } from '@angular/material/slider';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/http';
import { Category, CreateDiscoveryJob, DiscoveryEstimate, DiscoveryJob, DiscoveryLocation, Paged, ProviderStatus } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

export const INDIAN_STATES = [
  'Andhra Pradesh', 'Arunachal Pradesh', 'Assam', 'Bihar', 'Chhattisgarh', 'Goa', 'Gujarat', 'Haryana', 'Himachal Pradesh', 'Jharkhand', 'Karnataka',
  'Kerala', 'Madhya Pradesh', 'Maharashtra', 'Manipur', 'Meghalaya', 'Mizoram', 'Nagaland', 'Odisha', 'Punjab', 'Rajasthan', 'Sikkim', 'Tamil Nadu',
  'Telangana', 'Tripura', 'Uttar Pradesh', 'Uttarakhand', 'West Bengal', 'Andaman and Nicobar Islands', 'Chandigarh',
  'Dadra and Nagar Haveli and Daman and Diu', 'Delhi', 'Jammu and Kashmir', 'Ladakh', 'Lakshadweep', 'Puducherry',
];

@Component({
  selector: 'lp-discover',
  imports: [FormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatSelectModule, MatChipsModule, MatButtonModule, MatButtonToggleModule,
    MatIconModule, MatSlideToggleModule, MatSliderModule, MatProgressBarModule, ...SHARED],
  template: `
    <h1 class="page">Discover leads</h1>

    <div class="card stack">
      <mat-form-field>
        <mat-label>Business categories</mat-label>
        <mat-select [(ngModel)]="categoryIds" multiple>
          @for (c of categories(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field>
        <mat-label>Keyword or business name (optional)</mat-label>
        <input matInput [(ngModel)]="keyword" placeholder="e.g. CBSE coaching, dental clinic" />
      </mat-form-field>

      <div class="grid2">
        <mat-form-field>
          <mat-label>State</mat-label>
          <mat-select [(ngModel)]="state">
            <mat-option [value]="''">Any / all India</mat-option>
            @for (s of states; track s) { <mat-option [value]="s">{{ s }}</mat-option> }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>PIN code (optional)</mat-label>
          <input matInput [(ngModel)]="pinCode" inputmode="numeric" maxlength="6" />
        </mat-form-field>
      </div>
      <mat-form-field>
        <mat-label>Cities (comma separated)</mat-label>
        <input matInput [(ngModel)]="cities" placeholder="Mumbai, Pune, Nashik, Surat" />
        <mat-hint>Each city becomes a separate search segment</mat-hint>
      </mat-form-field>
      <mat-form-field>
        <mat-label>Locality / district (optional)</mat-label>
        <input matInput [(ngModel)]="locality" placeholder="e.g. Andheri" />
      </mat-form-field>

      <div>
        <div class="small muted">Website</div>
        <mat-button-toggle-group [(ngModel)]="websiteFilter" aria-label="Website filter">
          <mat-button-toggle value="any">Any</mat-button-toggle>
          <mat-button-toggle value="with">Has website</mat-button-toggle>
          <mat-button-toggle value="without">No website</mat-button-toggle>
        </mat-button-toggle-group>
      </div>
      <mat-slide-toggle [(ngModel)]="requirePhone">Only businesses with a public phone</mat-slide-toggle>
      <mat-form-field>
        <mat-label>Minimum rating (optional)</mat-label>
        <input matInput type="number" min="0" max="5" step="0.5" [(ngModel)]="minRating" />
      </mat-form-field>

      <div>
        <div class="row between"><span class="small muted">Number of leads</span><b>{{ maxResults }}</b></div>
        <mat-slider min="10" max="300" step="10" discrete class="full"><input matSliderThumb [(ngModel)]="maxResults" /></mat-slider>
      </div>

      <mat-form-field>
        <mat-label>Data provider</mat-label>
        <mat-select [(ngModel)]="provider">
          @for (p of providers(); track p.name) {
            <mat-option [value]="p.name" [disabled]="!p.configured">{{ p.displayName }}{{ p.configured ? '' : ' (not configured)' }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      @if (selectedProvider()?.isMock) { <div class="notice warn"><mat-icon>science</mat-icon>Demo provider: creates fictitious "[DEMO]" businesses for testing only.</div> }
      @if (selectedProvider() && !selectedProvider()!.configured) { <div class="notice bad"><mat-icon>key_off</mat-icon>This provider needs an API key on the server. You can still import leads via CSV.</div> }
      <mat-slide-toggle [(ngModel)]="autoAnalyse">Analyse websites automatically after discovery</mat-slide-toggle>
      <p class="small muted">Recommended: leave off to save cost — import first, then analyse the leads you choose.</p>

      @if (estimate(); as e) {
        <div class="notice" [class.warn]="e.requiresConfirmation">
          <mat-icon>payments</mat-icon>
          <span>{{ e.segments }} search segment(s), ~{{ e.estimatedRequests }} API request(s), estimated cost <b>{{ '$' + (e.estimatedCostUsd | number: '1.2-3') }}</b>.
            This month: {{ '$' + (e.monthSpendUsd | number: '1.2-2') }} of {{ '$' + (e.monthlyBudgetUsd | number: '1.0-0') }} budget.</span>
        </div>
      }
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      <div class="actions">
        <button mat-stroked-button (click)="runEstimate()" [disabled]="busy()"><mat-icon>calculate</mat-icon>Estimate cost</button>
        <button mat-flat-button (click)="start()" [disabled]="busy()"><mat-icon>play_arrow</mat-icon>{{ estimate()?.requiresConfirmation ? 'Confirm & start' : 'Start discovery' }}</button>
      </div>
    </div>

    <div class="row between"><h2 class="section">Recent jobs</h2><a mat-button routerLink="/leads/import"><mat-icon>upload_file</mat-icon>Import CSV</a></div>
    <div class="list">
      @for (j of jobs(); track j.id) {
        <a class="list-item col" [routerLink]="['/discover/jobs', j.id]">
          <div class="row full"><b class="grow ellipsis">{{ j.name }}</b><span class="chip" [class.ok]="j.status === 'Completed'" [class.info]="j.status === 'Running'" [class.bad]="j.status === 'Failed'">{{ j.status }}</span></div>
          <mat-progress-bar [mode]="j.status === 'Running' && !j.progress ? 'indeterminate' : 'determinate'" [value]="j.progress * 100" />
          <div class="small muted">{{ j.createdCount }} new · {{ j.duplicateCount }} duplicates · {{ j.skippedCount }} skipped · {{ j.createdAt | date: 'short' }}</div>
        </a>
      } @empty { <lp-state [empty]="true" icon="travel_explore" emptyText="No discovery jobs yet." /> }
    </div>
  `,
  styles: `.col{flex-direction:column;align-items:stretch;gap:8px} mat-button-toggle-group{margin-top:4px;flex-wrap:wrap}`,
})
export class DiscoverPage implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private toast = inject(Toast);
  private router = inject(Router);
  readonly states = INDIAN_STATES;
  readonly categories = signal<Category[]>([]);
  readonly providers = signal<ProviderStatus[]>([]);
  readonly jobs = signal<DiscoveryJob[]>([]);
  readonly estimate = signal<DiscoveryEstimate | null>(null);
  readonly busy = signal(false);
  private timer?: ReturnType<typeof setInterval>;

  categoryIds: string[] = [];
  keyword = '';
  state = '';
  cities = '';
  locality = '';
  pinCode = '';
  websiteFilter: 'any' | 'with' | 'without' = 'any';
  requirePhone = false;
  minRating: number | null = null;
  maxResults = 50;
  provider = 'google_places';
  autoAnalyse = false;
  readonly selectedProviderName = signal('google_places');
  readonly selectedProvider = computed(() => this.providers().find(p => p.name === this.selectedProviderName()) ?? this.providers().find(p => p.name === this.provider));

  async ngOnInit() {
    try {
      const [cats, provs] = await Promise.all([this.api.get<Category[]>('/categories'), this.api.get<ProviderStatus[]>('/discovery/providers')]);
      this.categories.set(cats);
      this.providers.set(provs);
      const preferred = provs.find(p => p.configured && !p.isMock) ?? provs.find(p => p.configured) ?? provs[0];
      if (preferred) { this.provider = preferred.name; this.selectedProviderName.set(preferred.name); }
    } catch (e) { this.toast.error(e); }
    await this.loadJobs();
    this.timer = setInterval(() => { if (this.jobs().some(j => j.status === 'Running' || j.status === 'Pending')) void this.loadJobs(); }, 4000);
  }

  ngOnDestroy() { clearInterval(this.timer); }

  async loadJobs() {
    try { this.jobs.set((await this.api.get<Paged<DiscoveryJob>>('/discovery/jobs', { pageSize: 10 })).items); } catch { /* offline */ }
  }

  private request(confirm: boolean): CreateDiscoveryJob {
    this.selectedProviderName.set(this.provider);
    const cityList = this.cities.split(',').map(c => c.trim()).filter(Boolean);
    const base: DiscoveryLocation = { state: this.state || undefined, locality: this.locality || undefined, pinCode: this.pinCode || undefined };
    const locations = cityList.length ? cityList.map(city => ({ ...base, city })) : this.state || this.pinCode || this.locality ? [base] : [];
    return {
      provider: this.provider, maxResults: this.maxResults, autoAnalyse: this.autoAnalyse, confirmCost: confirm,
      query: { categoryIds: this.categoryIds, keyword: this.keyword || undefined, locations, websiteFilter: this.websiteFilter,
        requirePhone: this.requirePhone, minRating: this.minRating ?? undefined, skipPermanentlyClosed: true },
    };
  }

  async runEstimate() {
    this.busy.set(true);
    try { this.estimate.set(await this.api.post<DiscoveryEstimate>('/discovery/estimate', this.request(false))); }
    catch (e) { this.toast.error(e); }
    finally { this.busy.set(false); }
  }

  async start() {
    this.busy.set(true);
    try {
      const job = await this.api.post<DiscoveryJob>('/discovery/jobs', this.request(!!this.estimate()?.requiresConfirmation));
      this.toast.ok('Discovery started — it keeps running on the server even if you close the app.');
      await this.router.navigate(['/discover/jobs', job.id]);
    } catch (e) {
      if (errorMessage(e).includes('Confirm')) await this.runEstimate();
      this.toast.error(e);
    } finally { this.busy.set(false); }
  }
}
