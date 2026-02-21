import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, Subject, of, forkJoin } from 'rxjs';
import { map, delay, tap, catchError, switchMap, finalize } from 'rxjs/operators';
import { User, UserRole, UserComparison, FieldConflict, CsvImport, SyncResult, SyncProgress, SyncProgressUpdate, SyncStatus, Department, ImportConflictHistory, ImportHistoryItem } from '../models/csv-sync.model';
import { environment } from '../../environments/environment';
import { UserSyncSignalrService, UploadProgress } from './user-sync.signalr.service';
import { from } from 'rxjs';

interface BackendUser {
  id: string;
  personalId: string;
  firstName: string;
  lastName: string;
  email: string;
  departmentId: string;
  departmentName: string;
  assignedToPersonalId?: string;
  assignedToName?: string;
  createdAt: string;
  updatedAt?: string;
}

@Injectable({
  providedIn: 'root'
})
export class UserSyncService {
  private apiUrl = environment.apiUrl + environment.endpoints.users;
  private departmentUrl = environment.apiUrl + environment.endpoints.departments;
  private syncProgressSubject = new BehaviorSubject<SyncProgressUpdate | null>(null);
  private currentComparisonSubject = new BehaviorSubject<UserComparison[] | null>(null);
  private usersSubject = new BehaviorSubject<User[]>([]);
  private timingInfoSubject = new BehaviorSubject<{ validationTimeMs: number; comparisonTimeMs: number; totalTimeMs: number } | null>(null);
  private warningsSubject = new BehaviorSubject<string[]>([]);
  private errorsSubject = new BehaviorSubject<string[]>([]);

  syncProgress$ = this.syncProgressSubject.asObservable();
  currentComparison$ = this.currentComparisonSubject.asObservable();
  users$ = this.usersSubject.asObservable();
  uploadProgress$!: Observable<UploadProgress>;
  timingInfo$ = this.timingInfoSubject.asObservable();
  warnings$ = this.warningsSubject.asObservable();
  errors$ = this.errorsSubject.asObservable();

  constructor(
    private http: HttpClient,
    private signalrService: UserSyncSignalrService
  ) {
    this.uploadProgress$ = this.signalrService.uploadProgress$;
    this.loadUsers();

    // Subscribe to SignalR sync progress and update local subject
    this.signalrService.syncProgress$.subscribe(progress => {
      this.syncProgressSubject.next(progress);
    });
  }

  /**
   * Load users from API
   */
  private loadUsers(): void {
    this.getUsers().subscribe({
      next: (users) => {
        this.usersSubject.next(users);
      },
      error: (error) => {
        console.error('Error loading users:', error);
        this.usersSubject.next([]);
      }
    });
  }

  /**
   * Map backend user to frontend user and calculate role
   */
  private mapBackendUser(backendUser: BackendUser, allUsers: BackendUser[]): User {
    // Determine role: if user has anyone assigned to them, they're a line manager
    const hasDirectReports = allUsers.some(u => u.assignedToPersonalId === backendUser.personalId);
    
    return {
      id: backendUser.id,
      personalId: backendUser.personalId,
      firstName: backendUser.firstName,
      lastName: backendUser.lastName,
      email: backendUser.email,
      departmentId: backendUser.departmentId,
      departmentName: backendUser.departmentName,
      assignedToPersonalId: backendUser.assignedToPersonalId,
      assignedToName: backendUser.assignedToName,
      createdAt: new Date(backendUser.createdAt),
      updatedAt: backendUser.updatedAt ? new Date(backendUser.updatedAt) : undefined,
      role: hasDirectReports ? UserRole.LineManager : UserRole.Employee
    };
  }

  /**
   * Get all users from database
   */
  getUsers(): Observable<User[]> {
    return this.http.get<BackendUser[]>(this.apiUrl).pipe(
      map(backendUsers => {
        // Map all users and calculate their roles
        return backendUsers.map(user => this.mapBackendUser(user, backendUsers));
      }),
      catchError(error => {
        console.error('Error fetching users:', error);
        return of([]);
      })
    );
  }

  /**
   * Get user by ID
   */
  getUserById(id: string): Observable<User | null> {
    return this.http.get<BackendUser>(`${this.apiUrl}/${id}`).pipe(
      map(backendUser => {
        // We need all users to calculate role, so we'll use the cached users
        const allUsers = this.usersSubject.value;
        const hasDirectReports = allUsers.some(u => u.assignedToPersonalId === backendUser.personalId);
        
        return {
          id: backendUser.id,
          personalId: backendUser.personalId,
          firstName: backendUser.firstName,
          lastName: backendUser.lastName,
          email: backendUser.email,
          departmentId: backendUser.departmentId,
          departmentName: backendUser.departmentName,
          assignedToPersonalId: backendUser.assignedToPersonalId,
          assignedToName: backendUser.assignedToName,
          createdAt: new Date(backendUser.createdAt),
          updatedAt: backendUser.updatedAt ? new Date(backendUser.updatedAt) : undefined,
          role: hasDirectReports ? UserRole.LineManager : UserRole.Employee
        };
      }),
      catchError(error => {
        console.error('Error fetching user:', error);
        return of(null);
      })
    );
  }

