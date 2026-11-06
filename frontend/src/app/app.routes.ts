import { Routes } from '@angular/router';
import { authGuard, homeRedirectGuard, roleGuard } from './core/auth.guard';
import { ShellComponent } from './shell/shell.component';
import { LoginPage } from './pages/login.page';
import { DashboardPage } from './pages/dashboard.page';
import { PeoplePage } from './pages/people.page';
import { StructurePage } from './pages/structure.page';
import { GradesPage } from './pages/grades.page';
import { AttendancePage } from './pages/attendance.page';
import { ReportCardsPage } from './pages/report-cards.page';
import { MyReportCardPage } from './pages/my-report-card.page';
import { ChangePasswordPage } from './pages/change-password.page';

/**
 * Mapa de pantallas. Todo lo que está dentro del Shell requiere sesión (authGuard)
 * y cada pantalla indica qué roles pueden entrar (roleGuard).
 */
export const routes: Routes = [
  { path: 'login', component: LoginPage, title: 'Ingresar' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      // Dirección y Secretaría
      { path: 'panel', component: DashboardPage, title: 'Panel', canActivate: [roleGuard('Director', 'Secretaria')] },
      { path: 'personas', component: PeoplePage, title: 'Personas', canActivate: [roleGuard('Director', 'Secretaria')] },
      { path: 'cursos', component: StructurePage, title: 'Cursos y materias', canActivate: [roleGuard('Director', 'Secretaria')] },

      // Carga de notas: profesores (sus materias) y Dirección/Secretaría (todas)
      { path: 'calificaciones', component: GradesPage, title: 'Calificaciones', canActivate: [roleGuard('Profesor', 'Director', 'Secretaria')] },

      // Preceptores (sus cursos) y Dirección/Secretaría (todos)
      { path: 'asistencia', component: AttendancePage, title: 'Asistencia', canActivate: [roleGuard('Preceptor', 'Director', 'Secretaria')] },
      { path: 'boletines', component: ReportCardsPage, title: 'Boletines', canActivate: [roleGuard('Preceptor', 'Director', 'Secretaria')] },

      // Familias
      { path: 'mi-boletin', component: MyReportCardPage, title: 'Boletín', canActivate: [roleGuard('Familia')] },

      // Todos
      { path: 'cambiar-clave', component: ChangePasswordPage, title: 'Cambiar clave' },
      { path: '', pathMatch: 'full', canActivate: [homeRedirectGuard], children: [] }
    ]
  },
  { path: '**', redirectTo: '' }
];
