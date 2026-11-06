import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/api.service';
import { AuthService } from '../core/auth.service';

/** Cambio de clave voluntario. */
@Component({
  selector: 'app-change-password',
  imports: [FormsModule],
  template: `
    <header class="page-header"><h1>Cambiar clave</h1></header>
    <form class="card narrow" (ngSubmit)="save()">
      <label>Clave actual <input type="password" name="current" [(ngModel)]="current" autocomplete="current-password" required></label>
      <label>Nueva clave <input type="password" name="next" [(ngModel)]="next" autocomplete="new-password" minlength="6" required></label>
      <label>Repetir nueva clave <input type="password" name="repeat" [(ngModel)]="repeat" autocomplete="new-password" required></label>
      <p class="hint">Mínimo 6 caracteres. Si te la olvidás, Secretaría puede volverla a tu DNI.</p>

      @if (error()) { <div class="alert error">{{ error() }}</div> }
      @if (done()) { <div class="alert success">Listo, tu clave fue cambiada.</div> }

      <button class="button primary" [disabled]="saving()">Guardar</button>
    </form>
  `
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);

  current = '';
  next = '';
  repeat = '';

  readonly saving = signal(false);
  readonly error = signal('');
  readonly done = signal(false);

  save() {
    this.error.set('');
    this.done.set(false);
    // Validaciones rápidas en el navegador; la API vuelve a validar.
    const problem =
      this.next.length < 6 ? 'La nueva clave debe tener al menos 6 caracteres.' :
      this.next !== this.repeat ? 'Las claves nuevas no coinciden.' :
      this.next === this.auth.user()?.dni ? 'La nueva clave no puede ser tu DNI.' : '';
    if (problem) {
      this.error.set(problem);
      return;
    }

    this.saving.set(true);
    this.auth.changePassword(this.current, this.next).subscribe({
      next: () => {
        this.done.set(true);
        this.saving.set(false);
        this.current = this.next = this.repeat = '';
      },
      error: e => { this.error.set(errorMessage(e)); this.saving.set(false); }
    });
  }
}
