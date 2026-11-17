import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { SHARED, Toast } from '../../shared/ui';

interface ImportResult { totalRows: number; created: number; duplicates: number; skipped: number; errors: string[]; }

@Component({
  selector: 'lp-lead-import',
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, ...SHARED],
  template: `
    <h1 class="page">Import CSV</h1>
    <div class="card stack">
      <p class="small">Columns: <code>business_name</code> (required), category, website, phone, email, address, locality, city, district, state, pincode, source_url, tags, notes.
        Each contact is recorded with its source (the <code>source_url</code> column, or the file name). Max 2&nbsp;MB / 5,000 rows.</p>
      <button mat-stroked-button (click)="template()"><mat-icon>download</mat-icon>Download template</button>
      <input type="file" accept=".csv,text/csv" (change)="pick($event)" #f hidden />
      <button mat-flat-button (click)="f.click()"><mat-icon>upload_file</mat-icon>{{ file()?.name ?? 'Choose CSV file' }}</button>
      <mat-slide-toggle [(ngModel)]="skipDuplicates">Skip exact duplicates</mat-slide-toggle>
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      <button mat-flat-button [disabled]="!file() || busy()" (click)="upload()">Import</button>
    </div>
    @if (result(); as r) {
      <div class="card stack" style="margin-top:12px">
        <div class="grid2">
          <div class="stat"><span class="value">{{ r.created }}</span><span class="label">Created</span></div>
          <div class="stat"><span class="value">{{ r.duplicates }}</span><span class="label">Duplicates skipped</span></div>
          <div class="stat"><span class="value">{{ r.skipped }}</span><span class="label">Invalid rows</span></div>
          <div class="stat"><span class="value">{{ r.totalRows }}</span><span class="label">Rows read</span></div>
        </div>
        @for (e of r.errors; track $index) { <div class="small muted">• {{ e }}</div> }
        <a mat-button routerLink="/leads" [queryParams]="{ sort: '-discovered' }">View leads</a>
      </div>
    }
  `,
})
export class LeadImportPage {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly file = signal<File | null>(null);
  readonly busy = signal(false);
  readonly result = signal<ImportResult | null>(null);
  skipDuplicates = true;

  pick(e: Event) {
    const f = (e.target as HTMLInputElement).files?.[0] ?? null;
    if (f && (f.size > 2 * 1024 * 1024 || !f.name.toLowerCase().endsWith('.csv'))) { this.toast.error(new Error('Choose a .csv file up to 2 MB.')); return; }
    this.file.set(f);
  }

  async upload() {
    const f = this.file();
    if (!f) return;
    const form = new FormData();
    form.append('file', f);
    form.append('skipDuplicates', String(this.skipDuplicates));
    this.busy.set(true);
    try { this.result.set(await this.api.upload<ImportResult>('/leads/import', form)); }
    catch (e) { this.toast.error(e); }
    finally { this.busy.set(false); }
  }

  async template() {
    const blob = await this.api.download('/leads/import/template');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob); a.download = 'leadpilot-import-template.csv'; a.click();
  }
}
