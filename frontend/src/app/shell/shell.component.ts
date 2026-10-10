import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="app-shell">
      <aside class="sidebar">
        <a class="brand" [routerLink]="home()">
          <span class="brand-mark"><i class="pi pi-graduation-cap"></i></span>
          <span><strong>Aula Clara</strong><small>Gestión escolar</small></span>
        </a>
        <div class="profile-card">
          <div class="avatar">{{ initials() }}</div>
          <div><strong>{{ auth.user()?.displayName }}</strong><small>{{ profileLabel() }}</small></div>
        </div>
        <nav>
          @for (item of navigation(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active"><i [class]="item.icon"></i><span>{{ item.label }}</span></a>
          }
        </nav>
        <div class="sidebar-foot">
          <div class="demo-pill"><span></span> Entorno de demostración</div>
          <button class="text-button" (click)="logout()"><i class="pi pi-sign-out"></i> Cerrar sesión</button>
        </div>
      </aside>
      <main class="workspace"><router-outlet /></main>
    </div>
  `
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  home() { return this.auth.user() ? this.auth.homeFor(this.auth.user()!.profile) : '/login'; }
  initials() { return (this.auth.user()?.displayName ?? 'AC').split(' ').slice(0, 2).map(x => x[0]).join('').toUpperCase(); }
  profileLabel() { return ({ Administracion: 'Secretaría / Dirección', Docente: 'Docente', Preceptor: 'Preceptoría', Responsable: 'Familia' } as Record<string, string>)[this.auth.user()?.profile ?? ''] ?? ''; }
  navigation() {
    const profile = this.auth.user()?.profile;
    if (profile === 'Administracion') return [{ path: '/administracion', label: 'Panel de gestión', icon: 'pi pi-th-large' }];
    if (profile === 'Docente') return [{ path: '/docente', label: 'Calificaciones', icon: 'pi pi-pencil' }];
    if (profile === 'Preceptor') return [{ path: '/preceptor', label: 'Asistencia', icon: 'pi pi-check-square' }];
    return [{ path: '/familia', label: 'Mis estudiantes', icon: 'pi pi-users' }];
  }
  logout() { this.auth.logout().subscribe(() => this.router.navigateByUrl('/login')); }
}
