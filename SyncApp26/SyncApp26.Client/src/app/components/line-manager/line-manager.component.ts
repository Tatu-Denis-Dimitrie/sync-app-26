import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, Observable, combineLatest } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSyncService } from '../../services/user-sync.service';
import { User, UserRole } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'app-line-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './line-manager.component.html',
  styleUrls: ['./line-manager.component.css']
})
export class LineManagerComponent implements OnInit {
  user: User | null = null;
  isLoading = true;
  errorMessage = '';

  assignedUsers$!: Observable<User[]>;
  paginatedAssignedUsers$!: Observable<User[]>;
  availableFunctions: string[] = [];

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 10;
  totalItems = 0;

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedFunction$ = new BehaviorSubject<string>('all');

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedFunction(): string { return this.selectedFunction$.value; }
  set selectedFunction(value: string) { this.selectedFunction$.next(value); }

  UserRole = UserRole;

  constructor(
    private authService: AuthenticationService,
    private userSyncService: UserSyncService
  ) {}

  ngOnInit(): void {
    const currentUser = this.authService.getCurrentUser();
    if (!currentUser?.id) {
      this.errorMessage = 'User session not found.';
      this.isLoading = false;
      return;
    }

    this.userSyncService.getUserById(currentUser.id).subscribe({
      next: (user) => {
        this.user = user;
        if (!user) {
          this.errorMessage = 'Could not load profile details.';
          this.isLoading = false;
          return;
        }

        this.loadDepartmentFunctions(user.departmentId);
        this.setupAssignedUsersStream(user);
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Could not load profile details.';
        this.isLoading = false;
      }
    });
  }

  private setupAssignedUsersStream(manager: User): void {
    this.assignedUsers$ = this.userSyncService.users$.pipe(
      map(users => users.filter(u =>
        (u.assignedToId === manager.id || u.assignedToPersonalId === manager.personalId) &&
        u.id !== manager.id
      ))
    );

    this.paginatedAssignedUsers$ = combineLatest([
      this.assignedUsers$,
      this.searchQuery$,
      this.selectedFunction$,
      this.currentPage$
    ]).pipe(
      map(([users, searchQuery, selectedFunction, currentPage]) => {
        const query = searchQuery.trim().toLowerCase();

        const filtered = users.filter(user => {
          const matchesSearch = !query ||
            user.firstName.toLowerCase().includes(query) ||
            user.lastName.toLowerCase().includes(query);

          const matchesFunction = selectedFunction === 'all' ||
            (user.function || '').trim().toLowerCase() === selectedFunction.trim().toLowerCase();

          return matchesSearch && matchesFunction;
        });

        this.totalItems = filtered.length;
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  private loadDepartmentFunctions(departmentId: string): void {
    this.userSyncService.getFunctionsByDepartmentId(departmentId)
      .pipe(take(1))
      .subscribe(functions => {
        this.availableFunctions = functions;
      });
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFunctionFilterChange(): void {
    this.currentPage = 1;
  }

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  logout(): void {
    this.authService.logout();
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    const d = new Date(date);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = String(d.getFullYear()).slice(-2);
    return `${day}/${month}/${year}`;
  }

  getRelativeTime(date: Date | string | undefined): string {
    if (!date) return '';
    const now = new Date().getTime();
    const then = new Date(date).getTime();
    const diff = now - then;

    const seconds = Math.floor(diff / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (minutes > 0) return `${minutes}m ago`;
    return 'just now';
  }

  getRoleBadgeColor(role: UserRole | undefined): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
  }

}
