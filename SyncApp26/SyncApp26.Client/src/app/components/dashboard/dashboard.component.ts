import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, Subject, combineLatest, BehaviorSubject } from 'rxjs';
import { map, takeUntil } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { DepartmentsSyncService} from '../../services/departments-sync.service';
import { CSVDepartmentComparisonDTO } from '../../models/csv-department-sync.model';
import { User, UserComparison, UserRole, Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';
import { ComparisonViewComponent } from '../comparison-view/comparison-view.component';
import { UploadProgress, SyncProgressUpdate } from '../../services/user-sync.signalr.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent, ComparisonViewComponent],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  stats$!: Observable<any>;
  departments$!: Observable<Department[]>;
  currentComparison$!: Observable<UserComparison[] | null>;

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 10;
  totalItems = 0;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedDepartment$ = new BehaviorSubject<string>('all');
  private selectedRole$ = new BehaviorSubject<UserRole | 'all'>('all');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedDepartment(): string { return this.selectedDepartment$.value; }
  set selectedDepartment(value: string) { this.selectedDepartment$.next(value); }

  get selectedRole(): UserRole | 'all' { return this.selectedRole$.value; }
  set selectedRole(value: UserRole | 'all') { this.selectedRole$.next(value); }
  
  isUploading = false;
  isSyncing = false;
  showComparison = false;
  showDepartmentComparison = false;
  showErrorModal = false;
  errorModalTitle = '';
  errorModalErrors: string[] = [];
  errorModalWarnings: string[] = [];
  errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };

  currentComparisons: UserComparison[] = [];
  totalSyncItems = 0;
  currentDepartmentComparisons: CSVDepartmentComparisonDTO[] = [];
  departmentSearchQuery: string = '';

  uploadProgress: UploadProgress | null = null;
  syncProgress: SyncProgressUpdate | null = null;

  uploadStartTime: number = 0;
  syncStartTime: number = 0;
  successMessage: string = '';
  serverTimingInfo: { validationTimeMs: number; comparisonTimeMs: number; totalTimeMs: number } | null = null;

  UserRole = UserRole;
  fileName = '';

  constructor(
    private userSyncService: UserSyncService,
    private departmentsSyncService: DepartmentsSyncService,
    private router: Router
  ) { }

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.stats$ = this.userSyncService.getUserStats();
    this.departments$ = this.userSyncService.getDepartments();
    this.currentComparison$ = this.userSyncService.currentComparison$;

    // Subscribe to progress events
    this.userSyncService.uploadProgress$
      .pipe(takeUntil(this.destroy$))
      .subscribe(progress => {
        this.uploadProgress = progress;
      });

    this.userSyncService.syncProgress$
      .pipe(takeUntil(this.destroy$))
      .subscribe(progress => {
        this.syncProgress = progress;
      });

    // Subscribe to comparison changes
    this.currentComparison$
      .pipe(takeUntil(this.destroy$))
      .subscribe(comparisons => {
        this.showComparison = comparisons !== null && comparisons.length > 0;
        this.currentComparisons = comparisons || [];
      });

    this.paginatedUsers$ = combineLatest([
      this.users$,
      this.stats$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.selectedRole$,
      this.currentPage$
    ]).pipe(
      map(([users, stats, searchQuery, selectedDepartment, selectedRole, currentPage]) => {
        // Filter users
        let filtered = users.filter(user => {
          const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
          const matchesSearch = !searchQuery ||
            fullName.includes(searchQuery.toLowerCase()) ||
            user.email.toLowerCase().includes(searchQuery.toLowerCase());
          const matchesDepartment = selectedDepartment === 'all' ||
            user.departmentName === selectedDepartment;
          const matchesRole = selectedRole === 'all' ||
            user.role === selectedRole;
          return matchesSearch && matchesDepartment && matchesRole;
        });

        this.totalItems = filtered.length;

        // Paginate
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  formatDuration(ms: number): string {
    const seconds = Math.floor(ms / 1000);
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    if (minutes > 0) {
      return `${minutes}m ${remainingSeconds}s`;
    }
    return `${seconds}.${Math.floor((ms % 1000) / 100)}s`;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.fileName = file.name;
      this.uploadFile(file);
    }
  }

  onDepartmentFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.uploadDepartmentFile(file);
    }
  }

  uploadFile(file: File): void {
    this.isUploading = true;
    this.successMessage = '';
    this.uploadStartTime = Date.now();
    this.serverTimingInfo = null;

    this.userSyncService.uploadAndCompare(file)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comparisons) => {
          const duration = Date.now() - this.uploadStartTime;
          console.log('CSV uploaded and compared:', comparisons);
          this.isUploading = false;
          this.showComparison = true;
          
          // Get server timing info
          this.userSyncService.timingInfo$.pipe(takeUntil(this.destroy$)).subscribe(timing => {
            this.serverTimingInfo = timing;
            if (timing) {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)} (Server: ${this.formatDuration(timing.totalTimeMs)}, Network: ${this.formatDuration(duration - timing.totalTimeMs)})`;
            } else {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)}`;
            }
          });
          
          setTimeout(() => this.successMessage = '', 10000);
        },
        error: (error) => {
          console.error('Upload failed:', error);
          this.isUploading = false;

          // Show validation errors in custom modal
          if (error.status === 400 && error.error) {
            const errorData = error.error;

            if (errorData.errors && errorData.errors.length > 0) {
              this.errorModalTitle = 'CSV Validation Failed';
              this.errorModalErrors = [];
              this.errorModalWarnings = [];

              // Format errors
              const maxErrors = 20;
              const errorsToShow = errorData.errors.slice(0, maxErrors);

              errorsToShow.forEach((err: any) => {
                // Handle both string errors and object errors
                if (typeof err === 'string') {
                  this.errorModalErrors.push(err);
                } else if (err.row === 0 || !err.row) {
                  this.errorModalErrors.push(err.message || err);
                } else {
                  this.errorModalErrors.push(`Row ${err.row}: ${err.field} - ${err.message}`);
                }
              });

              if (errorData.errors.length > maxErrors) {
                this.errorModalErrors.push(`...and ${errorData.errors.length - maxErrors} more errors`);
              }

              // Format warnings
              if (errorData.warnings && errorData.warnings.length > 0) {
                errorData.warnings.slice(0, 10).forEach((warn: any) => {
                  if (typeof warn === 'string') {
                    this.errorModalWarnings.push(warn);
                  } else {
                    this.errorModalWarnings.push(`Row ${warn.row}: ${warn.field} - ${warn.message}`);
                  }
                });
              }

              // Stats
              if (errorData.totalRows !== undefined) {
                this.errorModalStats = {
                  totalRows: errorData.totalRows,
                  validRows: errorData.validRows,
                  invalidRows: errorData.invalidRows
                };
              }

              this.showErrorModal = true;
            } else if (errorData.error) {
              this.errorModalTitle = 'Upload Error';
              this.errorModalErrors = [errorData.error];
              this.errorModalWarnings = [];
              this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
              this.showErrorModal = true;
            }
          } else {
            this.errorModalTitle = 'Upload Failed';
            this.errorModalErrors = [error.message || 'Unknown error occurred'];
            this.errorModalWarnings = [];
            this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
            this.showErrorModal = true;
          }
        }
      });
  }

  uploadDepartmentFile(file: File): void {
    this.isUploading = true;
    this.departmentsSyncService.uploadAndCompareDepartments(file)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comparisons) => {
          console.log('CSV uploaded and compared:', comparisons);
          this.currentDepartmentComparisons = comparisons;
          this.isUploading = false;
          this.showDepartmentComparison = true;
        },
        error: (error) => {
          console.error('Upload failed:', error);
          this.isUploading = false;

          if (error.status === 400 && error.error) {
            const errorData = error.error;

            if (errorData.errors && errorData.errors.length > 0) {
              this.errorModalTitle = 'CSV Validation Failed';
              this.errorModalErrors = errorData.errors.slice(0, 20);
              this.errorModalWarnings = errorData.warnings || [];
              this.showErrorModal = true;
            } else if (errorData.error) {
              this.errorModalTitle = 'Upload Error';
              this.errorModalErrors = [errorData.error];
              this.errorModalWarnings = [];
              this.showErrorModal = true;
            }
          } else {
            this.errorModalTitle = 'Upload Failed';
            this.errorModalErrors = [error.message || 'Unknown error occurred'];
            this.errorModalWarnings = [];
            this.showErrorModal = true;
          }
        }
      });
  }

  closeErrorModal(): void {
    this.showErrorModal = false;
    this.errorModalTitle = '';
    this.errorModalErrors = [];
    this.errorModalWarnings = [];
    this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
  }

  syncSelectedUsers(): void {
    this.totalSyncItems = this.getSelectedSyncCount();
    this.isSyncing = true;
    this.successMessage = '';
    this.syncStartTime = Date.now();
    this.serverTimingInfo = null;

    this.userSyncService.syncUsers(this.currentComparisons)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          const duration = Date.now() - this.syncStartTime;
          console.log('Sync successful:', result);
          this.isSyncing = false;
          this.showComparison = false;
          
          if (result.processingTimeMs) {
            this.successMessage = `Sync completed in ${this.formatDuration(duration)} (Server: ${this.formatDuration(result.processingTimeMs)}, Network: ${this.formatDuration(duration - result.processingTimeMs)})`;
          } else {
            this.successMessage = `Sync completed in ${this.formatDuration(duration)}`;
          }
          
          setTimeout(() => this.successMessage = '', 10000);

          this.userSyncService.refreshUsers();
        },
        error: (error) => {
          console.error('Sync failed:', error);
          this.isSyncing = false;
        }
      });
  }

  syncSelectedDepartments(): void {
    if (!this.currentDepartmentComparisons || this.currentDepartmentComparisons.length === 0) {
      return;
    }

    this.isSyncing = true;
    const selected = this.currentDepartmentComparisons.filter((c: any) => c.selected);
    this.departmentsSyncService.syncDepartments(selected)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          console.log('Department sync successful:', result);
          this.isSyncing = false;
          this.showDepartmentComparison = false;
        },
        error: (error) => {
          console.error('Department sync failed:', error);
          this.isSyncing = false;
        }
      });
  }

  cancelComparison(): void {
    this.userSyncService.clearComparison();
    this.showComparison = false;
  }

  cancelDepartmentComparison(): void {
    this.departmentsSyncService.clearComparison();
    this.showDepartmentComparison = false;
  }

  toggleAllDepartmentSelections(checked: boolean): void {
    this.currentDepartmentComparisons = this.currentDepartmentComparisons.map((c: any) => ({
      ...c,
      selected: c.status === 'new' ? checked : false
    }));
  }

  toggleDepartmentSelection(comparison: any): void {
    if (comparison.status !== 'new') {
      return;
    }
    comparison.selected = !comparison.selected;
  }

  getSelectedDepartmentSyncCount(): number {
    return this.currentDepartmentComparisons.filter((c: any) => c.selected).length;
  }

  getDepartmentChangesCount(): number {
    return this.currentDepartmentComparisons.filter((c: any) => c.status === 'new').length;
  }

  hasDepartmentChanges(): boolean {
    return this.getDepartmentChangesCount() > 0;
  }

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onDepartmentFilterChange(): void {
    this.currentPage = 1;
  }

  onRoleFilterChange(): void {
    this.currentPage = 1;
  }

  getRoleBadgeColor(role: UserRole): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
  }

  getRoleIcon(role: UserRole): string {
    return role === UserRole.LineManager ? '👔' : '👤';
  }

  formatDate(date: Date | string): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString();
  }

  onComparisonSelectionChange(comparisons: UserComparison[]): void {
    this.currentComparisons = comparisons;
  }

  onFieldConflictResolved(event: { comparisonId: string, field: string, value: 'db' | 'csv' }): void {
    console.log('Conflict resolved:', event);
  }

  getSelectedSyncCount(): number {
    return this.currentComparisons.filter(c => c.selected).length;
  }

  navigateToDepartments(): void {
    this.router.navigate(['/departments']);
  }

  navigateToUsers(): void {
    this.router.navigate(['/users']);
  }

  navigateToEmployees(): void {
    this.router.navigate(['/employees']);
  }

  navigateToImportHistory(): void {
    this.router.navigate(['/import-history']);
  }

  getFilteredDepartmentComparisons(): CSVDepartmentComparisonDTO[] {
    // Filter to show only new departments
    let filtered = this.currentDepartmentComparisons.filter((comp: CSVDepartmentComparisonDTO) => comp.status === 'new');

    // Apply search query if provided
    if (!this.departmentSearchQuery.trim()) {
      return filtered;
    }

    const query = this.departmentSearchQuery.toLowerCase();
    return filtered.filter((comp: CSVDepartmentComparisonDTO) => {
      const csvName = comp.csvDepartment?.name?.toLowerCase() || '';
      return csvName.includes(query);
    });
  }
}
