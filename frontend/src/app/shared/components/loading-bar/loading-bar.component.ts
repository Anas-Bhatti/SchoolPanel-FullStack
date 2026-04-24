// src/app/shared/components/loading-bar/loading-bar.component.ts
import {
  Injectable, signal, computed,
  Component, inject
} from '@angular/core';
import { CommonModule }  from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';

// ── Service ─────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class LoadingService {
  private _count = signal(0);

  /** True while any HTTP request is in-flight */
  readonly loading = computed(() => this._count() > 0);

  increment(): void { this._count.update(n => n + 1); }
  decrement(): void { this._count.update(n => Math.max(0, n - 1)); }
  reset():     void { this._count.set(0); }
}

// ── Component ────────────────────────────────────────────────

@Component({
  selector:   'sp-loading-bar',
  standalone: true,
  imports:    [CommonModule],
  animations: [
    trigger('bar', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease', style({ opacity: 1 }))
      ]),
      transition(':leave', [
        animate('300ms ease', style({ opacity: 0 }))
      ])
    ])
  ],
  template: `
    @if (svc.loading()) {
      <div
        class="loading-bar"
        [@bar]
        role="progressbar"
        aria-label="Loading"
        aria-busy="true"
        aria-valuemin="0"
        aria-valuemax="100">
        <div class="loading-bar-fill"></div>
      </div>
    }
  `,
  styles: [`
    .loading-bar {
      position: fixed;
      top: 0; left: 0; right: 0;
      height: 3px;
      z-index: 9999;
      background: rgba(var(--color-primary-rgb, 37,99,235), .15);
      overflow: hidden;
      pointer-events: none;
    }

    .loading-bar-fill {
      height: 100%;
      background: var(--color-primary);
      box-shadow: 0 0 8px rgba(var(--color-primary-rgb, 37,99,235), .7);
      animation: sp-loading-slide 1.6s ease-in-out infinite;
    }

    @keyframes sp-loading-slide {
      0%   { transform: translateX(-100%) scaleX(.4); }
      60%  { transform: translateX(50%)   scaleX(.9); }
      100% { transform: translateX(110%)  scaleX(.4); }
    }
  `]
})
export class LoadingBarComponent {
  readonly svc = inject(LoadingService);
}