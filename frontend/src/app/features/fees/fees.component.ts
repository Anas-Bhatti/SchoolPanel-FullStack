// src/app/features/fees/fees.component.ts
import {
  Component, inject, signal, OnInit, OnDestroy
} from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { RouterLink }           from '@angular/router';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatFormFieldModule }   from '@angular/material/form-field';
import { MatInputModule }       from '@angular/material/input';
import { MatButtonModule }      from '@angular/material/button';
import { MatTooltipModule }     from '@angular/material/tooltip';
import { Subject }              from 'rxjs';
import { debounceTime, takeUntil, distinctUntilChanged, filter } from 'rxjs/operators';
import {
  FeeService, ReportService, ToastService, downloadBlob
} from '@core/services/api.services';
import { AuthService }          from '@core/services/auth.service';
import type { FeesDue }         from '@core/models';

// UUID v4 pattern – 36 chars, xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

@Component({
  selector:   'sp-fees',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, DatePipe, RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule, MatInputModule,
    MatButtonModule, MatTooltipModule
  ],
  template: `
    <div>
      <div class="page-header">
        <h1>Fees</h1>
        <div class="page-header-actions">
          @if (auth.hasPermission('Fees', 'canCreate')) {
            <a routerLink="/fees/pay" class="btn btn-primary btn-sm">
              <span class="material-icons-round" aria-hidden="true">point_of_sale</span>
              Record Payment
            </a>
          }
        </div>
      </div>

      <!-- Student lookup -->
      <div class="card search-card">
        <mat-form-field style="width:100%;max-width:420px">
          <mat-label>Student ID</mat-label>
          <span class="material-icons-round" matPrefix aria-hidden="true">search</span>
          <input
            matInput
            [formControl]="studentIdCtrl"
            placeholder="Paste student UUID to view dues"
            aria-label="Enter student UUID to view fee dues">
          @if (loading()) {
            <mat-hint>Loading…</mat-hint>
          } @else if (studentIdCtrl.value && !UUID_PATTERN.test(studentIdCtrl.value)) {
            <mat-hint>Enter a valid student UUID (36 characters)</mat-hint>
          }
        </mat-form-field>
      </div>

      <!-- Loading state -->
      @if (loading()) {
        <div class="loading-overlay" style="padding:60px">
          <div class="spinner"></div>
        </div>
      }

      <!-- Dues display -->
      @if (fees(); as f) {
        <!-- Summary banner -->
        <div class="card fee-banner">
          <div class="fee-student-info">
            <h3>{{ f.studentName }}</h3>
            <div class="student-tags">
              <span class="badge badge-muted">{{ f.rollNumber }}</span>
              <span class="badge badge-muted">{{ f.className }}</span>
            </div>
          </div>

          <div class="fee-totals">
            <div class="fee-total">
              <span class="ft-label">Total Due</span>
              <span class="ft-value">Rs. {{ f.grandTotalDue | number:'1.0-2' }}</span>
            </div>
            <div class="fee-total">
              <span class="ft-label">Total Paid</span>
              <span class="ft-value" style="color:var(--color-success)">
                Rs. {{ f.grandTotalPaid | number:'1.0-2' }}
              </span>
            </div>
            <div class="fee-total">
              <span class="ft-label">Balance Due</span>
              <span class="ft-value"
                    [style.color]="f.grandBalance > 0
                      ? 'var(--color-danger)'
                      : 'var(--color-success)'">
                Rs. {{ f.grandBalance | number:'1.0-2' }}
              </span>
            </div>
          </div>

          @if (auth.hasPermission('Fees', 'canCreate')) {
            <a routerLink="/fees/pay"
               [queryParams]="{ studentId: f.studentId }"
               class="btn btn-primary btn-sm"
               aria-label="Record payment for {{ f.studentName }}">
              <span class="material-icons-round" aria-hidden="true">payments</span>
              Pay Now
            </a>
          }
        </div>

        <!-- Line items table -->
        @if (f.lineItems.length) {
          <div class="card table-scroll">
            <table class="sp-table" aria-label="Fee line items">
              <thead>
                <tr>
                  <th scope="col">Fee Type</th>
                  <th scope="col">Frequency</th>
                  <th scope="col">Due Date</th>
                  <th scope="col">Amount Due</th>
                  <th scope="col">Paid</th>
                  <th scope="col">Balance</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (item of f.lineItems; track item.feeTypeId) {
                  <tr [class.row-overdue]="item.isOverdue">
                    <td><strong>{{ item.feeTypeName }}</strong></td>
                    <td>{{ item.frequency }}</td>
                    <td>
                      {{ item.dueDate ? (item.dueDate | date:'dd MMM yyyy') : '—' }}
                    </td>
                    <td>Rs. {{ item.amountDue | number:'1.0-2' }}</td>
                    <td style="color:var(--color-success)">
                      Rs. {{ item.amountPaid | number:'1.0-2' }}
                    </td>
                    <td [style.color]="item.balance > 0
                            ? 'var(--color-danger)'
                            : 'var(--color-success)'">
                      <strong>Rs. {{ item.balance | number:'1.0-2' }}</strong>
                    </td>
                    <td>
                      @if (item.isOverdue) {
                        <span class="badge badge-danger">Overdue</span>
                      } @else if (item.balance <= 0) {
                        <span class="badge badge-success">Cleared</span>
                      } @else {
                        <span class="badge badge-warning">Pending</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <div class="empty-state card" style="padding:40px">
            <span class="material-icons-round">receipt_long</span>
            <h3>No fee records</h3>
            <p>No fee types are assigned to this student</p>
          </div>
        }
      }

      <!-- No result yet (UUID entered but not yet found) -->
      @if (!loading() && !fees() && hasSearched()) {
        <div class="empty-state card" style="padding:48px">
          <span class="material-icons-round">person_search</span>
          <h3>Student not found</h3>
          <p>No student found with this ID. Check the UUID and try again.</p>
        </div>
      }

    </div>
  `,
  styles: [`
    .search-card { padding: 16px; }

    /* Student banner */
    .fee-banner {
      display: flex; align-items: center;
      justify-content: space-between; flex-wrap: wrap;
      padding: 16px 20px; gap: 16px;
    }

    .fee-student-info {
      h3 { font-size: 15px; font-weight: 700; margin: 0 0 6px; }
    }

    .student-tags { display: flex; gap: 6px; }

    .fee-totals { display: flex; gap: 24px; flex-wrap: wrap; }

    .fee-total { display: flex; flex-direction: column; gap: 2px; }

    .ft-label {
      font-size: 11px; text-transform: uppercase; letter-spacing: .04em;
      color: var(--color-text-secondary);
    }

    .ft-value { font-size: 16px; font-weight: 700; }

    /* Overdue row */
    .row-overdue td { background: var(--color-danger-bg) !important; }

    /* Table */
    .card {
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      overflow: hidden;
    }

    .table-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; }

    /* Loading */
    .spinner {
      width: 28px; height: 28px;
      border: 3px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: sp-spin .6s linear infinite;
    }

    @keyframes sp-spin { to { transform: rotate(360deg); } }
  `]
})
export class FeesComponent implements OnInit, OnDestroy {
  private feeSvc = inject(FeeService);
  private toast  = inject(ToastService);
  readonly auth  = inject(AuthService);

  fees       = signal<FeesDue | null>(null);
  loading    = signal(false);
  hasSearched = signal(false);

  // Expose for template
  readonly UUID_PATTERN = UUID_PATTERN;

  // Standalone FormControl — no FormBuilder needed
  readonly studentIdCtrl = new FormControl<string>('');

  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.studentIdCtrl.valueChanges.pipe(
      debounceTime(600),
      distinctUntilChanged(),
      filter(v => !!v && UUID_PATTERN.test(v)),
      takeUntil(this.destroy$)
    ).subscribe(id => {
      if (id) this.loadDues(id);
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadDues(studentId: string): void {
    this.loading.set(true);
    this.hasSearched.set(true);
    this.fees.set(null);

    this.feeSvc.getDues(studentId).subscribe({
      next: (f) => {
        this.fees.set(f);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        const msg = err?.status === 404
          ? 'Student not found. Check the UUID.'
          : 'Failed to load fee data.';
        this.toast.error('Fee lookup failed', msg);
      }
    });
  }
}