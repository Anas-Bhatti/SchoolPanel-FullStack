// src/app/shared/components/header/header.component.ts
import {
  Component, inject, input, output, computed
} from '@angular/core';
import { CommonModule }      from '@angular/common';
import { RouterLink }        from '@angular/router';
import { MatMenuModule }     from '@angular/material/menu';
import { MatTooltipModule }  from '@angular/material/tooltip';
import { AuthService }       from '@core/services/auth.service';
import { ThemeService }      from '@core/services/theme.service';
import { NotificationBellComponent } from '@shared/components/notification-bell/notification-bell.component';

@Component({
  selector:   'sp-header',
  standalone: true,
  imports: [
    CommonModule, RouterLink,
    MatMenuModule, MatTooltipModule,
    NotificationBellComponent
  ],
  template: `
    <header class="header" role="banner">

      <!-- ── Left ─────────────────────────────────────────── -->
      <div class="header-left">
        <button
          class="icon-btn"
          type="button"
          (click)="toggleSidebar.emit()"
          aria-label="Toggle navigation sidebar"
          [matTooltip]="'Toggle sidebar'"
          matTooltipShowDelay="600">
          <span class="material-icons-round" aria-hidden="true">menu</span>
        </button>

        @if (title()) {
          <div class="page-title">
            <span class="material-icons-round page-icon" aria-hidden="true">
              {{ icon() }}
            </span>
            <h1>{{ title() }}</h1>
          </div>
        }
      </div>

      <!-- ── Right ────────────────────────────────────────── -->
      <div class="header-right" role="toolbar" aria-label="Header actions">

        <!-- Theme toggle -->
        <button
          class="icon-btn"
          type="button"
          (click)="theme.toggleTheme()"
          [attr.aria-label]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
          [matTooltip]="theme.isDark() ? 'Light mode' : 'Dark mode'"
          matTooltipShowDelay="600">
          <span class="material-icons-round" aria-hidden="true">
            {{ theme.isDark() ? 'light_mode' : 'dark_mode' }}
          </span>
        </button>

        <!-- Notification bell -->
        <sp-notification-bell />

        <!-- Profile button + menu -->
        <button
          class="profile-btn"
          type="button"
          [matMenuTriggerFor]="profileMenu"
          [attr.aria-label]="'User menu — ' + (auth.currentUser()?.fullName ?? 'User')"
          aria-haspopup="menu">

          <div class="avatar" aria-hidden="true">{{ initials() }}</div>

          <div class="profile-text" aria-hidden="true">
            <span class="profile-name">{{ auth.currentUser()?.fullName }}</span>
            <span class="profile-role">{{ primaryRole() }}</span>
          </div>

          <span class="material-icons-round chevron" aria-hidden="true">
            arrow_drop_down
          </span>
        </button>

        <mat-menu #profileMenu="matMenu" xPosition="before">
          <!-- User identity header (non-interactive) -->
          <div class="menu-identity" (click)="$event.stopPropagation()" role="banner">
            <div class="menu-avatar">{{ initials() }}</div>
            <div>
              <p class="menu-name">{{ auth.currentUser()?.fullName }}</p>
              <p class="menu-email">{{ auth.currentUser()?.email }}</p>
            </div>
          </div>

          <div class="menu-divider" role="separator"></div>

          <button mat-menu-item routerLink="/settings" aria-label="Open settings">
            <span class="material-icons-round mi" aria-hidden="true">settings</span>
            Settings
          </button>

          <button mat-menu-item routerLink="/settings" aria-label="Manage two-factor authentication">
            <span class="material-icons-round mi" aria-hidden="true">security</span>
            Two-Factor Auth
          </button>

          <div class="menu-divider" role="separator"></div>

          <button
            mat-menu-item
            class="logout-item"
            (click)="auth.logout()"
            aria-label="Sign out of SchoolPanel">
            <span class="material-icons-round mi" aria-hidden="true">logout</span>
            Sign Out
          </button>
        </mat-menu>

      </div>
    </header>
  `,
  styles: [`
    .header {
      height: var(--header-height);
      background: var(--header-bg);
      border-bottom: 1px solid var(--header-border);
      display: flex; align-items: center; justify-content: space-between;
      padding: 0 16px 0 12px;
      position: sticky; top: 0; z-index: var(--z-header);
      gap: 12px;
      transition: background var(--transition-base), border-color var(--transition-base);
    }

    .header-left {
      display: flex; align-items: center; gap: 8px;
      flex: 1; min-width: 0;
    }

    .page-title {
      display: flex; align-items: center; gap: 6px; min-width: 0;
      h1 {
        font-size: 16px; font-weight: 600; color: var(--color-text); margin: 0;
        overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
      }
    }

    .page-icon { color: var(--color-primary); font-size: 18px; flex-shrink: 0; }

    .header-right { display: flex; align-items: center; gap: 2px; flex-shrink: 0; }

    /* Icon button reusable */
    .icon-btn {
      width: 36px; height: 36px; border-radius: 8px; border: none;
      background: transparent; cursor: pointer;
      color: var(--color-text-secondary);
      display: flex; align-items: center; justify-content: center;
      transition: background var(--transition-fast), color var(--transition-fast);

      .material-icons-round { font-size: 20px; pointer-events: none; }

      &:hover { background: var(--color-surface-2); color: var(--color-text); }
      &:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; }
    }

    /* Profile button */
    .profile-btn {
      display: flex; align-items: center; gap: 8px;
      padding: 4px 6px 4px 4px; border-radius: 8px;
      background: none; border: none; cursor: pointer;
      color: var(--color-text);
      transition: background var(--transition-fast);
      &:hover { background: var(--color-surface-2); }
      &:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; }
    }

    .avatar {
      width: 30px; height: 30px; border-radius: 50%;
      background: var(--color-primary); color: #fff;
      font-size: 11px; font-weight: 700;
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0; user-select: none;
    }

    .profile-text {
      display: flex; flex-direction: column; text-align: left;
      @media (max-width: 640px) { display: none; }
    }

    .profile-name { font-size: 12px; font-weight: 600; line-height: 1.2; max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .profile-role { font-size: 10px; color: var(--color-text-secondary); }
    .chevron      { font-size: 18px; color: var(--color-text-secondary); }

    /* ── Mat menu contents ───────────────────────────────── */
    .menu-identity {
      display: flex; align-items: center; gap: 10px;
      padding: 12px 14px; cursor: default;
    }

    .menu-avatar {
      width: 36px; height: 36px; border-radius: 50%;
      background: var(--color-primary); color: #fff;
      font-size: 13px; font-weight: 700;
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0;
    }

    .menu-name  { font-size: 13px; font-weight: 600; margin: 0 0 1px; color: var(--color-text); }
    .menu-email { font-size: 11px; color: var(--color-text-secondary); margin: 0; max-width: 180px; overflow: hidden; text-overflow: ellipsis; }

    .menu-divider { height: 1px; background: var(--color-border); margin: 4px 0; }

    .mi { font-size: 18px; margin-right: 10px; color: var(--color-text-secondary); vertical-align: middle; }

    .logout-item {
      color: var(--color-danger) !important;
      .mi { color: var(--color-danger) !important; }
    }
  `]
})
export class HeaderComponent {
  readonly auth  = inject(AuthService);
  readonly theme = inject(ThemeService);

  title         = input<string>('');
  icon          = input<string>('dashboard');
  toggleSidebar = output<void>();

  initials = computed(() => {
    const name = this.auth.currentUser()?.fullName ?? '';
    return name.split(' ').slice(0, 2).map(n => n[0]?.toUpperCase() ?? '').join('') || 'U';
  });

  primaryRole = computed(() => this.auth.currentUser()?.roles?.[0] ?? 'User');
}