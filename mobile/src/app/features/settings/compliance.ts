import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../core/api.service';
import { Channel, Suppression } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';

@Component({
  selector: 'lp-compliance',
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">Opt-outs & consent</h1>
    <div class="notice info"><mat-icon>gavel</mat-icon><span>LeadPilot helps you respect opt-outs and record consent, but it is not a legal compliance guarantee. Review your obligations under Indian law and each provider's policies.</span></div>
    <div class="card stack add">
      <b>Add to suppression list</b>
      <mat-form-field><mat-label>Channel</mat-label><mat-select [(ngModel)]="channel"><mat-option value="Email">Email</mat-option><mat-option value="WhatsApp">WhatsApp</mat-option><mat-option value="Phone">Phone</mat-option></mat-select></mat-form-field>
      <mat-form-field><mat-label>Email / phone / &#64;domain</mat-label><input matInput [(ngModel)]="value" /></mat-form-field>
      <mat-form-field><mat-label>Reason</mat-label><input matInput [(ngModel)]="reason" placeholder="Replied 'unsubscribe'" /></mat-form-field>
      <button mat-flat-button (click)="add()" [disabled]="!value.trim()">Add opt-out</button>
    </div>
    <h2 class="section">Suppressed ({{ items().length }})</h2>
    <div class="list">
      @for (s of items(); track s.id) {
        <div class="list-item"><mat-icon>block</mat-icon><div class="grow"><b class="ellipsis">{{ s.value }}</b><div class="small muted">{{ s.channel }} · {{ s.reason }} · {{ s.createdAt | date: 'mediumDate' }}</div></div>
          <button mat-icon-button (click)="remove(s)" aria-label="Remove"><mat-icon>delete</mat-icon></button></div>
      } @empty { <p class="small muted">No opt-outs recorded.</p> }
    </div>
  `,
  styles: `.add{margin-top:12px}`,
})
export class CompliancePage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  readonly items = signal<Suppression[]>([]);
  channel: Channel = 'Email';
  value = '';
  reason = 'Opted out';

  async ngOnInit() { await this.load(); }
  async load() { try { this.items.set(await this.api.get<Suppression[]>('/compliance/suppressions')); } catch (e) { this.toast.error(e); } }

  async add() {
    try { await this.api.post('/compliance/suppressions', { channel: this.channel, value: this.value, reason: this.reason }); this.value = ''; await this.load(); this.toast.ok('Added. Pending emails to this recipient were cancelled.'); }
    catch (e) { this.toast.error(e); }
  }

  async remove(s: Suppression) {
    if (!confirm(`Remove ${s.value} from the opt-out list? Only do this if they explicitly asked to be contacted again.`)) return;
    try { await this.api.delete(`/compliance/suppressions/${s.id}`); await this.load(); } catch (e) { this.toast.error(e); }
  }
}
