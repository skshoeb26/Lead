import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { SHARED, Toast } from '../../shared/ui';

interface Pair { leadId: string; businessName: string; duplicateLeadId: string; duplicateBusinessName: string; score: number; isExact: boolean; reasons: string[]; }

@Component({
  selector: 'lp-duplicates',
  imports: [RouterLink, MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Possible duplicates</h1>
    <lp-state [loading]="loading()" [empty]="!loading() && !pairs().length" icon="verified" emptyText="No duplicates found." />
    <div class="list">
      @for (p of pairs(); track p.leadId + p.duplicateLeadId) {
        <div class="card stack">
          <div class="row between"><span class="chip" [class.bad]="p.isExact">{{ p.isExact ? 'Exact match' : 'Similar' }} · {{ p.score * 100 | number: '1.0-0' }}%</span></div>
          <a [routerLink]="['/leads', p.leadId]"><b>{{ p.businessName }}</b></a>
          <a [routerLink]="['/leads', p.duplicateLeadId]">{{ p.duplicateBusinessName }}</a>
          <div class="small muted">{{ p.reasons.join(' · ') }}</div>
          <div class="actions">
            <button mat-flat-button (click)="merge(p)"><mat-icon>merge</mat-icon>Merge into first</button>
            <button mat-button (click)="dismiss(p)">Not a duplicate</button>
          </div>
        </div>
      }
    </div>
  `,
})
export class DuplicatesPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly pairs = signal<Pair[]>([]);
  readonly loading = signal(true);

  async ngOnInit() {
    try { this.pairs.set(await this.api.get<Pair[]>('/leads/duplicates')); } catch (e) { this.toast.error(e); } finally { this.loading.set(false); }
  }

  async merge(p: Pair) {
    if (!confirm(`Merge "${p.duplicateBusinessName}" into "${p.businessName}"? Contacts and history move to the first lead.`)) return;
    try { await this.api.post(`/leads/${p.leadId}/merge`, { duplicateLeadId: p.duplicateLeadId }); this.dismiss(p); this.toast.ok('Merged.'); }
    catch (e) { this.toast.error(e); }
  }

  dismiss(p: Pair) { this.pairs.set(this.pairs().filter(x => x !== p)); }
}
