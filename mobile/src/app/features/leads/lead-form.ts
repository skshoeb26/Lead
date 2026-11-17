import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Category } from '../../core/models';
import { SHARED, Toast } from '../../shared/ui';
import { INDIAN_STATES } from '../discover/discover';

export const PHONE_PATTERN = /^\+?[0-9 ()-]{8,20}$/;
export const PIN_PATTERN = /^[1-9][0-9]{5}$/;

@Component({
  selector: 'lp-lead-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule, ...SHARED],
  template: `
    <h1 class="page">New lead</h1>
    <form class="card stack" [formGroup]="form" (ngSubmit)="save()">
      <mat-form-field><mat-label>Business name *</mat-label><input matInput formControlName="businessName" />
        @if (form.controls.businessName.touched && form.controls.businessName.invalid) { <mat-error>Required</mat-error> }</mat-form-field>
      <mat-form-field><mat-label>Category</mat-label><mat-select formControlName="categoryId"><mat-option [value]="null">None</mat-option>
        @for (c of categories(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field><mat-label>Website</mat-label><input matInput formControlName="websiteUrl" inputmode="url" placeholder="https://" /></mat-form-field>
      <mat-form-field><mat-label>Public business email</mat-label><input matInput formControlName="email" inputmode="email" />
        @if (form.controls.email.invalid) { <mat-error>Invalid email</mat-error> }</mat-form-field>
      <mat-form-field><mat-label>Public business phone</mat-label><input matInput formControlName="phone" inputmode="tel" />
        @if (form.controls.phone.invalid) { <mat-error>Invalid phone</mat-error> }</mat-form-field>
      <mat-form-field><mat-label>Where did you find these contact details?</mat-label><input matInput formControlName="contactSourceUrl" placeholder="URL or e.g. 'Visiting card at expo'" />
        <mat-hint>Required when adding email/phone — every contact records its source.</mat-hint>
        @if (form.errors?.['sourceRequired']) { <mat-error>Source is required for contact details</mat-error> }</mat-form-field>
      <mat-form-field><mat-label>Address</mat-label><input matInput formControlName="addressLine" /></mat-form-field>
      <div class="grid2">
        <mat-form-field><mat-label>Locality</mat-label><input matInput formControlName="locality" /></mat-form-field>
        <mat-form-field><mat-label>City</mat-label><input matInput formControlName="city" /></mat-form-field>
      </div>
      <div class="grid2">
        <mat-form-field><mat-label>State</mat-label><mat-select formControlName="state"><mat-option [value]="null">—</mat-option>
          @for (s of states; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field><mat-label>PIN code</mat-label><input matInput formControlName="pinCode" inputmode="numeric" maxlength="6" />
          @if (form.controls.pinCode.invalid) { <mat-error>6 digits</mat-error> }</mat-form-field>
      </div>
      <mat-form-field><mat-label>Notes</mat-label><textarea matInput rows="3" formControlName="notes"></textarea></mat-form-field>
      <button mat-flat-button type="submit" [disabled]="busy()">Save lead</button>
    </form>
  `,
})
export class LeadFormPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(Toast);
  private router = inject(Router);
  readonly states = INDIAN_STATES;
  readonly categories = signal<Category[]>([]);
  readonly busy = signal(false);

  readonly form = inject(FormBuilder).group({
    businessName: ['', [Validators.required, Validators.maxLength(300)]],
    categoryId: [null as string | null],
    websiteUrl: [''],
    email: ['', [Validators.email]],
    phone: ['', [Validators.pattern(PHONE_PATTERN)]],
    contactSourceUrl: [''],
    addressLine: [''], locality: [''], city: [''], state: [null as string | null],
    pinCode: ['', [Validators.pattern(PIN_PATTERN)]],
    notes: [''],
  }, {
    validators: g => (g.value.email || g.value.phone) && !g.value.contactSourceUrl ? { sourceRequired: true } : null,
  });

  async ngOnInit() { try { this.categories.set(await this.api.get<Category[]>('/categories')); } catch { /* offline */ } }

  async save() {
    this.form.markAllAsTouched();
    if (this.form.invalid) { if (this.form.errors?.['sourceRequired']) this.toast.error(new Error('Record where the contact details were found.')); return; }
    this.busy.set(true);
    const v = this.form.getRawValue();
    const clean = Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x === '' ? null : x]));
    try {
      const r = await this.api.post<{ id: string; possibleDuplicates: { businessName: string }[] }>('/leads', clean);
      if (r.possibleDuplicates?.length) this.toast.ok(`Saved. Possible duplicate of ${r.possibleDuplicates[0].businessName} — review in Duplicates.`);
      await this.router.navigate(['/leads', r.id], { replaceUrl: true });
    } catch (e) { this.toast.error(e); }
    finally { this.busy.set(false); }
  }
}
