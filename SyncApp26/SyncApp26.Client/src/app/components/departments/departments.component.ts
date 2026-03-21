import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, merge, of, BehaviorSubject, combineLatest, Subject } from 'rxjs';
import { switchMap, map, startWith, take } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { DepartmentsSyncService } from '../../services/departments-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'app-departments',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './departments.component.html',
  styleUrls: ['./departments.component.css']
})
export class DepartmentsComponent implements OnInit {
  departments$!: Observable<Department[]>;
  deletedDepartments$!: Observable<Department[]>;
  paginatedDepartments$!: Observable<Department[]>;
  stats$!: Observable<any>;

  private currentPage$ = new BehaviorSubject<number>(1);
  private refreshTrigger$ = new Subject<void>();
  pageSize = 9; // 3x3 grid
  totalItems = 0;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  private searchQuery$ = new BehaviorSubject<string>('');
  private sizeFilter$ = new BehaviorSubject<string>('all');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get sizeFilter(): string { return this.sizeFilter$.value; }
  set sizeFilter(value: string) { this.sizeFilter$.next(value); }

  constructor(
    private userSyncService: UserSyncService,
    private departmentsSyncService: DepartmentsSyncService,
    private authService: AuthenticationService,
    private router: Router
  ) { }

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    // Refresh departments when requested or when sync happens
    const refresh$ = merge(
      this.refreshTrigger$.pipe(startWith(null)), // Initial load and manual refresh
      this.departmentsSyncService.departmentsSynced$ // Auto-refresh after department sync
    );

    this.departments$ = refresh$.pipe(
      switchMap(() => this.userSyncService.getDepartments())
    );

    this.deletedDepartments$ = refresh$.pipe(
      switchMap(() => this.userSyncService.getDeletedDepartments())
    );

    this.stats$ = refresh$.pipe(
      switchMap(() => this.userSyncService.getUserStats())
    );

    this.paginatedDepartments$ = combineLatest([
      this.departments$,
      this.searchQuery$,
      this.sizeFilter$,
      this.currentPage$
    ]).pipe(
      map(([departments, searchQuery, sizeFilter, currentPage]) => {
        const normalizedQuery = searchQuery.trim().toLowerCase();

        const filtered = departments.filter(department => {
          const totalPeople = department.lineManagerCount + department.employeeCount;
          const matchesSearch = !normalizedQuery || department.name.toLowerCase().includes(normalizedQuery);

          const matchesSize =
            sizeFilter === 'all' ||
            (sizeFilter === 'small' && totalPeople <= 10) ||
            (sizeFilter === 'medium' && totalPeople >= 11 && totalPeople <= 50) ||
            (sizeFilter === 'large' && totalPeople > 50);

          return matchesSearch && matchesSize;
        });

        this.totalItems = filtered.length;

        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  viewDepartmentUsers(departmentName: string): void {
    this.router.navigate(['/users'], { queryParams: { department: departmentName } });
  }

  navigateToDepartments(): void {
    this.router.navigate(['/departments']);
  }

  toggleDepartmentStatus(event: Event, dept: Department): void {
    event.stopPropagation(); // Prevent navigation when clicking button

    const newStatus = !dept.isActive;
    this.userSyncService.updateDepartment(dept.id, dept.name, newStatus).subscribe({
      next: () => {
        console.log(`Department ${dept.name} ${newStatus ? 'activated' : 'deactivated'}`);
        // Trigger refresh to show updated status
        this.refreshTrigger$.next();
      },
      error: (error) => {
        console.error('Error toggling department status:', error);
      }
    });
  }

  navigateToUsers(): void {
    this.router.navigate(['/users']);
  }

  navigateToEmployees(): void {
    this.router.navigate(['/employees']);
  }

  navigateToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  navigateToImportHistory(): void {
    this.router.navigate(['/import-history']);
  }

  navigateToSignature(): void {
    this.router.navigate(['/admin-signature']);
  }

  onPageChange(event: any): void {
    this.currentPage = typeof event === 'number' ? event : event.page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFilterChange(): void {
    this.currentPage = 1;
  }

  getDaysRemaining(deletedAt: string | Date | undefined): number {
    if (!deletedAt) return 0;
    const deletedDate = new Date(deletedAt);
    const deletionDate = new Date(deletedDate.getTime() + 90 * 24 * 60 * 60 * 1000); // Add 90 days
    const today = new Date();
    const diffTime = Math.max(0, deletionDate.getTime() - today.getTime());
    return Math.ceil(diffTime / (1000 * 60 * 60 * 24));
  }

  // Edit and Delete Modals
  isEditModalOpen = false;
  isDeleteModalOpen = false;
  selectedDept: Department | null = null;
  editDeptName = '';
  transferToDeptId = '';
  allDepartments: Department[] = [];

  openEditModal(dept: Department, event: Event): void {
    event.stopPropagation();
    this.selectedDept = dept;
    this.editDeptName = dept.name;
    this.isEditModalOpen = true;
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.selectedDept = null;
    this.editDeptName = '';
  }

  saveDepartment(): void {
    if (!this.selectedDept || !this.editDeptName.trim()) return;

    this.userSyncService.updateDepartment(this.selectedDept.id, this.editDeptName, this.selectedDept.isActive)
      .subscribe({
        next: () => {
          this.closeEditModal();
          this.refreshTrigger$.next();
        },
        error: (err) => {
          console.error('Error updating department:', err);
          alert(err.error?.message || 'Error updating department');
        }
      });
  }

  openDeleteModal(dept: Department, event: Event): void {
    event.stopPropagation();
    this.selectedDept = dept;
    this.transferToDeptId = '';

    // Store all other active departments for the transfer dropdown
    this.departments$.pipe(take(1)).subscribe(depts => {
      this.allDepartments = depts.filter(d => d.id !== dept.id && d.isActive);
      this.isDeleteModalOpen = true;
    });
  }

  closeDeleteModal(): void {
    this.isDeleteModalOpen = false;
    this.selectedDept = null;
    this.transferToDeptId = '';
  }

  confirmDelete(): void {
    if (!this.selectedDept) return;

    const hasUsers = (this.selectedDept.employeeCount + this.selectedDept.lineManagerCount) > 0;
    if (hasUsers && !this.transferToDeptId) {
      alert('Please select a department to transfer the existing users to.');
      return;
    }

    this.userSyncService.deleteDepartment(this.selectedDept.id, hasUsers ? this.transferToDeptId : undefined)
      .subscribe({
        next: () => {
          this.closeDeleteModal();
          this.refreshTrigger$.next();
        },
        error: (err) => {
          console.error('Error deleting department:', err);
          alert(err.error?.message || 'Error deleting department');
        }
      });
  }

  isRestoreModalOpen = false;
  deptToRestore: Department | null = null;

  openRestoreModal(dept: Department, event: Event): void {
    event.stopPropagation();
    this.deptToRestore = dept;
    this.isRestoreModalOpen = true;
  }

  closeRestoreModal(): void {
    this.isRestoreModalOpen = false;
    this.deptToRestore = null;
  }

  confirmRestore(): void {
    if (!this.deptToRestore) return;

    this.userSyncService.restoreDepartment(this.deptToRestore.id).subscribe({
      next: () => {
        console.log(`Department ${this.deptToRestore?.name} restored successfully`);
        this.closeRestoreModal();
        this.refreshTrigger$.next();
      },
      error: (err) => {
        console.error('Error restoring department:', err);
        alert(err.error?.message || 'Error restoring department');
        this.closeRestoreModal();
      }
    });
  }
}
