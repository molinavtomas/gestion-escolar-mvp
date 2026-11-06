import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

/**
 * Envoltorio mínimo sobre HttpClient. Todas las rutas empiezan con /api:
 * en desarrollo las redirige el proxy de Angular (proxy.conf.json) y en producción
 * la API y el frontend comparten dominio, así que no hace falta configurar URLs.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(url: string) { return this.http.get<T>(url); }
  post<T = void>(url: string, body: unknown = {}) { return this.http.post<T>(url, body); }
  put<T = void>(url: string, body: unknown) { return this.http.put<T>(url, body); }
  delete<T = void>(url: string) { return this.http.delete<T>(url); }
}

/**
 * Convierte un error HTTP en un mensaje para mostrar.
 * La API devuelve { message: "..." } en los errores de validación.
 */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.error?.message) return error.error.message;
    if (error.status === 0) return 'No hay conexión con el servidor.';
    if (error.status === 401) return 'Tu sesión expiró. Volvé a ingresar.';
    if (error.status === 403) return 'No tenés permiso para hacer esto.';
  }
  return 'No pudimos completar la operación. Probá de nuevo.';
}
