// ============================================================
// features/dashboard/dashboard.component.ts
// ============================================================
import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NgApexchartsModule } from 'ng-apexcharts';
import type {
  ApexChart, ApexAxisChartSeries, ApexXAxis, ApexYAxis,
  ApexTooltip, ApexDataLabels, ApexStroke, ApexFill,
  ApexLegend, ApexNonAxisChartSeries, ApexPlotOptions, ApexGrid
} from 'ng-apexcharts';
import { DashboardService } from '@core/services/api.services';
import { ThemeService } from '@core/services/theme.service';
import type { DashboardStats } from '@core/models';

@Component({
  selector: 'sp-dashboard',
  standalone: true,
  imports: [CommonModule, DecimalPipe, RouterLink, NgApexchartsModule],
  template: `
    <div class="dashboard">

      <!-- Stat Cards -->
      <div class="stat-grid">
        @for (card of statCards(); track card.label) {
          <div class="stat-card" [class]="'stat-card--' + card.color">
            <div class="stat-inner">
              <div class="stat-body">
                <p class="stat-label">{{ card.label }}</p>
                <p class="stat-value">{{ card.value }}</p>
                @if (card.sub) { <p class="stat-sub">{{ card.sub }}</p> }
              </div>
              <div class="stat-icon">
                <span class="material-icons-round">{{ card.icon }}</span>
              </div>
            </div>
          </div>
        }
      </div>

      <!-- Chart row -->
      <div class="chart-row">

        <!-- Fee Trend area chart -->
        <div class="card chart-card chart-wide">
          <div class="chart-head">
            <div>
              <h3>Fee Collection Trend</h3>
              <p>Last 6 months — collected vs pending</p>
            </div>
            <a routerLink="/fees" class="btn btn-ghost btn-sm">
              View all <span class="material-icons-round">arrow_forward</span>
            </a>
          </div>
          @if (loading()) {
            <div class="loading-overlay"><div class="spinner"></div></div>
          } @else {
            <apx-chart
              [series]="feeChartSeries()"
              [chart]="feeChart"
              [xaxis]="feeXAxis"
              [yaxis]="feeYAxis"
              [stroke]="feeStroke"
              [fill]="feeFill"
              [dataLabels]="noDataLabels"
              [tooltip]="sharedTooltip"
              [legend]="topLegend"
              [grid]="chartGrid()"
              [theme]="theme.chartTheme()"
              [colors]="feeColors()"
            />
          }
        </div>

        <!-- Attendance donut -->
        <div class="card chart-card" style="position:relative">
          <div class="chart-head">
            <div>
              <h3>Today's Attendance</h3>
              <p>School-wide snapshot</p>
            </div>
          </div>
          @if (loading()) {
            <div class="loading-overlay"><div class="spinner"></div></div>
          } @else {
            <apx-chart
              [series]="attSeries()"
              [chart]="attChart"
              [labels]="attLabels"
              [plotOptions]="attPlot"
              [dataLabels]="noDataLabels"
              [legend]="bottomLegend"
              [colors]="attColors()"
              [tooltip]="attTooltip"
              [theme]="theme.chartTheme()"
            />
            <div class="donut-center">
              <span class="donut-pct">{{ attPct() }}%</span>
              <span class="donut-lbl">Present</span>
            </div>
          }
        </div>

      </div>

      <!-- Bottom row -->
      <div class="bottom-row">

        <!-- Class breakdown table -->
        <div class="card">
          <div class="card-hd"><h3>Class Attendance Today</h3></div>
          @if (loading()) {
            <div class="loading-overlay"><div class="spinner"></div></div>
          } @else if (stats()?.attendanceSnapshot?.length) {
            <table class="sp-table">
              <thead>
                <tr>
                  <th>Class</th><th>Total</th>
                  <th>Present</th><th>Absent</th><th>Rate</th>
                </tr>
              </thead>
              <tbody>
                @for (r of stats()!.attendanceSnapshot; track r.className + r.section) {
                  <tr>
                    <td><strong>{{ r.className }}</strong> {{ r.section }}</td>
                    <td>{{ r.total }}</td>
                    <td><span class="badge badge-success">{{ r.present }}</span></td>
                    <td>
                      <span class="badge" [class]="r.absent > 0 ? 'badge-danger' : 'badge-muted'">
                        {{ r.absent }}
                      </span>
                    </td>
                    <td>
                      <div class="bar-wrap">
                        <div class="bar-fill"
                             [style.width.%]="r.total ? (r.present/r.total*100) : 0"></div>
                        <span>{{ r.total ? (r.present/r.total*100 | number:'1.0-0') : 0 }}%</span>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <div class="empty-state">
              <span class="material-icons-round">event_busy</span>
              <h3>No data yet</h3>
              <p>Attendance has not been marked today</p>
            </div>
          }
        </div>

        <!-- Quick actions -->
        <div class="card qa-card">
          <div class="card-hd"><h3>Quick Actions</h3></div>
          <div class="qa-list">
            @for (a of quickActions; track a.label) {
              <a [routerLink]="a.route" class="qa-item">
                <div class="qa-ico">
                  <span class="material-icons-round">{{ a.icon }}</span>
                </div>
                <span>{{ a.label }}</span>
                <span class="material-icons-round qa-arrow">chevron_right</span>
              </a>
            }
          </div>
        </div>

      </div>
    </div>
  `,
  styles: [`
    .dashboard { display: flex; flex-direction: column; gap: 20px; }

    /* Cards */
    .stat-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      @media (max-width: 1100px) { grid-template-columns: repeat(2, 1fr); }
      @media (max-width: 640px)  { grid-template-columns: 1fr; }
    }

    .stat-card {
      border-radius: var(--radius-md);
      border: 1px solid var(--color-border);
      overflow: hidden;
      transition: transform .15s, box-shadow .15s;
      cursor: default;

      &:hover { transform: translateY(-2px); box-shadow: var(--shadow-md); }

      &--blue   { border-top: 3px solid var(--color-primary); }
      &--green  { border-top: 3px solid var(--color-success); }
      &--orange { border-top: 3px solid var(--color-warning); }
      &--red    { border-top: 3px solid var(--color-danger);  }
    }

    .stat-inner {
      background: var(--color-surface);
      padding: 18px 20px;
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
    }

    .stat-label {
      font-size: 11px; font-weight: 600;
      text-transform: uppercase; letter-spacing: .06em;
      color: var(--color-text-secondary); margin: 0 0 6px;
    }

    .stat-value {
      font-size: 24px; font-weight: 700;
      color: var(--color-text); margin: 0 0 3px; line-height: 1.1;
    }

    .stat-sub { font-size: 12px; color: var(--color-text-muted); margin: 0; }

    .stat-icon {
      width: 42px; height: 42px; border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0;
      background: var(--color-primary-50);

      .stat-card--green  & { background: var(--color-success-bg); }
      .stat-card--orange & { background: var(--color-warning-bg); }
      .stat-card--red    & { background: var(--color-danger-bg);  }

      .material-icons-round {
        font-size: 20px; color: var(--color-primary);
        .stat-card--green  & { color: var(--color-success); }
        .stat-card--orange & { color: var(--color-warning); }
        .stat-card--red    & { color: var(--color-danger);  }
      }
    }

    /* Charts */
    .chart-row {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 16px;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .chart-card {
      padding: 20px;
      min-height: 290px;
      display: flex;
      flex-direction: column;
    }

    .chart-head {
      display: flex; align-items: flex-start;
      justify-content: space-between; margin-bottom: 12px;
      h3 { font-size: 14px; font-weight: 600; margin: 0 0 2px; }
      p  { font-size: 12px; color: var(--color-text-secondary); margin: 0; }
    }

    .donut-center {
      position: absolute; top: 54%; left: 50%;
      transform: translate(-50%, -50%);
      text-align: center; pointer-events: none;
      .donut-pct  { display: block; font-size: 22px; font-weight: 700; }
      .donut-lbl  { display: block; font-size: 11px; color: var(--color-text-secondary); }
    }

    /* Bottom row */
    .bottom-row {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 16px;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .card {
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      overflow: hidden;
    }

    .card-hd {
      padding: 14px 18px 12px;
      border-bottom: 1px solid var(--color-border);
      h3 { font-size: 13px; font-weight: 600; margin: 0; }
    }

    .bar-wrap {
      display: flex; align-items: center; gap: 6px; font-size: 12px;
    }

    .bar-fill {
      height: 6px; border-radius: 3px;
      background: var(--color-primary);
      min-width: 4px; max-width: 70px;
    }

    /* Quick actions */
    .qa-card { }
    .qa-list { display: flex; flex-direction: column; padding: 6px; }

    .qa-item {
      display: flex; align-items: center; gap: 10px;
      padding: 10px 10px; border-radius: var(--radius-sm);
      text-decoration: none; font-size: 13px; font-weight: 500;
      color: var(--color-text);
      transition: background .15s;
      &:hover { background: var(--color-surface-2); }
    }

    .qa-ico {
      width: 34px; height: 34px; border-radius: 8px;
      background: var(--color-primary-50);
      display: flex; align-items: center; justify-content: center;
      .material-icons-round { font-size: 17px; color: var(--color-primary); }
    }

    .qa-arrow {
      margin-left: auto; font-size: 18px;
      color: var(--color-text-muted);
    }

    /* Loading */
    .loading-overlay {
      flex: 1; display: flex; align-items: center; justify-content: center;
    }

    .spinner {
      width: 28px; height: 28px;
      border: 3px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: spin .6s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class DashboardComponent implements OnInit {
  private dashSvc = inject(DashboardService);
  readonly theme  = inject(ThemeService);

  stats   = signal<DashboardStats | null>(null);
  loading = signal(true);

  // ── Computed from stats ──────────────────────────────────────

  statCards = computed(() => {
    const s = this.stats()?.summary;
    if (!s) return [];
    return [
      { label: 'Total Students',       value: s.totalStudents.toLocaleString(),
        sub: `♂ ${s.totalMaleStudents}  ♀ ${s.totalFemaleStudents}`, icon: 'school',          color: 'blue'   },
      { label: 'Total Teachers',        value: s.totalTeachers.toLocaleString(),
        sub: `${s.totalClasses} classes`,                            icon: 'person',           color: 'green'  },
      { label: 'Fee Collected (Month)', value: `Rs. ${(s.feeCollectedThisMonth ?? 0).toLocaleString()}`,
        sub: 'This month',                                           icon: 'payments',         color: 'green'  },
      { label: 'Fee Pending',           value: `Rs. ${(s.totalFeePending ?? 0).toLocaleString()}`,
        sub: 'Total outstanding',                                    icon: 'pending_actions',  color: 'red'    }
    ];
  });

  feeChartSeries = computed<ApexAxisChartSeries>(() => {
    const pts = this.stats()?.feeChart ?? [];
    return [
      { name: 'Collected', data: pts.map(p => ({ x: p.monthLabel, y: Math.round(p.collected) })) },
      { name: 'Pending',   data: pts.map(p => ({ x: p.monthLabel, y: Math.round(p.pending)   })) }
    ];
  });

  attSeries = computed<ApexNonAxisChartSeries>(() => {
    const snap    = this.stats()?.attendanceSnapshot ?? [];
    const present = snap.reduce((a, r) => a + r.present, 0);
    const absent  = snap.reduce((a, r) => a + r.absent,  0);
    const leave   = snap.reduce((a, r) => a + r.leave,   0);
    return [present, absent, leave];
  });

  attPct = computed(() => {
    const snap    = this.stats()?.attendanceSnapshot ?? [];
    const total   = snap.reduce((a, r) => a + r.total, 0);
    const present = snap.reduce((a, r) => a + r.present, 0);
    return total > 0 ? Math.round(present / total * 100) : 0;
  });

  feeColors   = computed(() => [this.theme.brandColor(), '#DC2626']);
  attColors   = computed(() => [this.theme.brandColor(), '#DC2626', '#D97706']);
  chartGrid   = computed<ApexGrid>(() => ({
    borderColor: this.theme.chartGridColor(), strokeDashArray: 4
  }));

  // ── Static chart options ────────────────────────────────────

  readonly feeChart: ApexChart = {
    type: 'area', height: 230, toolbar: { show: false },
    background: 'transparent', fontFamily: 'Inter,sans-serif'
  };

  readonly feeXAxis: ApexXAxis  = { type: 'category' };
  readonly feeYAxis: ApexYAxis  = {
    labels: { formatter: (v: number) => `${(v/1000).toFixed(0)}k` }
  };
  readonly feeStroke: ApexStroke     = { curve: 'smooth', width: 2 };
  readonly feeFill: ApexFill         = {
    type: 'gradient',
    gradient: { opacityFrom: 0.35, opacityTo: 0.04 }
  };
  readonly noDataLabels: ApexDataLabels  = { enabled: false };
  readonly sharedTooltip: ApexTooltip    = { shared: true, intersect: false };
  readonly topLegend: ApexLegend         = { position: 'top' };
  readonly bottomLegend: ApexLegend      = { position: 'bottom', fontSize: '11px' };

  readonly attChart: ApexChart = {
    type: 'donut', height: 230, background: 'transparent',
    fontFamily: 'Inter,sans-serif'
  };
  readonly attLabels = ['Present', 'Absent', 'Leave'];
  readonly attPlot: ApexPlotOptions = {
    pie: { donut: { size: '70%', labels: { show: false } } }
  };
  readonly attTooltip: ApexTooltip = {
    y: { formatter: (v: number) => `${v} students` }
  };

  readonly quickActions = [
    { label: 'Mark Attendance',  route: '/attendance',   icon: 'how_to_reg'    },
    { label: 'Add Student',      route: '/students/new', icon: 'person_add'    },
    { label: 'Record Payment',   route: '/fees/pay',     icon: 'point_of_sale' },
    { label: 'Generate Reports', route: '/reports',      icon: 'summarize'     },
    { label: 'Settings',         route: '/settings',     icon: 'settings'      }
  ];

  ngOnInit(): void {
    this.dashSvc.getStats().subscribe({
      next:  d => { this.stats.set(d); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }
}