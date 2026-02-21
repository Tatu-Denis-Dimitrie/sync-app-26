import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Observable, merge, of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { DepartmentsSyncService } from '../../services/departments-sync.service';
import { Department } from '../../models/csv-sync.model';

@Component({
  selector: 'app-departments',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './departments.component.html',
  styleUrls: ['./departments.component.css']
})
export class DepartmentsComponent implements OnInit {
  departments$!: Observable<Department[]>;
  stats$!: Observable<any>;

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
