// src/app/shared/components/notification-bell/notification-bell.component.ts
import {
  Component, inject, signal, computed,
  OnInit, OnDestroy, HostListener
} from '@angular/core';
import { CommonModule, DatePipe }   from '@angular/common';
import { RouterLink }               from '@angular/router';
import { HttpClient, HttpHeaders }  from '@angular/common/http';
import { trigger, transition, style, animate } from '@angular/animations';
import { interval, Subscription }  from 'rxjs';
import { startWith, switchMap }     from 'rxjs/operators';
import { environment }              from '@env/environment';
import type { Notification }        from '@core/models';

// Notification type → icon mapping
const TYPE_ICON: Record<string, string> = {
  Info:    'info',
  Success: 'check_circle',
  Warning: 'warning',
  Danger:  'error'
};

const TYPE_CLASS: Record<string, string> = {
  Info:    'ni--info',
  Success: 'ni--success',
  Warning: 'ni--warning',
  Danger:  'ni--danger'
};

@Component({
  selector: 'sp-notification-bell',
  standalone: true,
  imports: [CommonModule, DatePipe, RouterLink],
  animations: [
    trigger('dropdown', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(-10px) scale(.97)' }),
        animate('180ms ease-out', style({ opacity: 1, transform: 'translateY(0) scale(1)' }))
      ]),
      transition(':leave', [
        animate('130ms ease-in', style({ opacity: 0, transform: 'translateY(-6px)' }))
      ])
    ])
  ],
  template: `
    <div class="bell-wrap" (keydown.escape)="close()">

      <!-- Trigger button -->
      <button
        class="bell-btn"
        type="button"
        (click)="toggle()"
        [attr.aria-expanded]="open()"
        [attr.aria-label]="unreadCount() > 0
          ? 'Notifications — ' + unreadCount() + ' unread'
          : 'Notifications'"
        aria-haspopup="true"
        aria-controls="notif-dropdown">

        <span class="material-icons-round" aria-hidden="true">notifications</span>

        @if (unreadCount() > 0) {
          <span class="bell-badge" aria-hidden="true">
            {{ unreadCount() > 99 ? '99+' : unreadCount() }}
          </span>
        }
      </button>

      <!-- Dropdown -->
      @if (open()) {
        <div
          class="bell-dropdown"
          id="notif-dropdown"
          role="dialog"
          aria-label="Notifications"
          [@dropdown]>

          <!-- Header -->
          <div class="bell-head">
            <span class="bell-title">Notifications</span>
            @if (unreadCount() > 0) {
              <button class="bell-mark-all" type="button" (click)="markAllRead()">
                Mark all read
              </button>
            }
          </div>

          <!-- Body -->
          @if (loading()) {
            <div class="bell-loading" aria-busy="true" aria-label="Loading notifications">
              <div class="bell-spinner"></div>
            </div>
          } @else if (notifications().length === 0) {
            <div class="bell-empty" role="status">
              <span class="material-icons-round" aria-hidden="true">notifications_none</span>
              <p>You're all caught up!</p>
            </div>
          } @else {
            <ul class="bell-list" role="list">
              @for (n of notifications(); track n.notificationId) {
                <li
                  class="bell-item"
                  [class.bell-item--unread]="!n.isRead"
                  role="listitem">

                  <div class="bell-icon-wrap" [class]="typeClass(n.notificationType)" aria-hidden="true">
                    <span class="material-icons-round bell-type-icon">
                      {{ typeIcon(n.notificationType) }}
                    </span>
                  </div>

                  <div class="bell-content">
                    <p class="bell-title-item">{{ n.title }}</p>
                    <p class="bell-msg">{{ n.message }}</p>
                    <time class="bell-time" [dateTime]="n.createdAt">
                      {{ n.createdAt | date:'dd MMM, HH:mm' }}
                    </time>
                  </div>
                </li>
              }
            </ul>
          }

          <!-- Footer -->
          <div class="bell-footer">
            <a
              routerLink="/notifications"
              class="bell-view-all"
              (click)="close()"
              aria-label="View all notifications">
              View all notifications
            </a>
          </div>

        </div>
      }

    </div>
  `,
  styles: [`
    .bell-wrap { position: relative; display: inline-flex; align-items: center; }

    /* ── Button ──────────────────────────────────────────────── */
    .bell-btn {
      position: relative;
      width: 36px; height: 36px; border-radius: 8px;
      border: none; background: transparent;
      display: flex; align-items: center; justify-content: center;
      cursor: pointer; color: var(--color-text-secondary);
      transition: background var(--transition-fast), color var(--transition-fast);

      &:hover { background: var(--color-surface-2); color: var(--color-text); }
      &:focus-visible {
        outline: 2px solid var(--color-primary); outline-offset: 2px;
        color: var(--color-text);
      }

      .material-icons-round { font-size: 20px; pointer-events: none; }
    }

    .bell-badge {
      position: absolute; top: 1px; right: 1px;
      min-width: 17px; height: 17px;
      background: var(--color-danger); color: #fff;
      border-radius: 10px;
      font-size: 9px; font-weight: 700; line-height: 17px; text-align: center;
      padding: 0 4px;
      border: 2px solid var(--header-bg);
      pointer-events: none;
    }

    /* ── Dropdown ────────────────────────────────────────────── */
    .bell-dropdown {
      position: absolute;
      top: calc(100% + 10px); right: 0;
      width: 360px; max-width: calc(100vw - 24px);
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-lg);
      z-index: var(--z-modal);
      overflow: hidden;
    }

    .bell-head {
      display: flex; align-items: center; justify-content: space-between;
      padding: 14px 16px 12px;
      border-bottom: 1px solid var(--color-border);
    }

    .bell-title { font-size: 14px; font-weight: 600; }

    .bell-mark-all {
      font-size: 12px; color: var(--color-primary);
      background: none; border: none; cursor: pointer; padding: 0;
      &:hover { text-decoration: underline; }
      &:focus-visible { outline: 2px solid var(--color-primary); border-radius: 2px; }
    }

    .bell-loading {
      display: flex; align-items: center; justify-content: center; padding: 32px;
    }

    .bell-spinner {
      width: 24px; height: 24px;
      border: 2px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: spin .65s linear infinite;
    }

    .bell-empty {
      display: flex; flex-direction: column; align-items: center;
      padding: 40px 16px; gap: 8px; color: var(--color-text-muted);
      .material-icons-round { font-size: 38px; }
      p { font-size: 13px; margin: 0; }
    }

    /* ── List ────────────────────────────────────────────────── */
    .bell-list {
      list-style: none; margin: 0; padding: 4px 0;
      max-height: 340px; overflow-y: auto;
    }

    .bell-item {
      display: flex; align-items: flex-start; gap: 10px;
      padding: 12px 14px;
      border-bottom: 1px solid var(--color-border-light);
      transition: background var(--transition-fast);

      &:last-child { border-bottom: none; }
      &:hover { background: var(--color-surface-2); }

      &--unread { background: var(--color-primary-50); }
    }

    .bell-icon-wrap {
      width: 30px; height: 30px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0; margin-top: 1px;
      background: var(--color-surface-2);

      &.ni--info    { background: var(--color-primary-50);  }
      &.ni--success { background: var(--color-success-bg);  }
      &.ni--warning { background: var(--color-warning-bg);  }
      &.ni--danger  { background: var(--color-danger-bg);   }
    }

    .bell-type-icon {
      font-size: 16px;
      color: var(--color-text-muted);
      .ni--info    & { color: var(--color-primary); }
      .ni--success & { color: var(--color-success); }
      .ni--warning & { color: var(--color-warning); }
      .ni--danger  & { color: var(--color-danger);  }
    }

    .bell-content { flex: 1; min-width: 0; }

    .bell-title-item {
      font-size: 13px; font-weight: 600;
      color: var(--color-text); margin: 0 0 2px;
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    }

    .bell-msg {
      font-size: 12px; color: var(--color-text-secondary);
      margin: 0 0 4px; line-height: 1.4;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .bell-time { font-size: 11px; color: var(--color-text-muted); }

    /* ── Footer ──────────────────────────────────────────────── */
    .bell-footer {
      padding: 10px 14px;
      border-top: 1px solid var(--color-border);
      text-align: center;
    }

    .bell-view-all {
      font-size: 12px; font-weight: 500; color: var(--color-primary);
      text-decoration: none;
      &:hover { text-decoration: underline; }
      &:focus-visible { outline: 2px solid var(--color-primary); border-radius: 2px; }
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class NotificationBellComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);

  open          = signal(false);
  loading       = signal(false);
  notifications = signal<Notification[]>([]);

  unreadCount = computed(() => this.notifications().filter(n => !n.isRead).length);

  private pollSub?: Subscription;
  private readonly api = `${environment.apiUrl}/notifications`;

  ngOnInit(): void {
    // Poll every 90 s with the silent header so it doesn't trigger the loading bar
    this.pollSub = interval(90_000).pipe(
      startWith(0),
      switchMap(() =>
        this.http.get<any>(`${this.api}?page=1&pageSize=10`, {
          headers: new HttpHeaders({ 'X-Silent': '1' })
        })
      )
    ).subscribe({
      next: (data: any) => {
        const items: Notification[] = data?.notifications ?? data?.items ?? [];
        this.notifications.set(items.slice(0, 10));
      },
      error: () => {}
    });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  toggle(): void {
    if (!this.open()) this.fetchLatest();
    this.open.update(v => !v);
  }

  close(): void { this.open.set(false); }

  markAllRead(): void {
    this.notifications.update(list => list.map(n => ({ ...n, isRead: true })));
  }

  typeIcon(type: string):  string { return TYPE_ICON[type]  ?? 'info'; }
  typeClass(type: string): string { return TYPE_CLASS[type] ?? 'ni--info'; }

  private fetchLatest(): void {
    this.loading.set(true);
    this.http.get<any>(`${this.api}?page=1&pageSize=10`, {
      headers: new HttpHeaders({ 'X-Silent': '1' })
    }).subscribe({
      next: (data: any) => {
        const items: Notification[] = data?.notifications ?? data?.items ?? [];
        this.notifications.set(items.slice(0, 10));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  /* Close dropdown on outside click */
  @HostListener('document:click', ['$event'])
  onDocumentClick(e: MouseEvent): void {
    if (!(e.target as HTMLElement).closest('sp-notification-bell')) {
      this.open.set(false);
    }
  }
}