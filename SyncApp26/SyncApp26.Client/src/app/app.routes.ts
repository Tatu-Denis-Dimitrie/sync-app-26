import { Routes } from '@angular/router';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { DepartmentsComponent } from './components/departments/departments.component';
import { UsersListComponent } from './components/users-list/users-list.component';
import { EmployeesDetailComponent } from './components/employees-detail/employees-detail.component';
import { ImportHistoryComponent } from './components/import-history/import-history.component';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'departments', component: DepartmentsComponent },
  { path: 'users', component: UsersListComponent },
  { path: 'employees', component: EmployeesDetailComponent },
  { path: 'employees/:id', component: EmployeesDetailComponent },
  { path: 'import-history', component: ImportHistoryComponent },
  { path: '**', redirectTo: '/dashboard' }
];
