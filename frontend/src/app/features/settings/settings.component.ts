// ============================================================
// features/settings/settings.component.ts
// ============================================================
import {
  Component, inject, signal, OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder, ReactiveFormsModule, Validators
} from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { HttpClient } from '@angular/common/http';
import { SettingsService, ToastService } from '@core/services/api.services';
import { ThemeService } from '@core/services/theme.service';
import { AuthService } from '@core/services/auth.service';
import { environment } from '@env/environment';
import type { Setting } from '@core/models';

const COLOR_PRESETS = [
  '#2563EB', '#1D4ED8', '#7C3AED', '#DC2626',
  '#16A34A', '#D97706', '#DB2777', '#0891B2',
  '#059669', '#EA580C', '#9333EA', '#0284C7'
];

@Component({
  selector: 'sp-settings',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatTabsModule, MatProgressBarModule
  ],
  template: `
    <div>
      <div class="page-header">
        <h1>Settings</h1>
      </div>

      <mat-tab-group color="primary" animationDuration="150ms">

        <!-- School Profile Tab -->
        <mat-tab label="School Profile">
          <div class="tab-body">
            <form [formGroup]="profileForm" (ngSubmit)="saveProfile()" class="card settings-card">

              <div class="form-section-title">School Information</div>

              <div class="grid-2">
                <mat-form-field class="full">
                  <mat-label>School Name</mat-label>
                  <input matInput formControlName="schoolName">
                </mat-form-field>

                <mat-form-field class="full">
                  <mat-label>Address</mat-label>
                  <textarea matInput formControlName="address" rows="2"></textarea>
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Phone</mat-label>
                  <input matInput formControlName="phone">
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Email</mat-label>
                  <input matInput type="email" formControlName="email">
                </mat-form-field>

                <mat-form-field class="full">
                  <mat-label>Website</mat-label>
                  <input matInput formControlName="website">
                </mat-form-field>
              </div>

              <!-- Logo Upload -->
              <div class="form-section-title">School Logo</div>
              <div class="logo-upload">
                <div class="logo-preview" (click)="logoInput.click()">
                  @if (logoPreview()) {
                    <img [src]="logoPreview()" alt="Logo preview">
                  } @else {
                    <div class="logo-placeholder">
                      <span class="material-icons-round">add_photo_alternate</span>
                      <span>Upload Logo</span>
                    </div>
                  }
                </div>
                <input #logoInput type="file" accept="image/jpeg,image/png,image/webp"
                       (change)="onLogoSelected($event)" hidden>

                @if (uploadProgress() > 0 && uploadProgress() < 100) {
                  <mat-progress-bar mode="determinate"
                                    [value]="uploadProgress()"
                                    style="width:200px">
                  </mat-progress-bar>
                }
              </div>

              <div class="form-actions">
                <button type="submit" class="btn btn-primary"
                        [disabled]="savingProfile() || profileForm.invalid">
                  @if (savingProfile()) { <div class="spin-sm"></div> }
                  Save Profile
                </button>
              </div>
            </form>
          </div>
        </mat-tab>

        <!-- Theme / Branding Tab -->
        <mat-tab label="Theme & Branding">
          <div class="tab-body">
            <div class="card settings-card">

              <div class="form-section-title">Brand Color</div>
              <p class="section-desc">
                Choose a primary color for the panel. This updates the sidebar, buttons,
                charts and accents throughout the interface.
              </p>

              <!-- Color presets -->
              <div class="color-presets">
                @for (c of colorPresets; track c) {
                  <button class="color-swatch"
                          [style.background]="c"
                          [class.color-swatch--active]="theme.brandColor() === c"
                          (click)="applyColor(c)"
                          [title]="c">
                    @if (theme.brandColor() === c) {
                      <span class="material-icons-round">check</span>
                    }
                  </button>
                }
              </div>

              <!-- Custom hex input -->
              <div class="custom-color-row" [formGroup]="colorForm">
                <div class="color-input-wrap">
                  <input type="color" [value]="theme.brandColor()"
                         (input)="onColorPicker($event)"
                         class="color-native-input">
                  <mat-form-field style="flex:1">
                    <mat-label>Custom Hex Color</mat-label>
                    <input matInput formControlName="hex" placeholder="#2563EB"
                           (input)="onHexInput($event)">
                    <mat-hint>e.g. #2563EB</mat-hint>
                  </mat-form-field>
                </div>
                <button class="btn btn-primary" (click)="saveColor()"
                        [disabled]="savingColor()">
                  Apply Color
                </button>
              </div>

              <!-- Live preview -->
              <div class="color-preview">
                <div class="preview-label">Preview:</div>
                <div class="preview-samples">
                  <div class="preview-btn" [style.background]="theme.brandColor()">Primary Button</div>
                  <span class="badge badge-primary" [style.background]="theme.brandColorLight()"
                        [style.color]="theme.brandColor()">Badge</span>
                  <div class="preview-sidebar-item" [style.background]="theme.brandColorLight()"
                       [style.color]="theme.brandColor()">
                    <span class="material-icons-round" style="font-size:16px">school</span>
                    Active Nav Item
                  </div>
                </div>
              </div>

              <div class="form-section-title" style="margin-top:24px">Display Mode</div>

              <div class="theme-modes">
                <button class="mode-btn" [class.mode-btn--active]="!theme.isDark()"
                        (click)="theme.setTheme('light')">
                  <span class="material-icons-round">light_mode</span>
                  Light Mode
                </button>
                <button class="mode-btn" [class.mode-btn--active]="theme.isDark()"
                        (click)="theme.setTheme('dark')">
                  <span class="material-icons-round">dark_mode</span>
                  Dark Mode
                </button>
              </div>

            </div>
          </div>
        </mat-tab>

        <!-- Security Tab -->
        <mat-tab label="Security">
          <div class="tab-body">
            <div class="card settings-card">
              <div class="form-section-title">Two-Factor Authentication</div>
              <p class="section-desc">
                Protect your account with a time-based one-time password (TOTP)
                using Google Authenticator or similar apps.
              </p>

              @if (!setup2faMode()) {
                <button class="btn btn-primary" (click)="startSetup2fa()">
                  <span class="material-icons-round">security</span>
                  Set Up 2FA
                </button>
              }

              @if (setup2faMode() && qrPayload()) {
                <div class="twofa-setup">
                  <div class="qr-section">
                    <p><strong>1. Scan this QR code</strong> with your authenticator app:</p>
                    <div class="qr-placeholder">
                      <!-- In production: render QR from qrPayload().qrCodeUri using a QR lib -->
                      <div style="background:#000;color:#fff;padding:8px;font-size:11px;font-family:monospace;word-break:break-all;max-width:260px">
                        {{ qrPayload()!.qrCodeUri }}
                      </div>
                    </div>
                    <p style="margin-top:8px;font-size:12px">
                      Or enter manually: <code>{{ qrPayload()!.manualEntryKey }}</code>
                    </p>
                  </div>

                  <p><strong>2. Enter the 6-digit code</strong> to verify and activate:</p>

                  <div class="verify-row" [formGroup]="twoFaForm">
                    <mat-form-field>
                      <mat-label>Verification Code</mat-label>
                      <input matInput formControlName="code" maxlength="6" placeholder="000000">
                    </mat-form-field>
                    <button class="btn btn-primary" (click)="verify2fa()"
                            [disabled]="saving2fa()">
                      Verify & Activate
                    </button>
                  </div>

                  <div class="recovery-codes">
                    <p><strong>Recovery codes</strong> (save these securely):</p>
                    <div class="codes-grid">
                      @for (code of qrPayload()!.recoveryCodes; track code) {
                        <code>{{ code }}</code>
                      }
                    </div>
                  </div>
                </div>
              }
            </div>
          </div>
        </mat-tab>

      </mat-tab-group>
    </div>
  `,
  styles: [`
    .tab-body { padding: 20px 0; }
    .settings-card { padding: 24px; background: var(--color-surface);
                     border: 1px solid var(--color-border); border-radius: var(--radius-lg); }

    .grid-2 {
      display: grid; grid-template-columns: repeat(2, 1fr); gap: 0 16px;
      mat-form-field { width: 100%; }
      .full { grid-column: 1 / -1; }
      @media (max-width: 600px) { grid-template-columns: 1fr; }
    }

    .logo-upload { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }
    .logo-preview {
      width: 100px; height: 80px; border-radius: var(--radius-md);
      background: var(--color-surface-2); border: 2px dashed var(--color-border);
      cursor: pointer; overflow: hidden;
      display: flex; align-items: center; justify-content: center;
      transition: border-color .15s;
      &:hover { border-color: var(--color-primary); }
      img { width: 100%; height: 100%; object-fit: contain; }
    }
    .logo-placeholder {
      display: flex; flex-direction: column; align-items: center;
      gap: 4px; color: var(--color-text-muted);
      .material-icons-round { font-size: 24px; }
      span { font-size: 11px; }
    }

    .form-actions { display: flex; justify-content: flex-end; padding-top: 16px; border-top: 1px solid var(--color-border); }

    /* Color picker */
    .section-desc { font-size: 13px; color: var(--color-text-secondary); margin: 0 0 16px; }

    .color-presets { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 20px; }
    .color-swatch {
      width: 34px; height: 34px; border-radius: 50%; border: none;
      cursor: pointer; display: flex; align-items: center; justify-content: center;
      transition: transform .15s, box-shadow .15s;
      &:hover { transform: scale(1.1); }
      &--active { box-shadow: 0 0 0 3px #fff, 0 0 0 5px currentColor; }
      .material-icons-round { font-size: 16px; color: #fff; }
    }

    .custom-color-row {
      display: flex; align-items: flex-end; gap: 12px; flex-wrap: wrap;
      margin-bottom: 20px;
    }
    .color-input-wrap { display: flex; align-items: center; gap: 8px; flex: 1; }
    .color-native-input {
      width: 44px; height: 44px; border-radius: 8px; border: 1px solid var(--color-border);
      padding: 2px; cursor: pointer; background: none;
    }

    .color-preview { background: var(--color-surface-2); border-radius: var(--radius-sm); padding: 14px; }
    .preview-label { font-size: 11px; font-weight: 600; text-transform: uppercase; color: var(--color-text-secondary); margin-bottom: 10px; }
    .preview-samples { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .preview-btn {
      color: #fff; padding: 6px 14px; border-radius: 6px;
      font-size: 13px; font-weight: 500;
    }
    .preview-sidebar-item {
      display: flex; align-items: center; gap: 6px;
      padding: 6px 12px; border-radius: 6px; font-size: 13px; font-weight: 500;
    }

    .theme-modes { display: flex; gap: 12px; }
    .mode-btn {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 20px; border-radius: var(--radius-sm);
      border: 1.5px solid var(--color-border);
      background: var(--color-surface); cursor: pointer;
      font-size: 13px; font-weight: 500; color: var(--color-text);
      transition: all .15s;
      &--active {
        border-color: var(--color-primary);
        background: var(--color-primary-50);
        color: var(--color-primary);
      }
    }

    /* 2FA */
    .twofa-setup { display: flex; flex-direction: column; gap: 16px; }
    .qr-section { }
    .qr-placeholder { margin-top: 10px; }
    .verify-row { display: flex; align-items: flex-end; gap: 12px;
      mat-form-field { width: 200px; }
    }
    .recovery-codes { }
    .codes-grid {
      display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-top: 8px;
      code { font-family: var(--font-mono); font-size: 12px; background: var(--color-surface-2);
             padding: 4px 8px; border-radius: 4px; text-align: center; }
    }

    .spin-sm {
      width: 14px; height: 14px;
      border: 2px solid rgba(255,255,255,.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin .5s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class SettingsComponent implements OnInit {
  private settingsSvc = inject(SettingsService);
  private http        = inject(HttpClient);
  private toast       = inject(ToastService);
  readonly theme      = inject(ThemeService);
  readonly auth       = inject(AuthService);
  private fb          = inject(FormBuilder);

  savingProfile  = signal(false);
  savingColor    = signal(false);
  saving2fa      = signal(false);
  uploadProgress = signal(0);
  logoPreview    = signal<string | null>(null);
  setup2faMode   = signal(false);
  qrPayload      = signal<any>(null);

  readonly colorPresets = COLOR_PRESETS;

  profileForm = this.fb.group({
    schoolName: [''],
    address:    [''],
    phone:      [''],
    email:      ['', Validators.email],
    website:    ['']
  });

  colorForm = this.fb.group({
    hex: [this.theme.brandColor()]
  });

  twoFaForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]]
  });

  ngOnInit(): void {
    this.loadSettings();
  }

  loadSettings(): void {
    this.settingsSvc.getSettings('Branding').subscribe({
      next: (settings) => {
        const get = (key: string) =>
          settings.find(s => s.settingKey === key)?.settingValue ?? '';

        this.profileForm.patchValue({
          schoolName: get('School.Name'),
          address:    get('School.Address'),
          phone:      get('School.Phone'),
          email:      get('School.Email'),
          website:    get('School.Website')
        });

        const logo = get('School.LogoUrl');
        if (logo) this.logoPreview.set(logo);
      },
      error: () => {}
    });
  }

  saveProfile(): void {
    const v = this.profileForm.value;
    this.savingProfile.set(true);

    const updates = [
      { key: 'School.Name',    value: v.schoolName ?? '' },
      { key: 'School.Address', value: v.address ?? '' },
      { key: 'School.Phone',   value: v.phone ?? '' },
      { key: 'School.Email',   value: v.email ?? '' },
      { key: 'School.Website', value: v.website ?? '' }
    ];

    let completed = 0;
    updates.forEach(u => {
      this.settingsSvc.updateSetting({ key: u.key, value: u.value, category: 'Branding' }).subscribe({
        next: () => {
          completed++;
          if (completed === updates.length) {
            this.savingProfile.set(false);
            this.toast.success('Profile saved');
          }
        },
        error: () => { this.savingProfile.set(false); this.toast.error('Save failed'); }
      });
    });
  }

  onLogoSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = e => this.logoPreview.set(e.target?.result as string);
    reader.readAsDataURL(file);

    // Upload to Azure Blob via API
    const fd = new FormData();
    fd.append('file', file);
    fd.append('folder', 'StudentPhotos'); // reuse the upload endpoint

    this.http.post<any>(`${environment.apiUrl}/upload`, fd, {
      reportProgress: true, observe: 'events'
    }).subscribe({
      next: (event: any) => {
        if (event.type === 1) {
          this.uploadProgress.set(Math.round(event.loaded / event.total * 100));
        } else if (event.type === 4) {
          this.uploadProgress.set(100);
          const url = event.body?.data?.url;
          if (url) {
            this.settingsSvc.updateSetting({ key: 'School.LogoUrl', value: url, category: 'Branding' }).subscribe();
            this.toast.success('Logo uploaded');
          }
        }
      },
      error: () => { this.uploadProgress.set(0); this.toast.error('Logo upload failed'); }
    });
  }

  applyColor(hex: string): void {
    this.theme.setBrandColor(hex);
    this.colorForm.patchValue({ hex });
  }

  onColorPicker(event: Event): void {
    const hex = (event.target as HTMLInputElement).value;
    this.theme.setBrandColor(hex);
    this.colorForm.patchValue({ hex }, { emitEvent: false });
  }

  onHexInput(event: Event): void {
    const hex = (event.target as HTMLInputElement).value;
    if (/^#[0-9A-Fa-f]{6}$/.test(hex)) {
      this.theme.setBrandColor(hex);
    }
  }

  saveColor(): void {
    const hex = this.colorForm.value.hex;
    if (!hex) return;
    this.savingColor.set(true);
    this.settingsSvc.updateSetting({
      key: 'Theme.PrimaryColor', value: hex, category: 'Theme'
    }).subscribe({
      next: () => { this.savingColor.set(false); this.toast.success('Brand color saved'); },
      error: () => { this.savingColor.set(false); this.toast.error('Save failed'); }
    });
  }

  startSetup2fa(): void {
    this.auth.setup2fa().subscribe({
      next: (payload) => { this.qrPayload.set(payload); this.setup2faMode.set(true); },
      error: () => this.toast.error('Failed to generate 2FA setup')
    });
  }

  verify2fa(): void {
    const code = this.twoFaForm.value.code;
    if (!code) return;
    this.saving2fa.set(true);
    this.auth.verifySetup2fa(code).subscribe({
      next: () => {
        this.saving2fa.set(false);
        this.setup2faMode.set(false);
        this.toast.success('2FA activated', 'Your account is now protected');
      },
      error: () => { this.saving2fa.set(false); this.toast.error('Invalid code'); }
    });
  }
}