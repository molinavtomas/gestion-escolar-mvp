import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FiltersService } from '../core/filters.service';
import { Permissions, ROLE_LABELS } from '../core/models';
import { NoticesService } from '../core/notices.service';

/** Un ítem del menú lateral, con el permiso que hace falta para verlo. */
interface NavItem { path: string; label: string; icon: string; visible: (p: Permissions) => boolean; }

/**
 * Menú completo. Cada ítem dice con qué permiso se muestra (ver la matriz en models.ts y
 * en el backend, Auth/Permissions.cs). Así hay UNA sola lista y cada rol ve lo suyo.
 */
const NAVIGATION: NavItem[] = [
  { path: '/panel', label: 'Panel', icon: 'pi pi-home', visible: p => p.viewAll },
  { path: '/mi-boletin', label: 'Boletín', icon: 'pi pi-file', visible: p => p.family },
  { path: '/boletines', label: 'Boletines', icon: 'pi pi-file', visible: p => p.viewAll },
  { path: '/calificaciones', label: 'Calificaciones', icon: 'pi pi-pencil', visible: p => p.editGrades },
  { path: '/asistencia', label: 'Asistencia', icon: 'pi pi-check-square', visible: p => p.viewAttendance },
  { path: '/personas', label: 'Personas', icon: 'pi pi-users', visible: p => p.viewAll },
  { path: '/cursos', label: 'Cursos y materias', icon: 'pi pi-sitemap', visible: p => p.viewAll },
  { path: '/avisos', label: 'Avisos', icon: 'pi pi-bell', visible: p => p.family || p.viewAll },
  { path: '/administracion', label: 'Administración', icon: 'pi pi-cog', visible: p => p.admin },
  { path: '/auditoria', label: 'Auditoría', icon: 'pi pi-shield', visible: p => p.admin }
];

/**
 * Estructura común de todas las pantallas con sesión: barra lateral con el escudo,
 * menú según los permisos, y el contenido de la página a la derecha.
 * En celulares la barra se transforma en un encabezado con menú desplegable.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="app-shell">
      <aside class="sidebar" [class.open]="menuOpen()">
        <div class="sidebar-top">
          <a class="brand" [routerLink]="home()" (click)="menuOpen.set(false)">
            <img src="/api/settings/logo" alt="Logo del colegio">
            <span>{{ schoolName() }}</span>
          </a>
          <!-- Botón de menú: solo visible en pantallas chicas -->
          <button class="menu-toggle" (click)="menuOpen.set(!menuOpen())" aria-label="Menú">
            <i [class]="menuOpen() ? 'pi pi-times' : 'pi pi-bars'"></i>
          </button>
        </div>

        <div class="sidebar-body">
          <div class="profile-card">
            <strong>{{ auth.user()?.displayName }}</strong>
            <small>{{ roleLabel() }}</small>
          </div>

          <nav>
            @for (item of navigation(); track item.path) {
              <a [routerLink]="item.path" routerLinkActive="active" (click)="menuOpen.set(false)">
                <i [class]="item.icon"></i><span>{{ item.label }}</span>
                @if (item.path === '/avisos' && notices.unread() > 0) { <span class="badge">{{ notices.unread() }}</span> }
              </a>
            }
          </nav>

          <div class="sidebar-foot">
            <a routerLink="/cambiar-clave" routerLinkActive="active" (click)="menuOpen.set(false)"><i class="pi pi-key"></i> Cambiar clave</a>
            <button class="link-button" (click)="logout()"><i class="pi pi-sign-out"></i> Cerrar sesión</button>
          </div>
        </div>
      </aside>

      <main class="workspace">
        <!-- Aviso: la clave sigue siendo el DNI (el cambio es opcional, solo lo recomendamos) -->
        @if (auth.user()?.usesDefaultPassword) {
          <div class="alert warning no-print">
            <i class="pi pi-shield"></i>
            <span>Tu clave todavía es tu DNI. Te recomendamos <a routerLink="/cambiar-clave">cambiarla</a>.</span>
          </div>
        }
        <router-outlet />
      </main>
    </div>
  `
})
export class ShellComponent implements OnInit {
  readonly auth = inject(AuthService);
  readonly notices = inject(NoticesService);
  private readonly api = inject(ApiService);
  private readonly filters = inject(FiltersService);
  private readonly router = inject(Router);

  /** Menú abierto/cerrado en celulares. */
  readonly menuOpen = signal(false);
  readonly schoolName = signal('Colegio');

  readonly navigation = computed(() => {
    const permissions = this.auth.user()?.permissions;
    return permissions ? NAVIGATION.filter(item => item.visible(permissions)) : [];
  });

  readonly roleLabel = computed(() => {
    const role = this.auth.user()?.role;
    return role ? ROLE_LABELS[role] : '';
  });

  ngOnInit() {
    this.api.get<{ schoolName: string }>('/api/settings/public').subscribe(s => this.schoolName.set(s.schoolName));
    this.notices.refresh();
  }

  home() {
    const user = this.auth.user();
    return user ? this.auth.homeFor(user.role) : '/login';
  }

  logout() {
    this.auth.logout().subscribe(() => {
      this.filters.reset(); // el próximo usuario puede ver otros cursos
      this.router.navigateByUrl('/login');
    });
  }
}
