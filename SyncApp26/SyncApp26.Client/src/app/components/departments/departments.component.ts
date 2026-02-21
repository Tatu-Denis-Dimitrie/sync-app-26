import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, merge, of, BehaviorSubject, combineLatest } from 'rxjs';
import { switchMap, map } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { DepartmentsSyncService } from '../../services/departments-sync.service';
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
  paginatedDepartments$!: Observable<Department[]>;
  stats$!: Observable<any>;

  private currentPage$ = new BehaviorSubject<number>(1);
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
    private router: Router
  ) {}

  ngOnInit(): void {
    const refresh$ = merge(of(null), this.departmentsSyncService.departmentsSynced$);
    this.departments$ = refresh$.pipe(
      switchMap(() => this.userSyncService.getDepartments())
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

  onPageChange(event: any): void {
    this.currentPage = typeof event === 'number' ? event : event.page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFilterChange(): void {
    this.currentPage = 1;
  }
}
