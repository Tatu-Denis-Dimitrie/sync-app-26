import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { ForgotPasswordComponent } from './components/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './components/reset-password/reset-password.component';
import { AccessRestrictedComponent } from './components/access-restricted/access-restricted.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { DepartmentsComponent } from './components/departments/departments.component';
import { UsersListComponent } from './components/users-list/users-list.component';
import { EmployeesDetailComponent } from './components/employees-detail/employees-detail.component';
import { ImportHistoryComponent } from './components/import-history/import-history.component';
import { RegisterComponent } from './components/register/register.component';
import { AdminGuard } from './guards/admin.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'access-restricted', component: AccessRestrictedComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [AdminGuard] },
  { path: 'departments', component: DepartmentsComponent, canActivate: [AdminGuard] },
  { path: 'users', component: UsersListComponent, canActivate: [AdminGuard] },
  { path: 'employees', component: EmployeesDetailComponent, canActivate: [AdminGuard] },
  { path: 'employees/:id', component: EmployeesDetailComponent, canActivate: [AdminGuard] },
  { path: 'import-history', component: ImportHistoryComponent, canActivate: [AdminGuard] },
  { path: '**', redirectTo: '/login' }
];
