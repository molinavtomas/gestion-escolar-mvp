import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, Observable, of, tap, throwError } from 'rxjs';
import { Permissions, Role, SessionUser } from './models';

/**
 * Hace que todas las llamadas HTTP envíen la cookie de sesión.
 * (En producción no es estrictamente necesario porque es el mismo dominio,
 * pero en desarrollo con `ng serve` sí.)
 */
export const credentialsInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ withCredentials: true }));

/**
 * Si la API responde 401 (sesión vencida o cerrada) en medio del uso, vamos al login
 * en lugar de mostrar un error en la pantalla. Se excluyen las llamadas de /api/auth,
 * que ya manejan el 401 por su cuenta (ej. clave incorrecta).
 */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !request.url.includes('/api/auth/')) {
        auth.user.set(null);
        router.navigateByUrl('/login');
      }
      return throwError(() => error);
    })
  );
};

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
