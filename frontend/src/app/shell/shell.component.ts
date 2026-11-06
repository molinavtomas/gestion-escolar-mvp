import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ROLE_LABELS, Role } from '../core/models';

/** Un ítem del menú lateral. */
interface NavItem { path: string; label: string; icon: string; }

/** Menú de cada rol: cada uno ve solo sus tareas. */
const NAVIGATION: Record<Role, NavItem[]> = {
  Director: [
    { path: '/panel', label: 'Panel', icon: 'pi pi-home' },
    { path: '/personas', label: 'Personas', icon: 'pi pi-users' },
    { path: '/cursos', label: 'Cursos y materias', icon: 'pi pi-sitemap' },
    { path: '/calificaciones', label: 'Calificaciones', icon: 'pi pi-pencil' },
    { path: '/boletines', label: 'Boletines', icon: 'pi pi-file' },
    { path: '/asistencia', label: 'Asistencia', icon: 'pi pi-check-square' }
  ],
  Secretaria: [], // se completa abajo: igual que Dirección
  Profesor: [{ path: '/calificaciones', label: 'Mis calificaciones', icon: 'pi pi-pencil' }],
  Preceptor: [
    { path: '/asistencia', label: 'Asistencia', icon: 'pi pi-check-square' },
    { path: '/boletines', label: 'Boletines', icon: 'pi pi-file' }
  ],
  Familia: [{ path: '/mi-boletin', label: 'Boletín', icon: 'pi pi-file' }]
};
NAVIGATION.Secretaria = NAVIGATION.Director;

/**
 * Estructura común de todas las pantallas con sesión: barra lateral con el escudo,
 * menú según el rol, y el contenido de la página a la derecha.
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
            <img src="logo.png" alt="Escudo del colegio">
            <span>Colegio de los<br>Buenos Ayres</span>
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
export class ShellComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Menú abierto/cerrado en celulares. */
  readonly menuOpen = signal(false);

  readonly navigation = computed(() => {
    const role = this.auth.user()?.role;
    return role ? NAVIGATION[role] : [];
  });

  readonly roleLabel = computed(() => {
    const role = this.auth.user()?.role;
    return role ? ROLE_LABELS[role] : '';
  });

  home() {
    const user = this.auth.user();
    return user ? this.auth.homeFor(user.role) : '/login';
  }

  logout() {
    this.auth.logout().subscribe(() => this.router.navigateByUrl('/login'));
  }
}
