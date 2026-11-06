import { inject, Injectable } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { ApiService } from './api.service';
import { FiltersData } from './models';

/**
 * Carga UNA vez los datos de los filtros (años lectivos, niveles y cursos visibles para el
 * usuario) y los comparte entre pantallas. shareReplay(1) guarda la última respuesta, así
 * pasar de Boletines a Asistencia no vuelve a pedirlos.
 */
@Injectable({ providedIn: 'root' })
export class FiltersService {
  private readonly api = inject(ApiService);
  private cache$: Observable<FiltersData> | null = null;

  load(): Observable<FiltersData> {
    this.cache$ ??= this.api.get<FiltersData>('/api/courses/filters').pipe(shareReplay(1));
    return this.cache$;
  }

  /** Olvida lo guardado (por ejemplo, después de crear un curso o cerrar sesión). */
  reset() { this.cache$ = null; }
}
