import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ConfigService, validateBaseUrl } from '../../core/config.service';
import { errorMessage } from '../../core/http';

@Component({
  selector: 'lp-login',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatExpansionModule, MatProgressBarModule],
  template: `
    <div class="wrap">
      <div class="hero">
        <mat-icon class="logo">rocket_launch</mat-icon>
        <h1>LeadPilot <span>AI</span></h1>
        <p class="muted">Find, qualify and win clients across India.</p>
      </div>

      @if (auth.sessionExpired()) { <div class="notice warn"><mat-icon>schedule</mat-icon>Your session expired. Please sign in again.</div> }

      <form class="card stack" [formGroup]="form" (ngSubmit)="submit()">
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
        @if (mode() === 'register') {
          <div class="notice info"><mat-icon>info</mat-icon>First-time setup: create the owner account. Registration closes after this.</div>
          <mat-form-field><mat-label>Your name</mat-label><input matInput formControlName="displayName" autocomplete="name" /></mat-form-field>
        }
        <mat-form-field>
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="username" inputmode="email" />
          @if (form.controls.email.touched && form.controls.email.invalid) { <mat-error>Enter a valid email</mat-error> }
        </mat-form-field>
        <mat-form-field>
          <mat-label>Password</mat-label>
          <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" />
          <button mat-icon-button matSuffix type="button" (click)="showPassword.set(!showPassword())" aria-label="Show password">
            <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
          @if (mode() === 'register') { <mat-hint>At least 10 characters with upper, lower case and a digit</mat-hint> }
        </mat-form-field>
        @if (error()) { <div class="notice bad"><mat-icon>error</mat-icon>{{ error() }}</div> }
        <button mat-flat-button class="full big" type="submit" [disabled]="busy()">{{ mode() === 'login' ? 'Sign in' : 'Create owner account' }}</button>
        <button mat-button type="button" (click)="mode.set(mode() === 'login' ? 'register' : 'login')">
          {{ mode() === 'login' ? 'First time? Set up owner account' : 'Already set up? Sign in' }}
        </button>
      </form>

      <mat-accordion>
        <mat-expansion-panel class="server">
          <mat-expansion-panel-header><mat-panel-title><mat-icon>dns</mat-icon>&nbsp;Server</mat-panel-title>
            <mat-panel-description class="ellipsis">{{ config.apiBaseUrl() || 'Not set' }}</mat-panel-description></mat-expansion-panel-header>
          <mat-form-field>
            <mat-label>Server URL</mat-label>
            <input matInput [value]="config.apiBaseUrl()" #url placeholder="https://api.yourdomain.com" inputmode="url" />
            <mat-hint>{{ config.allowInsecure ? 'Development build: http allowed' : 'Must be HTTPS' }}</mat-hint>
          </mat-form-field>
          <button mat-stroked-button type="button" (click)="saveUrl(url.value)">Save server</button>
          @if (urlMessage()) { <p class="small muted">{{ urlMessage() }}</p> }
        </mat-expansion-panel>
      </mat-accordion>
    </div>
  `,
  styles: `
    .wrap{max-width:420px;margin:0 auto;padding:calc(32px + env(safe-area-inset-top)) 16px 32px;display:flex;flex-direction:column;gap:16px}
    .hero{text-align:center} .hero h1{margin:8px 0 4px;font-size:30px;font-weight:700;letter-spacing:-.5px} .hero h1 span{color:var(--mat-sys-primary)}
    .logo{font-size:48px;width:48px;height:48px;color:var(--mat-sys-primary)}
    .big{height:48px;font-size:16px} .server{border-radius:16px !important}
  `,
})
export class LoginPage {
  readonly auth = inject(AuthService);
  readonly config = inject(ConfigService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  readonly mode = signal<'login' | 'register'>('login');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);
  readonly urlMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    displayName: [''],
  });

  async saveUrl(url: string) {
    const err = validateBaseUrl(url, this.config.allowInsecure);
    if (err) { this.urlMessage.set(err); return; }
    await this.config.setBaseUrl(url);
    this.urlMessage.set('Saved.');
  }

  async submit() {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    if (!this.config.apiBaseUrl()) { this.error.set('Set the server URL first (Server section below).'); return; }
    this.busy.set(true);
    this.error.set(null);
    const { email, password, displayName } = this.form.getRawValue();
    try {
      if (this.mode() === 'login') await this.auth.login(email, password);
      else await this.auth.register(email, password, displayName || 'Owner');
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
