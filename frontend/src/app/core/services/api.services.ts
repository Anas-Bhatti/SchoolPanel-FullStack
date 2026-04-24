// ============================================================
// core/services/toast.service.ts
// Global toast notifications via signal.
// ============================================================

import { Injectable, signal } from '@angular/core';
import type { ToastMessage } from '../models';

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<ToastMessage[]>([]);

  success(title: string, message?: string, duration = 4000): void {
    this.add({ type: 'success', title, message, duration });
  }

  error(title: string, message?: string, duration = 6000): void {
    this.add({ type: 'error', title, message, duration });
  }

  warning(title: string, message?: string, duration = 5000): void {
    this.add({ type: 'warning', title, message, duration });
  }

  info(title: string, message?: string, duration = 4000): void {
    this.add({ type: 'info', title, message, duration });
  }

  dismiss(id: string): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private add(toast: Omit<ToastMessage, 'id'>): void {
    const id = crypto.randomUUID();
    this.toasts.update(list => [...list, { ...toast, id }]);

    if (toast.duration && toast.duration > 0) {
      setTimeout(() => this.dismiss(id), toast.duration);
    }
  }
}

// ============================================================
// core/services/api-base.service.ts
// HTTP helpers: typed GET/POST/PUT/DELETE with error handling.
// ============================================================
import { inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';

@Injectable({ providedIn: 'root' })
export class ApiBaseService {
  protected http = inject(HttpClient);
  protected base = environment.apiUrl;

  protected get<T>(path: string, params?: Record<string, any>): Observable<T> {
    let httpParams = new HttpParams();
    if (params) {
      Object.entries(params).forEach(([key, val]) => {
        if (val !== null && val !== undefined && val !== '') {
          httpParams = httpParams.set(key, String(val));
        }
      });
    }
    return this.http.get<T>(`${this.base}/${path}`, { params: httpParams });
  }

  protected post<T>(path: string, body: any): Observable<T> {
    return this.http.post<T>(`${this.base}/${path}`, body);
  }

  protected put<T>(path: string, body: any): Observable<T> {
    return this.http.put<T>(`${this.base}/${path}`, body);
  }

  protected delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(`${this.base}/${path}`);
  }

  protected postFormData<T>(path: string, formData: FormData): Observable<T> {
    return this.http.post<T>(`${this.base}/${path}`, formData);
  }

  protected putFormData<T>(path: string, formData: FormData): Observable<T> {
    return this.http.put<T>(`${this.base}/${path}`, formData);
  }

  protected getBlob(path: string, params?: Record<string, any>): Observable<Blob> {
    let httpParams = new HttpParams();
    if (params) {
      Object.entries(params).forEach(([key, val]) => {
        if (val !== null && val !== undefined) {
          httpParams = httpParams.set(key, String(val));
        }
      });
    }
    return this.http.get(`${this.base}/${path}`, {
      params: httpParams,
      responseType: 'blob'
    });
  }
}

// ============================================================
// core/services/student.service.ts
// ============================================================
import type {
  StudentListItem, StudentDetail, StudentFilters,
  CreateStudentRequest, UpdateStudentRequest,
  PagedResult, BulkImportResult, FileUploadResult
} from '../models';

@Injectable({ providedIn: 'root' })
export class StudentService extends ApiBaseService {
  getStudents(filters: StudentFilters): Observable<PagedResult<StudentListItem>> {
    return this.get<PagedResult<StudentListItem>>('students', filters as any);
  }

  getStudent(id: string): Observable<StudentDetail> {
    return this.get<StudentDetail>(`students/${id}`);
  }

  createStudent(request: CreateStudentRequest): Observable<any> {
    return this.post<any>('students', request);
  }

  updateStudent(id: string, request: UpdateStudentRequest): Observable<any> {
    return this.put<any>(`students/${id}`, request);
  }

  updateStudentWithPhoto(id: string, request: UpdateStudentRequest, photo?: File): Observable<any> {
    const fd = new FormData();
    if (photo) fd.append('photo', photo);
    Object.entries(request).forEach(([k, v]) => {
      if (v !== null && v !== undefined) fd.append(k, String(v));
    });
    return this.putFormData<any>(`students/${id}`, fd);
  }

  deleteStudent(id: string): Observable<any> {
    return this.delete<any>(`students/${id}`);
  }

