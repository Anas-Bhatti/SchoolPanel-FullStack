// src/app/features/not-found/not-found.component.ts
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector:   'sp-not-found',
  standalone: true,
  imports:    [RouterLink],
  template: `
    <div class="nf-page" role="main" aria-labelledby="nf-heading">
      <div class="nf-card">

        <!-- Big gradient 404 -->
        <div class="nf-code" aria-hidden="true">404</div>

        <!-- Icon -->
        <div class="nf-icon-wrap" aria-hidden="true">
          <span class="material-icons-round">search_off</span>
        </div>

        <h1 class="nf-heading" id="nf-heading">Page Not Found</h1>

        <p class="nf-desc">
          The page you're looking for doesn't exist or has been moved.
          Check the URL or return to the dashboard.
        </p>

        <div class="nf-actions">
          <a routerLink="/dashboard" class="btn btn-primary"
             aria-label="Return to dashboard">
            <span class="material-icons-round" aria-hidden="true">home</span>
            Go to Dashboard
          </a>
          <button class="btn btn-secondary" type="button"
                  (click)="goBack()" aria-label="Go back to previous page">
            <span class="material-icons-round" aria-hidden="true">arrow_back</span>
            Go Back
          </button>
        </div>

      </div>
    </div>
  `,
  styles: [`
    .nf-page {
      min-height: 100vh;
      display: flex; align-items: center; justify-content: center;
      background: var(--color-bg);
      padding: 40px 20px;
    }

    .nf-card {
      text-align: center;
      max-width: 440px;
      width: 100%;
    }

    /* Gradient 404 number */
    .nf-code {
      font-size: clamp(72px, 18vw, 120px);
      font-weight: 800;
      line-height: 1;
      letter-spacing: -4px;
      background: linear-gradient(135deg, var(--color-primary), var(--color-primary-600));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
      margin-bottom: 8px;
      user-select: none;
    }

    .nf-icon-wrap {
      margin-bottom: 16px;
      .material-icons-round { font-size: 52px; color: var(--color-text-muted); }
    }

    .nf-heading {
      font-size: 24px; font-weight: 700;
      color: var(--color-text); margin: 0 0 12px;
    }

    .nf-desc {
      font-size: 15px; color: var(--color-text-secondary);
      line-height: 1.6; margin: 0 0 28px;
    }

    .nf-actions {
      display: flex; gap: 10px;
      justify-content: center; flex-wrap: wrap;
    }
  `]
})
export class NotFoundComponent {
  goBack(): void { window.history.back(); }
}