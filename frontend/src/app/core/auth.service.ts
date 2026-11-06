import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, Observable, of, tap } from 'rxjs';
import { Permissions, Role, SessionUser } from './models';

/**
 * Hace que todas las llamadas HTTP envíen la cookie de sesión.
 * (En producción no es estrictamente necesario porque es el mismo dominio,
 * pero en desarrollo con `ng serve` sí.)
 */
export const credentialsInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ withCredentials: true }));

/**
 * Estado de la sesión. `user` es una signal: cualquier componente que la lea
 * se actualiza solo cuando cambia (al iniciar o cerrar sesión).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly user = signal<SessionUser | null>(null);

  login(dni: string, password: string): Observable<SessionUser> {
    return this.http.post<SessionUser>('/api/auth/login', { dni, password })
      .pipe(tap(user => this.user.set(user)));
  }

  /** Si ya sabemos quién es, lo devolvemos; si no, le preguntamos a la API (ej. al recargar la página). */
  ensure(): Observable<SessionUser | null> {
    if (this.user()) return of(this.user());
    return this.http.get<SessionUser>('/api/auth/me').pipe(
      tap(user => this.user.set(user)),
      catchError(() => of(null))
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(tap(() => this.user.set(null)));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/change-password', { currentPassword, newPassword }).pipe(
      // Ya no usa el DNI como clave: ocultamos el aviso.
      tap(() => this.user.update(u => u ? { ...u, usesDefaultPassword: false } : u))
    );
  }

  /** Pantalla inicial de cada rol. */
  homeFor(role: Role): string {
    switch (role) {
      case 'Administrador':
      case 'Director':
      case 'Secretaria':
      case 'Preceptor': return '/panel';
      case 'Profesor': return '/calificaciones';
      case 'Familia': return '/mi-boletin';
    }
  }

  /** Atajo para leer un permiso de la sesión: auth.can('managePeople'). */
  can(permission: keyof Permissions): boolean {
    return this.user()?.permissions[permission] ?? false;
  }
}