  bulkImport(file: File): Observable<BulkImportResult> {
    const fd = new FormData();
    fd.append('file', file);
    return this.postFormData<BulkImportResult>('students/bulk', fd);
  }

  downloadTemplate(): Observable<Blob> {
    return this.getBlob('students/template');
  }
}

// ============================================================
// core/services/attendance.service.ts
// ============================================================
import type {
  MarkAttendanceRequest, TodayAttendance, AttendanceRow
} from '../models';

@Injectable({ providedIn: 'root' })
export class AttendanceService extends ApiBaseService {
  markAttendance(request: MarkAttendanceRequest): Observable<any> {
    return this.post<any>('attendance/mark', request);
  }

  getMonthly(params: {
    studentId?: string;
    classId?: number;
    month: number;
    year: number;
    academicYearId?: number;
  }): Observable<any> {
    return this.get<any>('attendance/monthly', params as any);
  }

  getToday(academicYearId?: number): Observable<TodayAttendance> {
    return this.get<TodayAttendance>('attendance/today',
      academicYearId ? { academicYearId } : undefined);
  }
}

// ============================================================
// core/services/fee.service.ts
// ============================================================
import type {
  FeesDue, RecordPaymentRequest, FeeCollectionSummary
} from '../models';

@Injectable({ providedIn: 'root' })
export class FeeService extends ApiBaseService {
  getDues(studentId: string, academicYearId?: number): Observable<FeesDue> {
    return this.get<FeesDue>(`fees/dues/${studentId}`,
      academicYearId ? { academicYearId } : undefined);
  }

  recordPayment(request: RecordPaymentRequest): Observable<any> {
    return this.post<any>('fees/pay', request);
  }

  getReceipt(paymentId: number, download = false): Observable<Blob> {
    return this.getBlob(`fees/receipt/${paymentId}`, { download });
  }

  getSummary(params: {
    academicYearId: number;
    classId?: number;
    month?: number;
    year?: number;
  }): Observable<FeeCollectionSummary> {
    return this.get<FeeCollectionSummary>('fees/summary', params as any);
  }
}

// ============================================================
// core/services/dashboard.service.ts
// ============================================================
import type { DashboardStats } from '../models';

@Injectable({ providedIn: 'root' })
export class DashboardService extends ApiBaseService {
  getStats(academicYearId?: number): Observable<DashboardStats> {
    return this.get<DashboardStats>('dashboard/stats',
      academicYearId ? { academicYearId } : undefined);
  }
}

// ============================================================
// core/services/settings.service.ts
// ============================================================
import type { Setting, UpdateSettingRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService extends ApiBaseService {
  getSettings(category?: string): Observable<Setting[]> {
    return this.get<Setting[]>('settings', category ? { category } : undefined);
  }

  updateSetting(request: UpdateSettingRequest): Observable<any> {
    return this.put<any>('settings', request);
  }
}

// ============================================================
// core/services/report.service.ts
// ============================================================
@Injectable({ providedIn: 'root' })
export class ReportService extends ApiBaseService {
  getReportCard(studentId: string, academicYearId: number, download = false): Observable<Blob> {
    return this.getBlob(`reports/report-card/${studentId}`, { academicYearId, download });
  }

  getFeeReceipt(paymentId: number, download = false): Observable<Blob> {
    return this.getBlob(`reports/fee-receipt/${paymentId}`, { download });
  }

  getAttendanceExcel(classId: number, month: number, year: number): Observable<Blob> {
    return this.getBlob('reports/attendance/excel', { classId, month, year });
  }

  getExamResults(examId: number, download = false): Observable<Blob> {
    return this.getBlob(`reports/exam-results/${examId}`, { download });
  }
}

// ============================================================
// core/services/notification.service.ts
// ============================================================
import type { Notification } from '../models';

@Injectable({ providedIn: 'root' })
export class NotificationService extends ApiBaseService {
  readonly unreadCount = signal<number>(0);

  getNotifications(params?: { isRead?: boolean; page?: number }): Observable<any> {
    return this.get<any>('notifications', params as any);
  }

  loadUnreadCount(): void {
    this.getNotifications({ isRead: false, page: 1 }).subscribe({
      next: (data: any) => {
        this.unreadCount.set(data?.unreadCount ?? 0);
      },
      error: () => {}
    });
  }
}

// Utility to trigger a file download from a Blob
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a   = document.createElement('a');
  a.href    = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}