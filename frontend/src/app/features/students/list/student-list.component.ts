// src/app/features/students/list/student-list.component.ts
import {
  Component, inject, signal, computed, OnInit, OnDestroy, ViewChild
} from '@angular/core';
import { CommonModule }   from '@angular/common';
import { RouterLink }     from '@angular/router';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule, MatTableDataSource } from '@angular/material/table';
import { MatPaginatorModule, MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule }     from '@angular/material/input';
import { MatSelectModule }    from '@angular/material/select';
import { MatButtonModule }    from '@angular/material/button';
import { MatIconModule }      from '@angular/material/icon';
import { MatTooltipModule }   from '@angular/material/tooltip';
import { MatMenuModule }      from '@angular/material/menu';
import { Subject }            from 'rxjs';
import { debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import {
  StudentService, ToastService, downloadBlob
} from '@core/services/api.services';
import { AuthService } from '@core/services/auth.service';
import type { StudentListItem, StudentFilters } from '@core/models';

@Component({
  selector:   'sp-student-list',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    MatTableModule, MatPaginatorModule, MatSortModule,
    MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatTooltipModule, MatMenuModule
  ],
  template: `
    <div class="student-list">

      <div class="page-header">
        <h1>Students</h1>
        <div class="page-header-actions">
          <button class="btn btn-secondary btn-sm" type="button"
                  (click)="downloadTemplate()" aria-label="Download import template">
            <span class="material-icons-round" aria-hidden="true">download</span>
            Template
          </button>
          <button class="btn btn-secondary btn-sm" type="button"
                  (click)="fileInput.click()"
                  aria-label="Import students from Excel">
            <span class="material-icons-round" aria-hidden="true">upload_file</span>
            Import Excel
          </button>
          <input #fileInput type="file" accept=".xlsx,.xls"
                 aria-label="Excel import file" hidden
                 (change)="onBulkImport($event)">
          @if (auth.hasPermission('Students', 'canCreate')) {
            <a routerLink="/students/new" class="btn btn-primary btn-sm">
              <span class="material-icons-round" aria-hidden="true">person_add</span>
              Add Student
            </a>
          }
        </div>
      </div>

      <!-- Filters -->
      <div class="card filter-bar" [formGroup]="filterForm">
        <mat-form-field>
          <mat-label>Search</mat-label>
          <span class="material-icons-round" matPrefix aria-hidden="true">search</span>
          <input matInput formControlName="search"
                 placeholder="Name, roll number, email…"
                 aria-label="Search students">
        </mat-form-field>

        <mat-form-field>
          <mat-label>Status</mat-label>
          <mat-select formControlName="status" aria-label="Filter by status">
            <mat-option value="">All Statuses</mat-option>
            <mat-option value="Active">Active</mat-option>
            <mat-option value="Inactive">Inactive</mat-option>
            <mat-option value="Graduated">Graduated</mat-option>
            <mat-option value="Transferred">Transferred</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field>
          <mat-label>Gender</mat-label>
          <mat-select formControlName="gender" aria-label="Filter by gender">
            <mat-option value="">All</mat-option>
            <mat-option value="Male">Male</mat-option>
            <mat-option value="Female">Female</mat-option>
          </mat-select>
        </mat-form-field>

        <button class="btn btn-ghost btn-sm" type="button"
                (click)="clearFilters()" aria-label="Clear all filters">
          <span class="material-icons-round" aria-hidden="true">filter_alt_off</span>
          Clear
        </button>
      </div>

      <!-- Import progress -->
      @if (importing()) {
        <div class="card import-progress" role="status" aria-live="polite">
          <span class="spinner-sm"></span>
          <span>Importing students…</span>
        </div>
      }

      <!-- Table -->
      <div class="card table-card">
        @if (loading()) {
          <div class="loading-overlay" aria-busy="true" aria-label="Loading students">
            <div class="spinner"></div>
          </div>
        }

        <div class="table-scroll">
          <table mat-table [dataSource]="dataSource" class="sp-mat-table"
                 aria-label="Students list">

            <ng-container matColumnDef="photo">
              <th mat-header-cell *matHeaderCellDef scope="col"></th>
              <td mat-cell *matCellDef="let row">
                <div class="student-avatar" [attr.aria-label]="row.fullName">
                  @if (row.profilePhotoUrl) {
                    <img [src]="row.profilePhotoUrl" [alt]="row.fullName" loading="lazy">
                  } @else {
                    <span aria-hidden="true">{{ row.fullName[0] }}</span>
                  }
                </div>
              </td>
            </ng-container>

            <ng-container matColumnDef="fullName">
              <th mat-header-cell *matHeaderCellDef scope="col"
                  (click)="sortByColumn('fullName')" class="sortable-hd"
                  [class.sorted]="sortField() === 'fullName'"
                  aria-sort="none">
                Student
              </th>
              <td mat-cell *matCellDef="let row">
                <div class="student-info">
                  <a [routerLink]="['/students', row.studentId]" class="student-name">
                    {{ row.fullName }}
                  </a>
                  <span class="student-email">{{ row.email }}</span>
                </div>
              </td>
            </ng-container>

            <ng-container matColumnDef="rollNumber">
              <th mat-header-cell *matHeaderCellDef scope="col">Roll No</th>
              <td mat-cell *matCellDef="let row">
                <code class="roll-no">{{ row.rollNumber }}</code>
              </td>
            </ng-container>

            <ng-container matColumnDef="className">
              <th mat-header-cell *matHeaderCellDef scope="col">Class</th>
              <td mat-cell *matCellDef="let row">
                {{ row.className }} {{ row.section }}
              </td>
            </ng-container>

            <ng-container matColumnDef="gender">
              <th mat-header-cell *matHeaderCellDef scope="col">Gender</th>
              <td mat-cell *matCellDef="let row">
                <span class="badge"
                      [class]="row.gender === 'Male' ? 'badge-primary' : 'badge-muted'">
                  {{ row.gender }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef scope="col">Status</th>
              <td mat-cell *matCellDef="let row">
                <span class="badge" [ngClass]="statusClass(row.status)">
                  {{ row.status }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="enrollmentDate">
              <th mat-header-cell *matHeaderCellDef scope="col">Enrolled</th>
              <td mat-cell *matCellDef="let row">
                {{ row.enrollmentDate | date:'dd MMM yyyy' }}
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef scope="col"></th>
              <td mat-cell *matCellDef="let row">
                <div class="row-actions">
                  <a [routerLink]="['/students', row.studentId]"
                     class="btn btn-ghost btn-icon btn-sm"
                     [matTooltip]="'View ' + row.fullName"
                     aria-label="View profile">
                    <span class="material-icons-round" aria-hidden="true">visibility</span>
                  </a>
                  @if (auth.hasPermission('Students', 'canEdit')) {
                    <a [routerLink]="['/students', row.studentId, 'edit']"
                       class="btn btn-ghost btn-icon btn-sm"
                       [matTooltip]="'Edit ' + row.fullName"
                       aria-label="Edit student">
                      <span class="material-icons-round" aria-hidden="true">edit</span>
                    </a>
                  }
                  @if (auth.hasPermission('Students', 'canDelete')) {
                    <button class="btn btn-ghost btn-icon btn-sm" type="button"
                            (click)="confirmDelete(row)"
                            [attr.aria-label]="'Delete ' + row.fullName"
                            matTooltip="Delete">
                      <span class="material-icons-round" aria-hidden="true"
                            style="color:var(--color-danger)">delete_outline</span>
                    </button>
                  }
                </div>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns; sticky: true"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row" *matNoDataRow>
              <td class="mat-cell" [attr.colspan]="displayedColumns.length">
                <div class="empty-state">
                  <span class="material-icons-round">school</span>
                  <h3>No students found</h3>
                  <p>Try adjusting your search filters</p>
                </div>
              </td>
            </tr>
          </table>
        </div>

        <!-- Pagination -->
        <div class="table-footer">
          <span class="total-count" aria-live="polite">
            {{ totalCount() }} student{{ totalCount() !== 1 ? 's' : '' }}
          </span>
          <mat-paginator
            [length]="totalCount()"
            [pageSize]="pageSize()"
            [pageSizeOptions]="[10, 20, 50, 100]"
            (page)="onPage($event)"
            aria-label="Paginate students list">
          </mat-paginator>
        </div>
      </div>

    </div>
  `,
  styles: [`
    .student-list { display: flex; flex-direction: column; gap: 16px; }

    .filter-bar {
      padding: 14px 16px;
      display: flex; align-items: center; flex-wrap: wrap; gap: 12px;
      mat-form-field { min-width: 160px; flex: 1; max-width: 220px; }
    }

    .import-progress {
      display: flex; align-items: center; gap: 10px;
      padding: 12px 16px; font-size: 13px;
    }

    .table-card { overflow: hidden; position: relative; }
    .table-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; }

    .sp-mat-table {
      width: 100%;
      th {
        background: var(--color-surface-2);
        font-size: 11px; font-weight: 600;
        text-transform: uppercase; letter-spacing: .05em;
      }
      td, th { padding: 10px 14px; border-bottom-color: var(--color-border); }
      tr:hover td { background: var(--color-surface-2); }
    }

    .sortable-hd { cursor: pointer; user-select: none;
      &.sorted { color: var(--color-primary); }
    }

    .student-avatar {
      width: 34px; height: 34px; border-radius: 50%;
      background: var(--color-primary); color: #fff;
      font-size: 13px; font-weight: 700;
      display: flex; align-items: center; justify-content: center;
      overflow: hidden;
      img { width: 100%; height: 100%; object-fit: cover; }
    }

    .student-info { display: flex; flex-direction: column; }
    .student-name {
      font-size: 13px; font-weight: 600; color: var(--color-primary);
      text-decoration: none;
      &:hover { text-decoration: underline; }
    }
    .student-email { font-size: 11px; color: var(--color-text-secondary); }

    .roll-no {
      font-family: var(--font-mono); font-size: 12px;
      background: var(--color-surface-2); padding: 2px 7px; border-radius: 4px;
    }

    .row-actions { display: flex; gap: 2px; }

    .table-footer {
      display: flex; align-items: center; justify-content: space-between;
      padding: 4px 8px; border-top: 1px solid var(--color-border);
      flex-wrap: wrap; gap: 8px;
    }
    .total-count { font-size: 12px; color: var(--color-text-secondary); padding-left: 8px; }

    .loading-overlay {
      position: absolute; inset: 0; z-index: 10;
      display: flex; align-items: center; justify-content: center;
      background: rgba(0,0,0,.04);
    }

    .spinner {
      width: 28px; height: 28px;
      border: 3px solid var(--color-border); border-top-color: var(--color-primary);
      border-radius: 50%; animation: sp-spin .6s linear infinite;
    }

    .spinner-sm {
      width: 18px; height: 18px;
      border: 2px solid var(--color-border); border-top-color: var(--color-primary);
      border-radius: 50%; animation: sp-spin .6s linear infinite;
      flex-shrink: 0;
    }

    @keyframes sp-spin { to { transform: rotate(360deg); } }
  `]
})
export class StudentListComponent implements OnInit, OnDestroy {
  private studentSvc = inject(StudentService);
  private toast      = inject(ToastService);
  readonly auth      = inject(AuthService);
  private fb         = inject(FormBuilder);

