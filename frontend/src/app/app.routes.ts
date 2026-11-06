import { Routes } from '@angular/router';
import { authGuard, homeRedirectGuard, permissionGuard } from './core/auth.guard';
import { ShellComponent } from './shell/shell.component';
import { LoginPage } from './pages/login.page';
import { DashboardPage } from './pages/dashboard.page';
import { PeoplePage } from './pages/people.page';
import { StructurePage } from './pages/structure.page';
import { GradesPage } from './pages/grades.page';
import { AttendancePage } from './pages/attendance.page';
import { ReportCardsPage } from './pages/report-cards.page';
import { MyReportCardPage } from './pages/my-report-card.page';
import { NoticesPage } from './pages/notices.page';
import { AdminPage } from './pages/admin.page';
import { AuditPage } from './pages/audit.page';
import { ChangePasswordPage } from './pages/change-password.page';

/**
 * Mapa de pantallas. Todo lo que está dentro del Shell requiere sesión (authGuard)
 * y cada pantalla indica qué permiso hace falta (permissionGuard, ver models.ts).
 */
export const routes: Routes = [
  { path: 'login', component: LoginPage, title: 'Ingresar' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: 'panel', component: DashboardPage, title: 'Panel', canActivate: [permissionGuard('viewAll')] },
      { path: 'boletines', component: ReportCardsPage, title: 'Boletines', canActivate: [permissionGuard('viewAll')] },
      { path: 'personas', component: PeoplePage, title: 'Personas', canActivate: [permissionGuard('viewAll')] },
      { path: 'cursos', component: StructurePage, title: 'Cursos y materias', canActivate: [permissionGuard('viewAll')] },
      { path: 'calificaciones', component: GradesPage, title: 'Calificaciones', canActivate: [permissionGuard('editGrades')] },
      { path: 'asistencia', component: AttendancePage, title: 'Asistencia', canActivate: [permissionGuard('viewAttendance')] },
      { path: 'mi-boletin', component: MyReportCardPage, title: 'Boletín', canActivate: [permissionGuard('family')] },
      { path: 'avisos', component: NoticesPage, title: 'Avisos', canActivate: [permissionGuard('family', 'viewAll')] },
      { path: 'administracion', component: AdminPage, title: 'Administración', canActivate: [permissionGuard('admin')] },
      { path: 'auditoria', component: AuditPage, title: 'Auditoría', canActivate: [permissionGuard('admin')] },
      { path: 'cambiar-clave', component: ChangePasswordPage, title: 'Cambiar clave' },
      { path: '', pathMatch: 'full', canActivate: [homeRedirectGuard], children: [] }
    ]
  },
  { path: '**', redirectTo: '' }
];
