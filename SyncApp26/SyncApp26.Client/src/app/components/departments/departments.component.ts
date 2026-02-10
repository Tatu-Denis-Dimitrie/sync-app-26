import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, BehaviorSubject, combineLatest } from 'rxjs';
import { map } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
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
    private router: Router
  ) {}

  ngOnInit(): void {
    this.departments$ = this.userSyncService.getDepartments();
    this.stats$ = this.userSyncService.getUserStats();

    this.paginatedDepartments$ = combineLatest([
      this.departments$,
      this.searchQuery$,
      this.sizeFilter$,
      this.currentPage$
    ]).pipe(
      map(([departments, searchQuery, sizeFilter, currentPage]) => {
        // Filter departments
        let filtered = departments.filter(dept => {
          const matchesSearch = !searchQuery ||
            dept.name.toLowerCase().includes(searchQuery.toLowerCase());
          
          const totalPersonnel = dept.lineManagerCount + dept.employeeCount;
          let matchesSize = true;
          if (sizeFilter === 'small') {
            matchesSize = totalPersonnel <= 10;
          } else if (sizeFilter === 'medium') {
            matchesSize = totalPersonnel > 10 && totalPersonnel <= 50;
          } else if (sizeFilter === 'large') {
            matchesSize = totalPersonnel > 50;
          }
          
          return matchesSearch && matchesSize;
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
}
