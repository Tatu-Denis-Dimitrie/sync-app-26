import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, Subject, of, forkJoin } from 'rxjs';
import { map, delay, tap, catchError } from 'rxjs/operators';
import { User, UserRole, UserComparison, FieldConflict, CsvImport, SyncResult, SyncProgress, SyncStatus, Department } from '../models/csv-sync.model';
import { environment } from '../../environments/environment';

interface BackendUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  departmentId: string;
  departmentName: string;
  assignedToId?: string;
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
  private syncProgressSubject = new BehaviorSubject<SyncProgress | null>(null);
  private currentComparisonSubject = new BehaviorSubject<UserComparison[] | null>(null);
  private usersSubject = new BehaviorSubject<User[]>([]);

  syncProgress$ = this.syncProgressSubject.asObservable();
  currentComparison$ = this.currentComparisonSubject.asObservable();
  users$ = this.usersSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadUsers();
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
    const hasDirectReports = allUsers.some(u => u.assignedToId === backendUser.id);
    
    return {
      id: backendUser.id,
      firstName: backendUser.firstName,
      lastName: backendUser.lastName,
      email: backendUser.email,
      departmentId: backendUser.departmentId,
      departmentName: backendUser.departmentName,
      assignedToId: backendUser.assignedToId,
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
        const hasDirectReports = allUsers.some(u => u.assignedToId === backendUser.id);
        
        return {
          id: backendUser.id,
          firstName: backendUser.firstName,
          lastName: backendUser.lastName,
          email: backendUser.email,
          departmentId: backendUser.departmentId,
          departmentName: backendUser.departmentName,
          assignedToId: backendUser.assignedToId,
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

  /**
   * Get departments summary
   */
  getDepartments(): Observable<Department[]> {
    return this.users$.pipe(
      map(users => {
        const deptMap = new Map<string, { lineManagers: Set<string>, employees: Set<string> }>();
        
        users.forEach(user => {
          if (!deptMap.has(user.departmentName)) {
            deptMap.set(user.departmentName, { lineManagers: new Set(), employees: new Set() });
          }
          const dept = deptMap.get(user.departmentName)!;
          if (user.role === UserRole.LineManager) {
            dept.lineManagers.add(user.id);
          } else {
            dept.employees.add(user.id);
          }
        });

        return Array.from(deptMap.entries()).map(([name, data]) => ({
          id: name.toLowerCase().replace(/\s+/g, '-'),
          name,
          lineManagerCount: data.lineManagers.size,
          employeeCount: data.employees.size
        }));
      })
    );
  }

  /**
   * Get sync statistics
   */
  getUserStats(): Observable<any> {
    return this.users$.pipe(
      map(users => ({
        total: users.length,
        lineManagers: users.filter(u => u.role === UserRole.LineManager).length,
        employees: users.filter(u => u.role === UserRole.Employee).length,
        departments: new Set(users.map(u => u.departmentName)).size
      }))
    );
  }

  /**
   * Upload CSV file and compare with database
   */
  uploadAndCompare(file: File): Observable<UserComparison[]> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<UserComparison[]>(`${environment.apiUrl}/CsvSync/upload`, formData).pipe(
      tap((comparisons) => {
        this.currentComparisonSubject.next(comparisons);
      }),
      catchError(error => {
        console.error('Error uploading CSV:', error);
        this.currentComparisonSubject.next(null);
        throw error; // Re-throw to let component handle it
      })
    );
  }

  /**
   * Sync selected users with resolved conflicts
   */
  syncUsers(comparisons: UserComparison[]): Observable<SyncResult> {
    // Filter only selected items and map to sync request format
    const selectedItems = comparisons
      .filter(c => c.selected)
      .map(c => ({
        // For modified/deleted users, use dbUser.id; for new users, use comparison id
        id: c.status === 'new' ? c.id : (c.dbUser?.id || c.id),
        status: c.status,
        csvData: c.csvUser ? {
          firstName: c.csvUser.firstName,
          lastName: c.csvUser.lastName,
          email: c.csvUser.email,
          departmentName: c.csvUser.departmentName,
          assignedToEmail: (c.csvUser as any).assignedToEmail || null
        } : null,
        conflicts: c.conflicts.map(conflict => ({
          field: conflict.field,
          dbValue: conflict.dbValue,
          csvValue: conflict.csvValue,
          selectedValue: conflict.selectedValue,
          selected: conflict.selected
        }))
      }));

    const syncRequest = { items: selectedItems };

    return this.http.post<SyncResult>(`${environment.apiUrl}/CsvSync/sync`, syncRequest).pipe(
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
        });
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
