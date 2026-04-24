// src/app/shared/components/sidebar/sidebar.component.ts
import {
  Component, inject, input, output, computed, signal
} from '@angular/core';
import { CommonModule }          from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatTooltipModule }      from '@angular/material/tooltip';
import { AuthService }           from '@core/services/auth.service';
import { ThemeService }          from '@core/services/theme.service';
import { environment }           from '@env/environment';

interface NavItem {
  label:      string;
  icon:       string;
  route:      string;
  permission: string;
  exact?:     boolean;
  children?:  { label: string; route: string; exact?: boolean }[];
}

const NAV: NavItem[] = [
  { label: 'Dashboard',  icon: 'dashboard',   route: '/dashboard',  permission: 'Dashboard', exact: true },
  {
    label: 'Students', icon: 'school', route: '/students', permission: 'Students',
    children: [
      { label: 'All Students', route: '/students',     exact: true },
      { label: 'Add Student',  route: '/students/new', exact: true }
    ]
  },
  { label: 'Attendance', icon: 'how_to_reg',  route: '/attendance', permission: 'Students'   },
  {
    label: 'Fees', icon: 'payments', route: '/fees', permission: 'Fees',
    children: [
      { label: 'Fee Dues',       route: '/fees',     exact: true  },
      { label: 'Record Payment', route: '/fees/pay', exact: true  }
    ]
  },
  { label: 'Reports',    icon: 'assessment',  route: '/reports',    permission: 'Reports'    },
  { label: 'Settings',   icon: 'settings',    route: '/settings',   permission: 'Settings'   }
];

