import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { ApplicationRef, computed, inject, Injectable, signal } from '@angular/core';
import { catchError, finalize, Observable, of, tap } from 'rxjs';

export interface SessionUser {
  id: number; email: string; displayName: string;
  profile: 'Administracion' | 'Docente' | 'Preceptor' | 'Responsable';
  personId: number;
}

export const credentialsInterceptor: HttpInterceptorFn = (request, next) => {
  const app = inject(ApplicationRef);
  return next(request.clone({ withCredentials: true })).pipe(finalize(() => app.tick()));
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly user = signal<SessionUser | null>(null);
  readonly authenticated = computed(() => this.user() !== null);

  login(email: string, password: string): Observable<SessionUser> {
    return this.http.post<SessionUser>('/api/auth/login', { email, password }).pipe(tap(user => this.user.set(user)));
  }

  ensure(): Observable<SessionUser | null> {
    if (this.user()) return of(this.user());
    return this.http.get<SessionUser>('/api/auth/me').pipe(tap(user => this.user.set(user)), catchError(() => of(null)));
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(tap(() => this.user.set(null)));
  }

  homeFor(profile: SessionUser['profile']): string {
    return ({ Administracion: '/administracion', Docente: '/docente', Preceptor: '/preceptor', Responsable: '/familia' })[profile];
  }
}