  @ViewChild(MatPaginator) paginator!: MatPaginator;

  loading    = signal(false);
  importing  = signal(false);
  totalCount = signal(0);
  pageSize   = signal(20);
  pageIndex  = signal(0);
  sortField  = signal<string>('');
  sortDir    = signal<'asc' | 'desc'>('asc');

  dataSource = new MatTableDataSource<StudentListItem>([]);

  displayedColumns = [
    'photo', 'fullName', 'rollNumber', 'className',
    'gender', 'status', 'enrollmentDate', 'actions'
  ];

  filterForm = this.fb.group({
    search: [''],
    status: [''],
    gender: ['']
  });

  // All subscriptions that must be cleaned up when the component is destroyed.
  // The valueChanges subscriptions for filter inputs are long-lived (they
  // persist until the user navigates away) so they MUST use takeUntil.
  private readonly destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.loadStudents();

    // Search — debounced so we don't fire on every keystroke.
    this.filterForm.get('search')!.valueChanges.pipe(
      debounceTime(400),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(() => {
      this.pageIndex.set(0);
      this.loadStudents();
    });

    // Select filters — immediate, no debounce needed.
    for (const field of ['status', 'gender']) {
      this.filterForm.get(field)!.valueChanges.pipe(
        takeUntil(this.destroy$)
      ).subscribe(() => {
        this.pageIndex.set(0);
        this.loadStudents();
      });
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadStudents(): void {
    this.loading.set(true);
    const v = this.filterForm.value;

    const filters: StudentFilters = {
      page:     this.pageIndex() + 1,
      pageSize: this.pageSize(),
      search:   v.search   || undefined,
      status:   v.status   || undefined,
      gender:   v.gender   || undefined,
      sort:     this.sortField() || undefined,
      dir:      this.sortDir()
    };

    // HTTP observables complete automatically — no takeUntil needed here.
    this.studentSvc.getStudents(filters).subscribe({
      next: (result) => {
        this.dataSource.data = result.items;
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.loadStudents();
  }

  sortByColumn(col: string): void {
    if (this.sortField() === col) {
      this.sortDir.update(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortField.set(col);
      this.sortDir.set('asc');
    }
    this.pageIndex.set(0);
    this.loadStudents();
  }

  clearFilters(): void {
    this.filterForm.reset({ search: '', status: '', gender: '' });
    this.pageIndex.set(0);
    this.loadStudents();
  }

  onBulkImport(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    this.importing.set(true);
    this.studentSvc.bulkImport(file).subscribe({
      next: (result) => {
        this.importing.set(false);
        if (result.succeeded > 0) {
          this.toast.success(
            'Import complete',
            `${result.succeeded} enrolled, ${result.failed} failed.`
          );
          this.loadStudents();
        } else {
          this.toast.error('Import failed', `${result.failed} row(s) had errors.`);
        }
      },
      error: () => {
        this.importing.set(false);
        this.toast.error('Import failed', 'Could not process the file.');
      }
    });
  }

  downloadTemplate(): void {
    this.studentSvc.downloadTemplate().subscribe({
      next: (blob) => downloadBlob(blob, 'students_import_template.xlsx'),
      error: () => this.toast.error('Download failed')
    });
  }

  confirmDelete(student: StudentListItem): void {
    if (!confirm(`Delete "${student.fullName}"? This cannot be undone.`)) return;
    this.studentSvc.deleteStudent(student.studentId).subscribe({
      next: () => { this.toast.success('Student removed'); this.loadStudents(); },
      error: () => this.toast.error('Delete failed')
    });
  }

  statusClass(status: string): string {
    return ({
      Active:      'badge-success',
      Inactive:    'badge-danger',
      Graduated:   'badge-primary',
      Transferred: 'badge-warning'
    } as Record<string, string>)[status] ?? 'badge-muted';
  }
}