  getByPersonalId(personalId: string): Observable<User | null> {
    return this.http.get<BackendUser>(`${this.apiUrl}/personalId/${personalId}`).pipe(
      map(backendUser => {
        const allUsers = this.usersSubject.value;
        const hasDirectReports = allUsers.some(u => u.assignedToPersonalId === backendUser.personalId);
        return {
          id: backendUser.id,
          personalId: backendUser.personalId,
          firstName: backendUser.firstName,
          lastName: backendUser.lastName,
          email: backendUser.email,
          departmentId: backendUser.departmentId,
          departmentName: backendUser.departmentName,
          assignedToPersonalId: backendUser.assignedToPersonalId,
          assignedToName: backendUser.assignedToName,
          createdAt: new Date(backendUser.createdAt),
          updatedAt: backendUser.updatedAt ? new Date(backendUser.updatedAt) : undefined,
          role: hasDirectReports ? UserRole.LineManager : UserRole.Employee
        };
      }),
      catchError(error => {
        console.error('Error fetching user by personal ID:', error);
        return of(null);
      })
    );
  }

  /**
   * Get import conflict history for a user
   */
  getImportConflictsByUserId(userId: string): Observable<ImportConflictHistory[]> {
    return this.http.get<ImportConflictHistory[]>(`${environment.apiUrl}/ImportConflict/byUser/${userId}`).pipe(
      catchError(error => {
        console.error('Error fetching import conflicts:', error);
        return of([]);
      })
    );
  }

  /**
   * Get import history list
   */
  getImportHistories(): Observable<ImportHistoryItem[]> {
    return this.http.get<ImportHistoryItem[]>(`${environment.apiUrl}/ImportHistory`).pipe(
      catchError(error => {
        console.error('Error fetching import history:', error);
        return of([]);
      })
    );
  }

  /**
   * Get import conflicts by import history id
   */
  getImportConflictsByImportHistoryId(importHistoryId: string): Observable<ImportConflictHistory[]> {
    return this.http.get<ImportConflictHistory[]>(`${environment.apiUrl}/ImportConflict/byImportHistory/${importHistoryId}`).pipe(
      catchError(error => {
        console.error('Error fetching import conflicts by history:', error);
        return of([]);
      })
    );
  }

  /**
   * Get departments from backend with user counts
   */
  getDepartments(): Observable<Department[]> {
    return this.users$.pipe(
      map(users => {
        const deptMap = new Map<string, { lineManagers: number, employees: number }>();

        users.forEach(user => {
          if (!deptMap.has(user.departmentName)) {
            deptMap.set(user.departmentName, { lineManagers: 0, employees: 0 });
          }
          const dept = deptMap.get(user.departmentName)!;
          if (user.role === UserRole.LineManager) {
            dept.lineManagers++;
          } else {
            dept.employees++;
          }
        });

        return Array.from(deptMap.entries()).map(([name, data]) => ({
          id: name.toLowerCase().replace(/\s+/g, '-'),
          name,
          lineManagerCount: data.lineManagers,
          employeeCount: data.employees
        }));
      }),
      catchError(error => {
        console.error('Error fetching departments:', error);
        return of([]);
      })
    );
  }

  /**
   * Get sync statistics
   */
  getUserStats(): Observable<any> {
    return this.users$.pipe(
      switchMap(users => 
        this.http.get<any[]>(`${this.departmentUrl}`).pipe(
          map(departments => ({
            total: users.length,
            lineManagers: users.filter(u => u.role === UserRole.LineManager).length,
            employees: users.filter(u => u.role === UserRole.Employee).length,
            departments: departments.length
          })),
          catchError(error => {
            console.error('Error fetching departments for stats:', error);
            return of({
              total: users.length,
              lineManagers: users.filter(u => u.role === UserRole.LineManager).length,
              employees: users.filter(u => u.role === UserRole.Employee).length,
              departments: new Set(users.map(u => u.departmentName)).size
            });
          })
        )
      )
    );
  }

  // ... other methods