@Component({
  selector:   'sp-sidebar',
  standalone: true,
  imports:    [CommonModule, RouterLink, RouterLinkActive, MatTooltipModule],
  template: `
    <aside
      class="sidebar"
      [class.sidebar--collapsed]="collapsed()"
      role="navigation"
      aria-label="Primary navigation">

      <!-- Brand -->
      <div class="sidebar-brand" aria-label="SchoolPanel home">
        <div class="brand-logo" aria-hidden="true">
          <span class="material-icons-round">school</span>
        </div>
        @if (!collapsed()) {
          <div class="brand-words">
            <span class="brand-name">SchoolPanel</span>
            <span class="brand-ver">v{{ version }}</span>
          </div>
        }
      </div>

      <!-- Nav items -->
      <nav class="sidebar-nav" aria-label="Module navigation">
        @for (item of visibleItems(); track item.route) {

          @if (item.children && !collapsed()) {
            <!-- Expandable group -->
            <div class="nav-group">
              <button
                class="nav-item nav-group-btn"
                type="button"
                (click)="toggleGroup(item.route)"
                [attr.aria-expanded]="isOpen(item.route)"
                [attr.aria-controls]="'ng-' + sanitize(item.route)">
                <span class="nav-icon material-icons-round" aria-hidden="true">{{ item.icon }}</span>
                <span class="nav-label">{{ item.label }}</span>
                <span
                  class="nav-chevron material-icons-round"
                  [class.open]="isOpen(item.route)"
                  aria-hidden="true">
                  expand_more
                </span>
              </button>

              @if (isOpen(item.route)) {
                <ul class="nav-children" [id]="'ng-' + sanitize(item.route)" role="list">
                  @for (child of item.children; track child.route) {
                    <li role="listitem">
                      <a
                        class="nav-child"
                        [routerLink]="child.route"
                        routerLinkActive="nav-child--active"
                        [routerLinkActiveOptions]="{ exact: child.exact ?? false }"
                        [attr.aria-current]="isCurrentRoute(child.route) ? 'page' : null">
                        {{ child.label }}
                      </a>
                    </li>
                  }
                </ul>
              }
            </div>

          } @else {
            <!-- Simple item -->
            <a
              class="nav-item"
              [routerLink]="item.route"
              routerLinkActive="nav-item--active"
              [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
              [attr.aria-current]="isCurrentRoute(item.route) ? 'page' : null"
              [matTooltip]="collapsed() ? item.label : ''"
              matTooltipPosition="right"
              matTooltipShowDelay="300">
              <span class="nav-icon material-icons-round" aria-hidden="true">{{ item.icon }}</span>
              @if (!collapsed()) {
                <span class="nav-label">{{ item.label }}</span>
              }
            </a>
          }

        }
      </nav>

      <!-- Footer -->
      <div class="sidebar-footer">
        <button
          class="nav-item"
          type="button"
          (click)="theme.toggleTheme()"
          [attr.aria-label]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
          [matTooltip]="collapsed() ? (theme.isDark() ? 'Light mode' : 'Dark mode') : ''"
          matTooltipPosition="right">
          <span class="nav-icon material-icons-round" aria-hidden="true">
            {{ theme.isDark() ? 'light_mode' : 'dark_mode' }}
          </span>
          @if (!collapsed()) {
            <span class="nav-label">{{ theme.isDark() ? 'Light Mode' : 'Dark Mode' }}</span>
          }
        </button>

        <button
          class="nav-item"
          type="button"
          (click)="toggleCollapse.emit()"
          [attr.aria-label]="collapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
          [matTooltip]="collapsed() ? 'Expand' : 'Collapse'"
          matTooltipPosition="right">
          <span class="nav-icon material-icons-round" aria-hidden="true">
            {{ collapsed() ? 'chevron_right' : 'chevron_left' }}
          </span>
          @if (!collapsed()) {
            <span class="nav-label">Collapse</span>
          }
        </button>
      </div>

    </aside>
  `,
  styles: [`
    :host { display: contents; }

    .sidebar {
      width: var(--sidebar-width);
      min-height: 100vh;
      background: var(--sidebar-bg);
      display: flex; flex-direction: column;
      position: sticky; top: 0;
      z-index: var(--z-sidebar);
      flex-shrink: 0; overflow-x: hidden;
      transition: width var(--transition-base);

      &--collapsed { width: 60px; }
    }

    /* Brand */
    .sidebar-brand {
      height: var(--header-height); flex-shrink: 0;
      display: flex; align-items: center; gap: 10px;
      padding: 0 14px;
      border-bottom: 1px solid rgba(255,255,255,.07);
      overflow: hidden;
      .sidebar--collapsed & { justify-content: center; padding: 0; }
    }

    .brand-logo {
      width: 32px; height: 32px; border-radius: 8px;
      background: var(--color-primary); flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      .material-icons-round { font-size: 18px; color: #fff; }
    }

    .brand-words { overflow: hidden; }
    .brand-name  { display: block; font-size: 14px; font-weight: 700; color: #F1F5F9; white-space: nowrap; }
    .brand-ver   { display: block; font-size: 10px; color: rgba(255,255,255,.35); }

    /* Nav */
    .sidebar-nav {
      flex: 1; padding: 10px 8px;
      display: flex; flex-direction: column; gap: 1px;
      overflow-y: auto; overflow-x: hidden;
      scrollbar-width: thin; scrollbar-color: rgba(255,255,255,.1) transparent;
    }

    .nav-item {
      display: flex; align-items: center; gap: 10px;
      padding: 9px 10px; border-radius: var(--radius-sm);
      color: var(--sidebar-text);
      text-decoration: none;
      font-size: 13px; font-weight: 500;
      background: none; border: none; cursor: pointer;
      width: 100%; white-space: nowrap; overflow: hidden;
      transition: all var(--transition-fast);

      &:hover { background: var(--sidebar-hover-bg); color: #E2E8F0; }

      &:focus-visible {
        outline: 2px solid rgba(147,197,253,.6);
        outline-offset: -2px;
      }

      &--active {
        background: var(--sidebar-active-bg) !important;
        color: var(--sidebar-active-text) !important;
        .nav-icon { opacity: 1; color: #93C5FD; }
      }

      .sidebar--collapsed & { justify-content: center; padding: 10px; }
    }

    .nav-icon {
      font-size: 19px; flex-shrink: 0; opacity: .75;
      .nav-item:hover & { opacity: 1; }
    }

    .nav-label { flex: 1; text-align: left; }

    .nav-chevron {
      font-size: 16px; flex-shrink: 0;
      transition: transform var(--transition-fast);
      &.open { transform: rotate(180deg); }
    }

    /* Group + children */
    .nav-group { }

    .nav-group-btn .nav-label { text-align: left; }

    .nav-children { list-style: none; margin: 2px 0 4px; padding: 0; }

    .nav-child {
      display: block;
      padding: 7px 10px 7px 40px;
      border-radius: var(--radius-sm);
      font-size: 12px; font-weight: 500;
      color: rgba(203,213,225,.65);
      text-decoration: none;
      transition: all var(--transition-fast);

      &:hover { background: var(--sidebar-hover-bg); color: #E2E8F0; }
      &:focus-visible { outline: 2px solid rgba(147,197,253,.6); outline-offset: -2px; }

      &--active { color: #93C5FD !important; background: var(--sidebar-active-bg); }
    }

    /* Footer */
    .sidebar-footer {
      padding: 8px;
      border-top: 1px solid rgba(255,255,255,.07);
      display: flex; flex-direction: column; gap: 1px; flex-shrink: 0;
    }

    /* Collapsed: hide text elements */
    .sidebar--collapsed {
      .nav-label, .nav-chevron, .nav-children, .brand-words { display: none !important; }
    }
  `]
})
export class SidebarComponent {
  readonly auth  = inject(AuthService);
  readonly theme = inject(ThemeService);

  collapsed      = input<boolean>(false);
  toggleCollapse = output<void>();

  readonly version = environment.version;

  private openGroups = signal<Set<string>>(new Set(['/students', '/fees']));

  visibleItems = computed(() =>
    NAV.filter(item =>
      this.auth.isSuperAdmin() || this.auth.canAccess(item.permission)
    )
  );

  toggleGroup(route: string): void {
    this.openGroups.update(s => {
      const next = new Set(s);
      next.has(route) ? next.delete(route) : next.add(route);
      return next;
    });
  }

  isOpen(route: string): boolean { return this.openGroups().has(route); }

  isCurrentRoute(route: string): boolean {
    return location.pathname === route || location.pathname.startsWith(route + '/');
  }

  sanitize(route: string): string { return route.replace(/\//g, '-'); }
}