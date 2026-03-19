import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Observable, combineLatest, BehaviorSubject } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { User, UserRole, Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'app-users-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './users-list.component.html',
  styleUrls: ['./users-list.component.css']
})
export class UsersListComponent implements OnInit {
  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  departments$!: Observable<Department[]>;

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 15;
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

  UserRole = UserRole;

  constructor(
    private userSyncService: UserSyncService,
    private authService: AuthenticationService,
    private router: Router,
    private route: ActivatedRoute
  ) { }

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.departments$ = this.userSyncService.getDepartments();

    // Check for department filter from query params
    this.route.queryParams.subscribe(params => {
      if (params['department']) {
        this.selectedDepartment = params['department'];
      }
    });

    this.paginatedUsers$ = combineLatest([
      this.users$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.selectedRole$,
      this.currentPage$
    ]).pipe(
      map(([users, searchQuery, selectedDepartment, selectedRole, currentPage]) => {
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

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFilterChange(): void {
    this.currentPage = 1;
  }

  viewUserDetails(userId: string): void {
    this.router.navigate(['/employees', userId]);
  }

  getRoleBadgeColor(role: UserRole): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
  }

  navigateToDashboard(): void {
    this.router.navigate(['/dashboard']);
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

  // Edit and Delete Modal State and Logic
  isEditModalOpen = false;
  isDeleteModalOpen = false;
  selectedUser: User | null = null;

  editForm = {
    firstName: '',
    lastName: '',
    email: '',
    departmentId: '',
    function: '',
    role: UserRole.Employee,
    assignedToId: ''
  };

  allUsers: User[] = [];
  availableFunctions: string[] = [];

  openEditModal(user: User, event: Event): void {
    event.stopPropagation();
    this.selectedUser = user;
    this.editForm = {
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      departmentId: user.departmentId,
      function: user.function || '',
      role: user.role || UserRole.Employee,
      assignedToId: user.assignedToId || ''
    };

    // Store all users for the Line Manager dropdown
    this.users$.pipe(take(1)).subscribe(users => {
      this.allUsers = users;
      this.loadFunctionsForDepartment(this.editForm.departmentId, this.editForm.function);
    });
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.selectedUser = null;
    this.availableFunctions = [];
  }

  onDepartmentChange(departmentId: string): void {
    this.editForm.assignedToId = '';
    this.loadFunctionsForDepartment(departmentId);
  }

  private loadFunctionsForDepartment(departmentId: string, preferredFunction?: string): void {
    this.userSyncService.getFunctionsByDepartmentId(departmentId).subscribe(functions => {
      this.availableFunctions = functions;

      const preferred = preferredFunction?.trim() || '';
      const preferredMatch = preferred
        ? this.availableFunctions.find(fn => fn.trim().toLowerCase() === preferred.toLowerCase())
        : undefined;

      if (preferredMatch) {
        this.editForm.function = preferredMatch;
      } else {
        this.editForm.function = '';
      }

      this.isEditModalOpen = true;
    });
  }

  getAvailableLineManagers(): User[] {
    // Exclude the current user from being their own line manager
    // Only include line managers from the selected department in the edit form
    return this.allUsers.filter(u =>
      u.role === UserRole.LineManager &&
      u.departmentId === this.editForm.departmentId &&
      (!this.selectedUser || u.id !== this.selectedUser.id)
    );
  }

  saveUser(): void {
    if (!this.selectedUser) return;

    const payload = {
      firstName: this.editForm.firstName,
      lastName: this.editForm.lastName,
      email: this.editForm.email,
      departmentId: this.editForm.departmentId,
      function: this.editForm.function || null,
      assignedToId: this.editForm.role === UserRole.LineManager ? null : (this.editForm.assignedToId || null)
    };

    if (this.editForm.role === UserRole.Employee && !payload.assignedToId) {
      alert('Please select a Line Manager for the Employee.');
      return;
    }

    this.userSyncService.updateUser(this.selectedUser.id, payload).subscribe({
      next: () => {
        this.closeEditModal();
        // Since loadUsers is called in service, users$ will emit updated list
      },
      error: (err) => {
        console.error('Error updating user:', err);
        alert(err.error?.message || 'Error updating user');
      }
    });
  }

  openDeleteModal(user: User, event: Event): void {
    event.stopPropagation();
    this.selectedUser = user;
    this.isDeleteModalOpen = true;
  }

  closeDeleteModal(): void {
    this.isDeleteModalOpen = false;
    this.selectedUser = null;
  }

  confirmDelete(): void {
    if (!this.selectedUser) return;

    this.userSyncService.deleteUser(this.selectedUser.id).subscribe({
      next: () => {
        this.closeDeleteModal();
      },
      error: (err) => {
        console.error('Error deleting user:', err);
        alert(err.error?.message || 'Error deleting user');
      }
    });
  }
}
