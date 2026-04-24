// src/app/shared/components/toast/toast.component.ts
import { Component, inject } from '@angular/core';
import { CommonModule }      from '@angular/common';
import {
  trigger, transition, style, animate, keyframes
} from '@angular/animations';
import { ToastService } from '@core/services/api.services';

const ICONS: Record<string, string> = {
  success: 'check_circle',
  error:   'error',
  warning: 'warning',
  info:    'info'
};

@Component({
  selector:   'sp-toast',
  standalone: true,
  imports:    [CommonModule],
  animations: [
    trigger('toast', [
      transition(':enter', [
        animate(
          '280ms cubic-bezier(.21,.72,.37,.99)',
          keyframes([
            style({ opacity: 0, transform: 'translateX(calc(100% + 24px))', offset: 0 }),
            style({ opacity: 1, transform: 'translateX(-4px)',               offset: .7 }),
            style({ opacity: 1, transform: 'translateX(0)',                  offset: 1  })
          ])
        )
      ]),
      transition(':leave', [
        animate(
          '180ms ease-in',
          style({ opacity: 0, transform: 'translateX(calc(100% + 24px))' })
        )
      ])
    ])
  ],
  template: `
    <div
      class="toast-container"
      role="region"
      aria-label="Notifications"
      aria-live="polite"
      aria-relevant="additions">

      @for (t of svc.toasts(); track t.id) {
        <div
          class="toast toast--{{ t.type }}"
          [@toast]
          role="alert"
          aria-atomic="true">

          <!-- Left colour stripe icon -->
          <div class="t-icon" aria-hidden="true">
            <span class="material-icons-round">{{ icons[t.type] }}</span>
          </div>

          <!-- Text -->
          <div class="t-body">
            <p class="t-title">{{ t.title }}</p>
            @if (t.message) {
              <p class="t-msg">{{ t.message }}</p>
            }
          </div>

          <!-- Dismiss button -->
          <button
            class="t-close"
            type="button"
            (click)="svc.dismiss(t.id)"
            [attr.aria-label]="'Dismiss: ' + t.title">
            <span class="material-icons-round" aria-hidden="true">close</span>
          </button>

        </div>
      }

    </div>
  `,
  styles: [`
    /* Container: right-aligned column, above everything */
    .toast-container {
      position: fixed;
      top: 18px;
      right: 18px;
      z-index: var(--z-toast);
      display: flex;
      flex-direction: column;
      gap: 10px;
      width: min(380px, calc(100vw - 36px));
      /* Container itself is pointer-events:none so stacking is clean */
      pointer-events: none;
    }

    /* Each toast */
    .toast {
      display: flex;
      align-items: flex-start;
      gap: 11px;
      padding: 12px 12px 12px 14px;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-left: 4px solid transparent;
      border-radius: var(--radius-md);
      box-shadow: var(--shadow-lg);
      pointer-events: all;  /* Restore clicks for the item */

      &--success { border-left-color: var(--color-success); }
      &--error   { border-left-color: var(--color-danger);  }
      &--warning { border-left-color: var(--color-warning); }
      &--info    { border-left-color: var(--color-primary); }
    }

    /* Icon */
    .t-icon {
      flex-shrink: 0;
      padding-top: 1px;

      .material-icons-round { font-size: 20px; }

      .toast--success & .material-icons-round { color: var(--color-success); }
      .toast--error   & .material-icons-round { color: var(--color-danger);  }
      .toast--warning & .material-icons-round { color: var(--color-warning); }
      .toast--info    & .material-icons-round { color: var(--color-primary); }
    }

    /* Text */
    .t-body { flex: 1; min-width: 0; }

    .t-title {
      font-size: 13px;
      font-weight: 600;
      color: var(--color-text);
      margin: 0 0 2px;
      line-height: 1.3;
    }

    .t-msg {
      font-size: 12px;
      color: var(--color-text-secondary);
      margin: 0;
      line-height: 1.45;
    }

    /* Close button */
    .t-close {
      flex-shrink: 0;
      align-self: flex-start;
      background: none;
      border: none;
      cursor: pointer;
      color: var(--color-text-muted);
      padding: 2px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: color var(--transition-fast), background var(--transition-fast);
      margin-top: -1px;

      .material-icons-round { font-size: 15px; }

      &:hover { color: var(--color-text); background: var(--color-surface-2); }
      &:focus-visible { outline: 2px solid var(--color-primary); }
    }
  `]
})
export class ToastComponent {
  readonly svc   = inject(ToastService);
  readonly icons = ICONS;
}