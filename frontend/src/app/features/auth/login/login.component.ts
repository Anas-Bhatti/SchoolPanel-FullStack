// ============================================================
// features/auth/login/login.component.ts
// ============================================================

import {
  Component, inject, signal, OnInit
} from '@angular/core';
import {
  FormBuilder, Validators, ReactiveFormsModule
} from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '@core/services/auth.service';
import { ToastService } from '@core/services/api.services';
import { ThemeService } from '@core/services/theme.service';

@Component({
  selector: 'sp-login',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatFormFieldModule, MatInputModule,
    MatButtonModule, MatProgressSpinnerModule
  ],
  template: `
    <div class="auth-page">
      <div class="auth-card">

        <!-- Brand -->
        <div class="auth-brand">
          <div class="auth-logo">
            <span class="material-icons-round">school</span>
          </div>
          <h1>SchoolPanel</h1>
          <p>Sign in to your account</p>
        </div>

        <!-- Login Form -->
        @if (!authSvc.requires2fa()) {
          <form [formGroup]="form" (ngSubmit)="onLogin()" class="auth-form" novalidate>

            <mat-form-field appearance="outline">
              <mat-label>Email address</mat-label>
              <input matInput type="email" formControlName="email"
                     autocomplete="email" placeholder="admin@school.edu">
              @if (form.get('email')?.hasError('required') && form.get('email')?.touched) {
                <mat-error>Email is required</mat-error>
              }
              @if (form.get('email')?.hasError('email')) {
                <mat-error>Enter a valid email address</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput [type]="showPassword() ? 'text' : 'password'"
                     formControlName="password"
                     autocomplete="current-password">
              <button mat-icon-button matSuffix type="button"
                      (click)="showPassword.update(v => !v)"
                      [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
                <span class="material-icons-round">
                  {{ showPassword() ? 'visibility_off' : 'visibility' }}
                </span>
              </button>
              @if (form.get('password')?.hasError('required') && form.get('password')?.touched) {
                <mat-error>Password is required</mat-error>
              }
            </mat-form-field>

            @if (errorMessage()) {
              <div class="auth-error" role="alert">
                <span class="material-icons-round">error</span>
                {{ errorMessage() }}
              </div>
            }

            <button mat-raised-button color="primary" type="submit"
                    class="auth-submit"
                    [disabled]="loading() || form.invalid">
              @if (loading()) {
                <mat-progress-spinner diameter="18" mode="indeterminate" />
              } @else {
                Sign In
              }
            </button>

            <!-- Divider -->
            <div class="auth-divider"><span>or continue with</span></div>

            <!-- Google OAuth -->
            <button type="button" class="google-btn" (click)="onGoogleLogin()">
              <svg width="18" height="18" viewBox="0 0 24 24">
                <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"/>
                <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/>
                <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"/>
                <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/>
              </svg>
              Continue with Google
            </button>

          </form>
        }

        <!-- 2FA Step -->
        @if (authSvc.requires2fa()) {
          <div class="two-fa-section">
            <div class="two-fa-icon">
              <span class="material-icons-round">security</span>
            </div>
            <h2>Two-Factor Authentication</h2>
            <p>Enter the 6-digit code from your authenticator app</p>

            <form [formGroup]="twoFaForm" (ngSubmit)="onVerify2fa()" class="auth-form">
              <mat-form-field appearance="outline">
                <mat-label>Verification Code</mat-label>
                <input matInput formControlName="code"
                       maxlength="6" placeholder="000000"
                       autocomplete="one-time-code"
                       inputmode="numeric">
                @if (twoFaForm.get('code')?.hasError('pattern')) {
                  <mat-error>Enter a valid 6-digit code</mat-error>
                }
              </mat-form-field>

              @if (errorMessage()) {
                <div class="auth-error" role="alert">
                  <span class="material-icons-round">error</span>
                  {{ errorMessage() }}
                </div>
              }

              <button mat-raised-button color="primary" type="submit"
                      class="auth-submit"
                      [disabled]="loading() || twoFaForm.invalid">
                @if (loading()) {
                  <mat-progress-spinner diameter="18" mode="indeterminate" />
                } @else {
                  Verify
                }
              </button>

              <button type="button" class="btn btn-ghost"
                      (click)="authSvc.requires2fa.set(false)">
                ← Back to login
              </button>
            </form>
          </div>
        }

      </div>

      <!-- Theme toggle -->
      <button class="theme-toggle" (click)="theme.toggleTheme()"
              [title]="theme.isDark() ? 'Light mode' : 'Dark mode'">
        <span class="material-icons-round">
          {{ theme.isDark() ? 'light_mode' : 'dark_mode' }}
        </span>
      </button>
    </div>
  `,
  styles: [`
    .auth-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--color-bg);
      padding: 20px;
      position: relative;
    }

    .auth-card {
      width: 100%;
      max-width: 420px;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-xl);
      padding: 40px 36px;
      box-shadow: var(--shadow-lg);
    }

    .auth-brand {
      text-align: center;
      margin-bottom: 32px;

      h1 {
        font-size: 24px;
        font-weight: 700;
        color: var(--color-text);
        margin: 12px 0 6px;
      }

      p { font-size: 14px; color: var(--color-text-secondary); margin: 0; }
    }

    .auth-logo {
      width: 56px; height: 56px;
      background: var(--color-primary);
      border-radius: 14px;
      display: flex; align-items: center; justify-content: center;
      margin: 0 auto;

      .material-icons-round { font-size: 28px; color: white; }
    }

    .auth-form {
      display: flex;
      flex-direction: column;
      gap: 16px;

      mat-form-field { width: 100%; }
    }

    .auth-error {
      display: flex;
      align-items: center;
      gap: 8px;
      background: var(--color-danger-bg);
      color: var(--color-danger);
      padding: 10px 14px;
      border-radius: var(--radius-sm);
      font-size: 13px;
      border: 1px solid rgba(220,38,38,.2);

      .material-icons-round { font-size: 18px; }
    }

    .auth-submit {
      height: 44px;
      font-size: 14px;
      font-weight: 500;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
    }

    .auth-divider {
      display: flex;
      align-items: center;
      gap: 12px;
      color: var(--color-text-muted);
      font-size: 12px;

      &::before, &::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--color-border);
      }
    }

    .google-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 10px;
      width: 100%;
      height: 44px;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      color: var(--color-text);
      transition: all var(--transition-fast);

      &:hover {
        background: var(--color-surface-2);
        border-color: var(--color-primary);
      }
    }

    .two-fa-section {
      text-align: center;

      .two-fa-icon {
        width: 64px; height: 64px;
        background: var(--color-primary-50);
        border-radius: 50%;
        display: flex; align-items: center; justify-content: center;
        margin: 0 auto 16px;

        .material-icons-round { font-size: 32px; color: var(--color-primary); }
      }

      h2 { font-size: 18px; font-weight: 600; margin: 0 0 6px; }
      p { font-size: 13px; color: var(--color-text-secondary); margin: 0 0 24px; }
    }

    .theme-toggle {
      position: fixed;
      bottom: 20px;
      right: 20px;
      width: 40px; height: 40px;
      border-radius: 50%;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      box-shadow: var(--shadow-md);
      display: flex; align-items: center; justify-content: center;
      cursor: pointer;
      color: var(--color-text-secondary);
      transition: all var(--transition-fast);

      &:hover { transform: scale(1.1); }
    }
  `]
})
export class LoginComponent {
  authSvc = inject(AuthService);
  theme   = inject(ThemeService);
  toast   = inject(ToastService);
  router  = inject(Router);
  route   = inject(ActivatedRoute);
  fb      = inject(FormBuilder);

