// ============================================================
// core/services/theme.service.ts
// Manages dark/light mode and custom brand color.
// Writes to CSS custom properties on :root in real-time.
// ============================================================

import { Injectable, signal, computed, effect, inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export type ThemeMode = 'light' | 'dark';

const THEME_KEY = 'sp_theme';
const COLOR_KEY = 'sp_brand_color';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private platformId = inject(PLATFORM_ID);
  private isBrowser  = isPlatformBrowser(this.platformId);

  // ── Signals ───────────────────────────────────────────────
  readonly mode       = signal<ThemeMode>(this.loadTheme());
  readonly brandColor = signal<string>(this.loadBrandColor());
  readonly isDark     = computed(() => this.mode() === 'dark');

  // Derived hex variations of the brand color
  readonly brandColorLight = computed(() => this.lighten(this.brandColor(), 0.88));
  readonly brandColorMid   = computed(() => this.lighten(this.brandColor(), 0.5));
  readonly brandColorDark  = computed(() => this.darken(this.brandColor(), 0.15));

  // ApexCharts theme-aware options (consumed by chart components)
  readonly chartTheme = computed(() => ({
    mode: this.mode() as 'light' | 'dark',
    palette: 'palette1',
    monochrome: {
      enabled:      false,
      color:        this.brandColor(),
      shadeTo:      'light' as const,
      shadeIntensity: 0.65
    }
  }));

  readonly chartForeColor = computed(() =>
    this.isDark() ? '#94A3B8' : '#475569'
  );

  readonly chartGridColor = computed(() =>
    this.isDark() ? '#334155' : '#E2E8F0'
  );

  constructor() {
    // Apply theme whenever signals change
    effect(() => {
      this.applyTheme(this.mode(), this.brandColor());
    });
  }

  // ─── Public API ───────────────────────────────────────────

  toggleTheme(): void {
    this.mode.update(m => m === 'light' ? 'dark' : 'light');
    if (this.isBrowser) {
      localStorage.setItem(THEME_KEY, this.mode());
    }
  }

  setTheme(mode: ThemeMode): void {
    this.mode.set(mode);
    if (this.isBrowser) {
      localStorage.setItem(THEME_KEY, mode);
    }
  }

  setBrandColor(hex: string): void {
    if (!this.isValidHex(hex)) return;
    this.brandColor.set(hex);
    if (this.isBrowser) {
      localStorage.setItem(COLOR_KEY, hex);
    }
  }

  // Called after settings are loaded from API
  applyFromSettings(primaryColor: string): void {
    if (this.isValidHex(primaryColor)) {
      this.setBrandColor(primaryColor);
    }
  }

  // ─── Private ──────────────────────────────────────────────

  private applyTheme(mode: ThemeMode, color: string): void {
    if (!this.isBrowser) return;
    const root = document.documentElement;

    // Theme mode
    root.setAttribute('data-theme', mode);

    // Brand color and derived values
    root.style.setProperty('--color-primary',      color);
    root.style.setProperty('--color-primary-600',  this.darken(color, 0.12));
    root.style.setProperty('--color-primary-700',  this.darken(color, 0.25));
    root.style.setProperty('--color-primary-50',   this.lighten(color, 0.88));
    root.style.setProperty('--color-primary-100',  this.lighten(color, 0.75));

    // RGB values for rgba() usage
    const rgb = this.hexToRgb(color);
    if (rgb) {
      root.style.setProperty('--color-primary-rgb', `${rgb.r},${rgb.g},${rgb.b}`);
    }
  }

  private loadTheme(): ThemeMode {
    if (!this.isBrowser) return 'light';
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'dark' || saved === 'light') return saved;
    // Respect OS preference
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private loadBrandColor(): string {
    if (!this.isBrowser) return '#2563EB';
    return localStorage.getItem(COLOR_KEY) || '#2563EB';
  }

  private isValidHex(hex: string): boolean {
    return /^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$/.test(hex);
  }

  private hexToRgb(hex: string): { r: number; g: number; b: number } | null {
    const match = hex.replace('#', '').match(/.{2}/g);
    if (!match || match.length < 3) return null;
    return {
      r: parseInt(match[0], 16),
      g: parseInt(match[1], 16),
      b: parseInt(match[2], 16)
    };
  }

  private lighten(hex: string, amount: number): string {
    const rgb = this.hexToRgb(hex);
    if (!rgb) return hex;
    const r = Math.round(rgb.r + (255 - rgb.r) * amount);
    const g = Math.round(rgb.g + (255 - rgb.g) * amount);
    const b = Math.round(rgb.b + (255 - rgb.b) * amount);
    return `#${r.toString(16).padStart(2,'0')}${g.toString(16).padStart(2,'0')}${b.toString(16).padStart(2,'0')}`;
  }

  private darken(hex: string, amount: number): string {
    const rgb = this.hexToRgb(hex);
    if (!rgb) return hex;
    const r = Math.round(rgb.r * (1 - amount));
    const g = Math.round(rgb.g * (1 - amount));
    const b = Math.round(rgb.b * (1 - amount));
    return `#${r.toString(16).padStart(2,'0')}${g.toString(16).padStart(2,'0')}${b.toString(16).padStart(2,'0')}`;
  }
}