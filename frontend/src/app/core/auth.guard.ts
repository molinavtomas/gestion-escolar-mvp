import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';
import { Permissions } from './models';

/** Deja pasar solo si hay sesión; si no, manda al login. */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ensure().pipe(map(user => user ? true : router.createUrlTree(['/login'])));
};

/**
 * Deja pasar solo si el usuario tiene ALGUNO de los permisos indicados (ver models.ts).
 * Si alguien escribe a mano una URL que no le corresponde, lo manda a su pantalla de inicio.
 * Ojo: esto es solo comodidad de la interfaz; la seguridad real está en la API,
 * que vuelve a verificar el rol en cada request.
 */
export function permissionGuard(...permissions: (keyof Permissions)[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const user = auth.user();
    if (user && permissions.some(p => user.permissions[p])) return true;
    return router.createUrlTree([user ? auth.homeFor(user.role) : '/login']);
  };
}

/** Redirige la raíz "/" a la pantalla del rol. */
export const homeRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();
  return router.createUrlTree([user ? auth.homeFor(user.role) : '/login']);
};
