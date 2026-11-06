import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, errorMessage } from '../core/api.service';
import { AuthService } from '../core/auth.service';

/** Cuenta de prueba (solo existe en modo demo). */
interface DemoAccount { label: string; dni: string; }

/**
 * Ingreso con DNI y clave. Las familias usan el DNI del alumno.
 * En modo demo muestra botones para entrar con cada rol.
 */
@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login-page">
      <form class="login-card" (ngSubmit)="login()">
        <img class="login-logo" src="/api/settings/logo" alt="Logo del colegio">
        <h1>{{ schoolName() }}</h1>
        <p class="muted">Boletines, calificaciones y asistencia</p>

        <label>
          DNI
          <input name="dni" [(ngModel)]="dni" inputmode="numeric" autocomplete="username" placeholder="Sin puntos" required>
        </label>
        <label>
          Clave
          <input name="password" type="password" [(ngModel)]="password" autocomplete="current-password" required>
        </label>
        <p class="hint">La primera vez, la clave es el mismo DNI. Las familias ingresan con el DNI del alumno.</p>

        @if (error()) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error() }}</div> }

        <button class="button primary wide" [disabled]="loading()">
          @if (loading()) { <i class="pi pi-spin pi-spinner"></i> } Ingresar
        </button>

        @if (demoAccounts().length) {
          <div class="demo-box">
            <small>Cuentas de prueba (clave = DNI):</small>
            <div class="demo-buttons">
              @for (account of demoAccounts(); track account.dni) {
                <button type="button" class="chip" (click)="useDemo(account)">{{ account.label }}</button>
              }
            </div>
          </div>
        }
      </form>
    </main>
  `
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  // Campos del formulario (propiedades simples porque los usa ngModel).
  dni = '';
  password = '';

  readonly loading = signal(false);
  readonly error = signal('');
  readonly demoAccounts = signal<DemoAccount[]>([]);
  readonly schoolName = signal('Colegio de los Buenos Ayres');

  ngOnInit() {
    // Si ya hay sesión abierta, vamos directo a la pantalla del rol.
    this.auth.ensure().subscribe(user => {
      if (user) this.router.navigateByUrl(this.auth.homeFor(user.role));
    });
    this.api.get<DemoAccount[]>('/api/auth/demo-accounts').subscribe(x => this.demoAccounts.set(x));
    // Nombre configurable por el Administrador (Administración > Configuración).
    this.api.get<{ schoolName: string }>('/api/settings/public').subscribe(s => this.schoolName.set(s.schoolName));
  }

  useDemo(account: DemoAccount) {
    this.dni = account.dni;
    this.password = account.dni;
    this.login();
  }

  login() {
    this.error.set('');
    this.loading.set(true);
    this.auth.login(this.dni, this.password).subscribe({
      next: user => this.router.navigateByUrl(this.auth.homeFor(user.role)),
      error: e => { this.error.set(errorMessage(e)); this.loading.set(false); }
    });
  }
}