  /**
   * Upload CSV file and compare with database
   */
  uploadAndCompare(file: File, skipInvalidRows: boolean = false): Observable<UserComparison[]> {
    // Reset comparisons
    this.currentComparisonSubject.next([]);

    // Subscribe to SignalR results and accumulate
    const subscription = this.signalrService.comparisonResult$.subscribe(comparison => {
      const current = this.currentComparisonSubject.value || [];
      this.currentComparisonSubject.next([...current, comparison]);
    });

    return from(this.signalrService.startConnection()).pipe(
      switchMap(() => {
        const connectionId = this.signalrService.getConnectionId();
        const headers: any = {};
        if (connectionId) {
          headers['X-Connection-Id'] = connectionId;
        }

        const formData = new FormData();
        formData.append('file', file);

        // Add query parameter for skipping invalid rows
        const url = `${environment.apiUrl}/CsvSync/upload${skipInvalidRows ? '?skipInvalidRows=true' : ''}`;

        return this.http.post<any>(url, formData, { headers });
      }),
      map((response: any) => {
        // Handle both old format (array) and new format (object with comparisons)
        if (Array.isArray(response)) {
          return { comparisons: response, totalRows: response.length, validationTimeMs: 0, comparisonTimeMs: 0, totalTimeMs: 0 };
        }
        return response;
      }),
      tap((response) => {
        // Use the final list from server to ensure completeness and correct order
        this.currentComparisonSubject.next(response.comparisons);
        
        // Store timing information for display
        this.timingInfoSubject.next({
          validationTimeMs: response.validationTimeMs,
          comparisonTimeMs: response.comparisonTimeMs,
          totalTimeMs: response.totalTimeMs
        });
        
        // Store warnings and errors if present
        this.warningsSubject.next(response.warnings || []);
        this.errorsSubject.next(response.errors || []);
        
        if (response.totalTimeMs) {
          console.log(`Server processing time: ${response.totalTimeMs}ms (Validation: ${response.validationTimeMs}ms, Comparison: ${response.comparisonTimeMs}ms)`);
        }
        
        if (response.warnings && response.warnings.length > 0) {
          console.warn(`CSV validation warnings (${response.warnings.length}):`, response.warnings);
        }
        
        if (response.errors && response.errors.length > 0) {
          console.warn(`CSV validation errors (${response.invalidRows} rows skipped):`, response.errors);
        }
      }),
      map(response => response.comparisons),
      catchError(error => {
        console.error('Error uploading CSV:', error);
        this.currentComparisonSubject.next(null);
        throw error;
      }),
      finalize(() => subscription.unsubscribe())
    );
  }

  /**
   * Sync selected users with resolved conflicts
   */
  syncUsers(comparisons: UserComparison[], fileName?: string): Observable<SyncResult> {
    // Filter only selected items and map to sync request format
    const selectedItems = comparisons
      .filter(c => c.selected)
      .map(c => ({
        // For modified/deleted users, use dbUser.id; for new users, use comparison id
        id: c.status === 'new' ? c.id : (c.dbUser?.id || c.id),
        status: c.status,
        csvData: c.csvUser ? {
          personalId: c.csvUser.personalId,
          firstName: c.csvUser.firstName,
          lastName: c.csvUser.lastName,
          email: c.csvUser.email,
          departmentName: c.csvUser.departmentName,
          assignedToPersonalId: c.csvUser.assignedToPersonalId || null
        } : null,
        conflicts: c.conflicts.map(conflict => ({
          field: conflict.field,
          dbValue: conflict.dbValue,
          csvValue: conflict.csvValue,
          selectedValue: conflict.selectedValue,
          selected: conflict.selected
        }))
      }));

    const syncRequest = { items: selectedItems, fileName: fileName || null };

    return from(this.signalrService.startConnection()).pipe(
      switchMap(() => {
        const connectionId = this.signalrService.getConnectionId();
        const headers: any = {};
        if (connectionId) {
          headers['X-Connection-Id'] = connectionId;
        }
        return this.http.post<SyncResult>(`${environment.apiUrl}/CsvSync/sync`, syncRequest, { headers });
      }),
      tap((result) => {
        if (result.success) {
          // Refresh users list after successful sync
          this.loadUsers();
          this.currentComparisonSubject.next(null);
        }
      }),
      catchError(error => {
        console.error('Error syncing users:', error);
        return of({
          success: false,
          recordsProcessed: 0,
          recordsFailed: 0,
          recordsSkipped: 0,
          message: error.error?.error || 'Sync failed',
          errors: [error.message]
        } as SyncResult);
      })
    );
  }

  /**
   * Clear current comparison
   */
  clearComparison(): void {
    this.currentComparisonSubject.next(null);
  }

  /**
   * Reload users from API
   */
  refreshUsers(): void {
    this.loadUsers();
  }
}
