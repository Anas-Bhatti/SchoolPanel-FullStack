// src/app/app-shell.component.ts
import {
  Component, signal, inject, OnInit, OnDestroy,
  HostListener, ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule }   from '@angular/common';
import { RouterOutlet, ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { Title }          from '@angular/platform-browser';
import { filter, Subscription } from 'rxjs';
import { HeaderComponent }      from './shared/components/header/header.component';
import { SidebarComponent }     from './shared/components/sidebar/sidebar.component';
import { ToastComponent }       from './shared/components/toast/toast.component';
import { LoadingBarComponent }  from './shared/components/loading-bar/loading-bar.component';
import { ThemeService }         from './core/services/theme.service';
import { SettingsService }      from './core/services/api.services';

const SIDEBAR_PREF_KEY = 'sp_sidebar_collapsed';

@Component({
  selector:    'sp-shell',
  standalone:  true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterOutlet,
    HeaderComponent, SidebarComponent,
    ToastComponent, LoadingBarComponent
  ],
  template: `
    <!-- Global HTTP progress bar -->
    <sp-loading-bar />

    <!-- Shell layout -->
    <div
      class="shell"
      [class.shell--sidebar-collapsed]="collapsed()"
      [class.shell--mobile-open]="mobileOpen()">

      <!-- Sidebar -->
      <sp-sidebar
        [collapsed]="collapsed()"
        (toggleCollapse)="toggleDesktop()" />

      <!-- Mobile overlay -->
      @if (mobileOpen()) {
        <div
          class="mobile-overlay"
          role="presentation"
          aria-hidden="true"
          (click)="closeMobile()">
        </div>
      }

      <!-- Main -->
      <div class="shell-body">
        <sp-header
          [title]="pageTitle()"
          [icon]="pageIcon()"
          (toggleSidebar)="onMenuClick()" />

        <main
          class="shell-content"
          id="main-content"
          tabindex="-1"
          [attr.aria-label]="pageTitle() + ' page'">
          <router-outlet />
        </main>
      </div>

    </div>

    <!-- Toast notifications -->
    <sp-toast />
  `,
  styles: [`
    .shell {
      display: flex;
      min-height: 100vh;
    }

    .shell-body {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      transition: all var(--transition-base);
    }

    .shell-content {
      flex: 1;
      padding: 24px;
      background: var(--color-bg);
      transition: background var(--transition-base), padding var(--transition-base);
      outline: none;

      @media (max-width: 768px) { padding: 16px 14px; }
    }

    /* ── Mobile sidebar ────────────────────────────────── */
    @media (max-width: 768px) {
      sp-sidebar {
        position: fixed !important;
        inset-block: 0;
        left: 0;
        z-index: var(--z-sidebar);
        transform: translateX(-100%);
        transition: transform var(--transition-base);
      }

      .shell--mobile-open sp-sidebar {
        transform: translateX(0) !important;
      }

      .mobile-overlay {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, .50);
        backdrop-filter: blur(2px);
        -webkit-backdrop-filter: blur(2px);
        z-index: calc(var(--z-sidebar) - 1);
        cursor: pointer;
      }
    }
  `]
})
export class AppShellComponent implements OnInit, OnDestroy {
  private router   = inject(Router);
  private route    = inject(ActivatedRoute);
  private title    = inject(Title);
  private theme    = inject(ThemeService);
  private settings = inject(SettingsService);

  collapsed  = signal<boolean>(this.readPref());
  mobileOpen = signal<boolean>(false);
  pageTitle  = signal<string>('Dashboard');
  pageIcon   = signal<string>('dashboard');

  private schoolName = 'SchoolPanel';
  private subs       = new Subscription();

  ngOnInit(): void {
    this.loadSettings();
    this.trackRouteTitle();
    this.checkMobile();
  }

  ngOnDestroy(): void { this.subs.unsubscribe(); }

  /* ── Sidebar toggles ─────────────────────────────────── */

  toggleDesktop(): void {
    this.collapsed.update(v => !v);
    try { localStorage.setItem(SIDEBAR_PREF_KEY, String(this.collapsed())); } catch {}
  }

  onMenuClick(): void {
    window.innerWidth <= 768
      ? this.mobileOpen.update(v => !v)
      : this.toggleDesktop();
  }

  closeMobile(): void { this.mobileOpen.set(false); }

  /* ── Route tracking ──────────────────────────────────── */

  private trackRouteTitle(): void {
    const sub = this.router.events.pipe(
      filter(e => e instanceof NavigationEnd)
    ).subscribe(() => {
      let child = this.route.firstChild;
      while (child?.firstChild) child = child.firstChild;

      const data = child?.snapshot.data ?? {};
      const pt   = (data['title'] as string) ?? '';
      const pi   = (data['icon']  as string) ?? 'dashboard';

      this.pageTitle.set(pt);
      this.pageIcon.set(pi);
      this.title.setTitle(
        pt ? `${pt} — ${this.schoolName}` : this.schoolName
      );

      // Close mobile sidebar on navigation
      this.mobileOpen.set(false);
    });

    this.subs.add(sub);
  }

  /* ── Settings load (brand color + school name) ────────── */

  private loadSettings(): void {
    this.settings.getSettings().subscribe({
      next: (items) => {
        for (const s of items) {
          if (s.settingKey === 'Theme.PrimaryColor' && s.settingValue) {
            this.theme.applyFromSettings(s.settingValue);
          }
          if (s.settingKey === 'School.Name' && s.settingValue) {
            this.schoolName = s.settingValue;
            // Update meta theme-color to match brand
            const meta = document.getElementById('theme-color-meta') as HTMLMetaElement;
            if (meta) meta.content = s.settingValue;
          }
        }
      },
      error: () => {}
    });
  }

  /* ── Responsive ──────────────────────────────────────── */

  @HostListener('window:resize')
  checkMobile(): void {
    if (window.innerWidth <= 768) this.mobileOpen.set(false);
  }

  private readPref(): boolean {
    try { return localStorage.getItem(SIDEBAR_PREF_KEY) === 'true'; }
    catch { return false; }
  }
}