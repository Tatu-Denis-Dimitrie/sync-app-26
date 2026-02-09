import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { UserSyncService } from '../../services/user-sync.service';
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
    private router: Router
  ) {}

  ngOnInit(): void {
    this.departments$ = this.userSyncService.getDepartments();
    this.stats$ = this.userSyncService.getUserStats();
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
