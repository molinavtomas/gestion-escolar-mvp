import { inject, Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';

/**
 * Cantidad de avisos sin leer, para el globito del menú "Avisos".
 * Se actualiza al entrar al sistema y cada vez que se abre la pantalla de avisos.
 */
@Injectable({ providedIn: 'root' })
export class NoticesService {
  private readonly api = inject(ApiService);
  readonly unread = signal(0);

  refresh() {
    this.api.get<{ count: number }>('/api/notices/unread-count').subscribe({
      next: r => this.unread.set(r.count),
      error: () => this.unread.set(0)
    });
  }
}
