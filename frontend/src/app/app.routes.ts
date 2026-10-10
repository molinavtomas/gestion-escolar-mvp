import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LoginComponent } from './pages/login.component';
import { ShellComponent } from './shell/shell.component';
import { AdminComponent } from './pages/admin.component';
import { TeacherComponent } from './pages/teacher.component';
import { PreceptorComponent } from './pages/preceptor.component';
import { FamilyComponent } from './pages/family.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '', component: ShellComponent, canActivate: [authGuard], children: [
      { path: 'administracion', component: AdminComponent },
      { path: 'docente', component: TeacherComponent },
      { path: 'preceptor', component: PreceptorComponent },
      { path: 'familia', component: FamilyComponent },
      { path: '', pathMatch: 'full', redirectTo: 'administracion' }
    ]
  },
  { path: '**', redirectTo: '' }
];
