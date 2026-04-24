// ============================================================
// features/students/detail/student-detail.component.ts
// ============================================================
import {
  Component, inject, signal, computed, OnInit
} from '@angular/core';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  StudentService, FeeService, ReportService,
  ToastService, downloadBlob
} from '@core/services/api.services';
import { AuthService } from '@core/services/auth.service';
import type { StudentDetail, FeesDue } from '@core/models';

@Component({
  selector: 'sp-student-detail',
  standalone: true,
  imports: [
    CommonModule, DatePipe, DecimalPipe, RouterLink,
    MatTabsModule, MatButtonModule, MatTooltipModule
  ],
  template: `
    <div class="detail-page">
      <div class="page-header">
        <div class="ph-left">
          <a routerLink="/students" class="btn btn-ghost btn-icon">
            <span class="material-icons-round">arrow_back</span>
          </a>
          <h1>Student Profile</h1>
        </div>
        <div class="page-header-actions">
          @if (auth.hasPermission('Reports','canView')) {
            <button class="btn btn-secondary btn-sm" (click)="downloadReportCard()"
                    [disabled]="dlLoading()">
              <span class="material-icons-round">picture_as_pdf</span>
              {{ dlLoading() ? 'Generating…' : 'Report Card' }}
            </button>
          }
          @if (auth.hasPermission('Students','canEdit') && student()) {
            <a [routerLink]="['/students', student()!.studentId, 'edit']" class="btn btn-primary btn-sm">
              <span class="material-icons-round">edit</span> Edit
            </a>
          }
        </div>
      </div>

      @if (loading()) {
        <div class="loading-overlay" style="padding:80px">
          <div class="spinner-lg"></div>
        </div>
      }

      @if (student(); as s) {
        <div class="profile-layout">

          <!-- LEFT: Profile card -->
          <div class="profile-card card">
            <div class="profile-hero">
              <div class="profile-photo">
                @if (s.profilePhotoUrl) {
                  <img [src]="s.profilePhotoUrl" [alt]="s.fullName" loading="lazy">
                } @else {
                  {{ s.fullName[0] }}
                }
              </div>
              <h2>{{ s.fullName }}</h2>
              <code class="roll-badge">{{ s.rollNumber }}</code>
              <span class="badge" [ngClass]="statusClass(s.status)">{{ s.status }}</span>
            </div>

            <div class="info-list">
              @for (field of infoFields(s); track field.label) {
                <div class="info-row">
                  <span class="info-lbl">{{ field.label }}</span>
                  <span class="info-val">{{ field.value }}</span>
                </div>
              }
            </div>

            <div class="mini-stats">
              <div class="mini-stat">
                <span [style.color]="s.attendance.attendancePct >= 75 ? 'var(--color-success)' : 'var(--color-danger)'">
                  {{ s.attendance.attendancePct | number:'1.0-1' }}%
                </span>
                <small>Attendance</small>
              </div>
              <div class="mini-stat">
                <span style="color:var(--color-success)">{{ s.attendance.presentDays }}</span>
                <small>Present</small>
              </div>
              <div class="mini-stat">
                <span style="color:var(--color-danger)">{{ s.attendance.absentDays }}</span>
                <small>Absent</small>
              </div>
              <div class="mini-stat">
                <span [style.color]="s.fees.balanceDue > 0 ? 'var(--color-danger)' : 'var(--color-success)'">
                  {{ s.fees.balanceDue | number:'1.0-0' }}
                </span>
                <small>Balance</small>
              </div>
            </div>
          </div>

          <!-- RIGHT: Tabs -->
          <div>
            <mat-tab-group color="primary" animationDuration="150ms">

              <mat-tab label="Attendance">
                <div class="tab-pad">
                  <div class="att-card card">
                    <h4>Attendance Summary</h4>
                    @for (bar of attBars(s); track bar.label) {
                      <div class="att-row">
                        <span class="att-lbl">{{ bar.label }}</span>
                        <div class="att-track">
                          <div class="att-fill" [style.width.%]="bar.pct"
                               [style.background]="bar.color"></div>
                        </div>
                        <span class="att-num">{{ bar.count }}</span>
                      </div>
                    }
                  </div>
                </div>
              </mat-tab>

              <mat-tab label="Fee Dues">
                <div class="tab-pad">
                  @if (feesLoading()) {
                    <div style="padding:40px" class="loading-overlay">
                      <div class="spinner-lg"></div>
                    </div>
                  }
                  @if (fees(); as f) {
                    <div class="fee-totals card">
                      <div class="fee-tot-item">
                        <span class="ft-lbl">Total Due</span>
                        <span class="ft-val">Rs. {{ f.grandTotalDue | number:'1.0-2' }}</span>
                      </div>
                      <div class="fee-tot-item">
                        <span class="ft-lbl">Total Paid</span>
                        <span class="ft-val" style="color:var(--color-success)">
                          Rs. {{ f.grandTotalPaid | number:'1.0-2' }}
                        </span>
                      </div>
                      <div class="fee-tot-item">
                        <span class="ft-lbl">Balance Due</span>
                        <span class="ft-val"
                              [style.color]="f.grandBalance > 0 ? 'var(--color-danger)' : 'var(--color-success)'">
                          Rs. {{ f.grandBalance | number:'1.0-2' }}
                        </span>
                      </div>
                    </div>

                    <table class="sp-table" style="margin-top:12px">
                      <thead>
                        <tr><th>Fee Type</th><th>Due Date</th><th>Due</th><th>Paid</th><th>Balance</th><th></th></tr>
                      </thead>
                      <tbody>
                        @for (item of f.lineItems; track item.feeTypeId) {
                          <tr [class.row-overdue]="item.isOverdue">
                            <td><strong>{{ item.feeTypeName }}</strong></td>
                            <td>{{ item.dueDate ? (item.dueDate | date:'dd MMM yy') : '—' }}</td>
                            <td>Rs. {{ item.amountDue | number:'1.0-2' }}</td>
                            <td style="color:var(--color-success)">Rs. {{ item.amountPaid | number:'1.0-2' }}</td>
                            <td [style.color]="item.balance > 0 ? 'var(--color-danger)' : 'var(--color-success)'">
                              Rs. {{ item.balance | number:'1.0-2' }}
                            </td>
                            <td>
                              @if (item.isOverdue) {
                                <span class="badge badge-danger">Overdue</span>
                              } @else if (item.balance <= 0) {
                                <span class="badge badge-success">Paid</span>
                              } @else {
                                <span class="badge badge-warning">Pending</span>
                              }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  }
                </div>
              </mat-tab>

            </mat-tab-group>
          </div>

        </div>
      }
    </div>
  `,
  styles: [`
    .ph-left { display:flex; align-items:center; gap:10px; }

    .profile-layout {
      display: grid;
      grid-template-columns: 280px 1fr;
      gap: 20px;
      align-items: flex-start;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .profile-card { border-radius: var(--radius-lg); overflow: hidden; }

    .profile-hero {
      padding: 24px 16px;
      display: flex; flex-direction: column; align-items: center; gap: 8px;
      background: var(--color-primary-50);
      border-bottom: 1px solid var(--color-border);
      text-align: center;

      h2 { font-size: 16px; font-weight: 700; margin: 4px 0 0; }
    }

    .profile-photo {
      width: 72px; height: 72px; border-radius: 50%;
      background: var(--color-primary); color: #fff;
      font-size: 28px; font-weight: 700;
      display: flex; align-items: center; justify-content: center;
      overflow: hidden; border: 3px solid var(--color-surface);

      img { width: 100%; height: 100%; object-fit: cover; }
    }

    .roll-badge {
      background: var(--color-surface); padding: 2px 10px;
      border-radius: 4px; font-family: var(--font-mono); font-size: 12px;
    }

    .info-list { padding: 8px 14px; }

    .info-row {
      display: flex; justify-content: space-between; align-items: baseline;
      padding: 6px 0; border-bottom: 1px solid var(--color-border-light);
      gap: 8px;
      &:last-child { border-bottom: none; }
    }

    .info-lbl { font-size: 11px; color: var(--color-text-secondary); white-space: nowrap; }
    .info-val  { font-size: 12px; font-weight: 500; text-align: right; word-break: break-word; }

    .mini-stats {
      display: grid; grid-template-columns: repeat(4, 1fr);
      border-top: 1px solid var(--color-border);
    }

    .mini-stat {
      padding: 10px 4px; text-align: center;
      border-right: 1px solid var(--color-border);
      &:last-child { border-right: none; }

      span { display: block; font-size: 14px; font-weight: 700; }
      small { font-size: 10px; color: var(--color-text-secondary); }
    }

    .att-card {
      padding: 16px; margin-bottom: 12px;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      h4 { font-size: 13px; margin: 0 0 14px; }
    }

    .att-row { display: grid; grid-template-columns: 70px 1fr 36px; align-items: center; gap: 10px; margin-bottom: 10px; }
    .att-lbl  { font-size: 12px; }
    .att-track { height: 8px; background: var(--color-surface-2); border-radius: 4px; overflow: hidden; }
    .att-fill  { height: 100%; border-radius: 4px; transition: width .5s; }
    .att-num   { font-size: 12px; font-weight: 600; text-align: right; }

    .fee-totals {
      display: flex; gap: 20px; padding: 14px;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      flex-wrap: wrap;
    }

    .fee-tot-item { display: flex; flex-direction: column; gap: 2px; }
    .ft-lbl { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: var(--color-text-secondary); }
    .ft-val { font-size: 16px; font-weight: 700; }

    .row-overdue td { background: var(--color-danger-bg) !important; }

    .tab-pad { padding: 16px 0; }

    .card { background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-md); }

    .spinner-lg {
      width: 32px; height: 32px;
      border: 3px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: spin .6s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class StudentDetailComponent implements OnInit {
  private route      = inject(ActivatedRoute);
  private studentSvc = inject(StudentService);
  private feeSvc     = inject(FeeService);
  private reportSvc  = inject(ReportService);
  private toast      = inject(ToastService);
  readonly auth      = inject(AuthService);

  student     = signal<StudentDetail | null>(null);
  fees        = signal<FeesDue | null>(null);
  loading     = signal(true);
  feesLoading = signal(false);
  dlLoading   = signal(false);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.studentSvc.getStudent(id).subscribe({
      next: s => { this.student.set(s); this.loading.set(false); this.loadFees(id); },
      error: () => { this.loading.set(false); this.toast.error('Student not found'); }
    });
  }

  loadFees(id: string): void {
    this.feesLoading.set(true);
    this.feeSvc.getDues(id).subscribe({
      next: f => { this.fees.set(f); this.feesLoading.set(false); },
      error: () => this.feesLoading.set(false)
    });
  }

  downloadReportCard(): void {
    const s = this.student();
    if (!s) return;
    this.dlLoading.set(true);
    this.reportSvc.getReportCard(s.studentId, s.academicYearId, true).subscribe({
      next: blob => { downloadBlob(blob, `ReportCard_${s.rollNumber}.pdf`); this.dlLoading.set(false); this.toast.success('Downloaded'); },
      error: () => { this.dlLoading.set(false); this.toast.error('Download failed'); }
    });
  }

  infoFields(s: StudentDetail) {
    return [
      { label: 'Email',    value: s.email },
      { label: 'Phone',    value: s.phoneNumber ?? '—' },
      { label: 'Class',    value: `${s.className} – ${s.section}` },
      { label: 'DOB',      value: s.dateOfBirth ?? '—' },
      { label: 'Gender',   value: s.gender },
      { label: 'Blood',    value: s.bloodGroup ?? '—' },
      { label: 'Enrolled', value: s.enrollmentDate },
      { label: 'Parent',   value: s.parentName ?? '—' }
    ];
  }

  attBars(s: StudentDetail) {
    const total = s.attendance.totalDays || 1;
    return [
      { label: 'Present', count: s.attendance.presentDays, pct: s.attendance.presentDays/total*100, color: 'var(--color-success)' },
      { label: 'Absent',  count: s.attendance.absentDays,  pct: s.attendance.absentDays/total*100,  color: 'var(--color-danger)'  },
      { label: 'Leave',   count: s.attendance.leaveDays,   pct: s.attendance.leaveDays/total*100,   color: 'var(--color-warning)' }
    ];
  }

  statusClass(s: string) {
    return { Active:'badge-success', Inactive:'badge-danger', Graduated:'badge-primary', Transferred:'badge-warning' }[s] ?? 'badge-muted';
  }
}