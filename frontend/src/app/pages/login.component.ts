import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login-page">
      <section class="login-story">
        <div class="story-content">
          <div class="brand brand-light"><span class="brand-mark"><i class="pi pi-graduation-cap"></i></span><span><strong>Aula Clara</strong><small>Gestión escolar simple</small></span></div>
          <div>
            <span class="eyebrow light">Ciclo lectivo 2026</span>
            <h1>Todo lo importante de la escuela, en un solo lugar.</h1>
            <p>Personas, cursos, notas y asistencia conectados para que cada rol vea exactamente lo que necesita.</p>
          </div>
          <div class="story-stats"><div><b>4</b><span>perfiles</span></div><div><b>100%</b><span>web</span></div><div><b>1</b><span>vista clara</span></div></div>
        </div>
        <div class="orb orb-one"></div><div class="orb orb-two"></div>
      </section>
      <section class="login-panel">
        <form class="login-card" (ngSubmit)="login()">
          <div><span class="eyebrow">Acceso al sistema</span><h2>Bienvenido de nuevo</h2><p>Elegí un perfil de prueba o ingresá tus datos.</p></div>
          <div class="demo-accounts">
            @for (account of accounts; track account.email) {
              <button type="button" [class.selected]="email === account.email" (click)="selectAccount(account.email)"><i [class]="account.icon"></i><span>{{ account.label }}</span></button>
            }
          </div>
          <label>Email<input type="email" name="email" [(ngModel)]="email" autocomplete="username" required></label>
          <label>Contraseña<div class="password-field"><input [type]="showPassword ? 'text' : 'password'" name="password" [(ngModel)]="password" autocomplete="current-password" required><button type="button" (click)="showPassword = !showPassword"><i [class]="showPassword ? 'pi pi-eye-slash' : 'pi pi-eye'"></i></button></div></label>
          @if (error) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error }}</div> }
          <button class="button primary wide" [disabled]="loading">@if (loading) { <i class="pi pi-spin pi-spinner"></i> } @else { <i class="pi pi-arrow-right"></i> } Ingresar</button>
          <p class="demo-note"><i class="pi pi-info-circle"></i> Todas las cuentas demo usan <code>Demo1234!</code></p>
        </form>
      </section>
    </main>
  `
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  email = 'secretaria@demo.edu'; password = 'Demo1234!'; loading = false; error = ''; showPassword = false;
  readonly accounts = [
    { label: 'Secretaría', email: 'secretaria@demo.edu', icon: 'pi pi-building' },
    { label: 'Docente', email: 'docente@demo.edu', icon: 'pi pi-book' },
    { label: 'Preceptor', email: 'preceptor@demo.edu', icon: 'pi pi-check-square' },
    { label: 'Familia', email: 'familia@demo.edu', icon: 'pi pi-home' }
  ];
  selectAccount(email: string) { this.email = email; this.password = 'Demo1234!'; }
  login() {
    this.error = ''; this.loading = true;
    this.auth.login(this.email, this.password).pipe(finalize(() => this.loading = false)).subscribe({
      next: user => this.router.navigateByUrl(this.auth.homeFor(user.profile)),
      error: error => this.error = error.error?.message ?? 'No pudimos iniciar sesión.'
    });
  }
}