  loading      = signal(false);
  errorMessage = signal<string | null>(null);
  showPassword = signal(false);

  form = this.fb.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  twoFaForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]]
  });

  onLogin(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.errorMessage.set(null);

    const { email, password } = this.form.value;

    this.authSvc.login({
      email: email!,
      password: password!,
      deviceInfo: navigator.userAgent
    }).subscribe({
      next: (response) => {
        this.loading.set(false);
        if (!response.requiresTwoFactor) {
          this.navigateAfterLogin();
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.handleError(err);
      }
    });
  }

  onVerify2fa(): void {
    if (this.twoFaForm.invalid) return;
    this.loading.set(true);
    this.errorMessage.set(null);

    this.authSvc.verifyTwoFactor(this.twoFaForm.value.code!).subscribe({
      next: () => {
        this.loading.set(false);
        this.navigateAfterLogin();
      },
      error: (err) => {
        this.loading.set(false);
        this.handleError(err);
      }
    });
  }

  onGoogleLogin(): void {
    // Google One Tap / Sign-In button — initialise Google Identity Services
    const google = (window as any)['google'];
    if (!google) {
      this.toast.error('Google Sign-In unavailable',
        'Google Identity Services script not loaded.');
      return;
    }

    google.accounts.id.initialize({
      client_id: (window as any)['GOOGLE_CLIENT_ID'],
      callback: (response: any) => {
        this.authSvc.googleLogin(response.credential).subscribe({
          next: () => this.navigateAfterLogin(),
          error: (err) => this.handleError(err)
        });
      }
    });

    google.accounts.id.prompt();
  }

  private navigateAfterLogin(): void {
    const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.router.navigateByUrl(returnUrl);
  }

  private handleError(err: any): void {
    const status = err?.status;
    const code   = err?.error?.code;

    if (status === 401 || code === 'INVALID_CREDENTIALS') {
      this.errorMessage.set('Invalid email or password. Please try again.');
    } else if (status === 423 || code === 'ACCOUNT_LOCKED') {
      this.errorMessage.set(err?.error?.detail || 'Account locked. Try again later.');
    } else if (code === 'INVALID_2FA_CODE') {
      this.errorMessage.set('Verification code is incorrect.');
    } else {
      this.errorMessage.set('An error occurred. Please try again.');
    }
  }
}