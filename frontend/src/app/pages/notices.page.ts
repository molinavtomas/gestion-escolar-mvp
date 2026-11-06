import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { CourseItem } from '../core/models';
import { FiltersService } from '../core/filters.service';
import { NoticesService } from '../core/notices.service';

interface Notice {
  id: number; kind: string; title: string; body: string;
  createdAtUtc: string; createdBy: string; student: string | null; course: string | null;
}
interface NoticesResponse { canSend: boolean; seenAtUtc: string | null; items: Notice[]; }

/**
 * Avisos: comunicados de Dirección, publicación del boletín e inasistencias.
 * Las familias los leen; Administración y Dirección además pueden enviar comunicados.
 */
@Component({
  selector: 'app-notices',
  imports: [FormsModule, DatePipe],
  template: `
    <header class="page-header"><h1>Avisos</h1></header>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error">{{ error() }}</div> }

    <!-- Envío de comunicados (solo conducción) -->
    @if (data()?.canSend) {
      <section class="card">
        <h2>Nuevo comunicado</h2>
        <div class="form-grid">
          <label>Destinatarios
            <select [(ngModel)]="courseId">
              <option [ngValue]="null">Todas las familias</option>
              @for (c of courses(); track c.id) { <option [ngValue]="c.id">Familias de {{ c.label }}</option> }
            </select>
          </label>
          <label>Título <input [(ngModel)]="title" maxlength="150"></label>
        </div>
        <label>Mensaje <textarea rows="4" [(ngModel)]="body" maxlength="2000"></textarea></label>
        <button class="button primary" (click)="send()" [disabled]="!title.trim() || !body.trim()"><i class="pi pi-send"></i> Enviar</button>
      </section>
    }

    <section class="card">
      @for (n of data()?.items ?? []; track n.id) {
        <article class="notice" [class.unread]="isNew(n)">
          <div class="notice-head">
            <span class="tag" [class]="kindClass(n.kind)">{{ n.kind }}</span>
            <strong>{{ n.title }}</strong>
            @if (isNew(n)) { <span class="tag new">Nuevo</span> }
          </div>
          <p>{{ n.body }}</p>
          <small class="muted">
            {{ n.createdAtUtc | date:'dd/MM/yyyy HH:mm' }} · {{ n.createdBy }}
            @if (n.student) { · {{ n.student }} }
            @if (n.course) { · {{ n.course }} } @else if (!n.student) { · Todo el colegio }
          </small>
          @if (data()?.canSend) {
            <button class="link-button danger-link" (click)="remove(n)">Eliminar</button>
          }
        </article>
      } @empty { <p class="muted">No hay avisos.</p> }
    </section>
  `
})
export class NoticesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly filters = inject(FiltersService);
  private readonly notices = inject(NoticesService);

  readonly data = signal<NoticesResponse | null>(null);
  readonly courses = signal<CourseItem[]>([]);
  readonly message = signal('');
  readonly error = signal('');

  courseId: number | null = null;
  title = '';
  body = '';

  ngOnInit() {
    this.load();
  }

  load() {
    this.api.get<NoticesResponse>('/api/notices').subscribe({
      next: d => {
        this.data.set(d);
        // Al abrir la pantalla, los avisos quedan marcados como vistos (se apaga el globito del menú).
        this.api.post('/api/notices/seen').subscribe(() => this.notices.refresh());
        if (d.canSend && this.courses().length === 0) {
          this.filters.load().subscribe(f => {
            const activeYear = f.years.find(y => y.isActive)?.id;
            this.courses.set(f.courses.filter(c => c.schoolYearId === activeYear));
          });
        }
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Nuevo = creado después de la última vez que se abrió esta pantalla. */
  isNew(n: Notice) {
    const seen = this.data()?.seenAtUtc;
    return !seen || new Date(n.createdAtUtc) > new Date(seen);
  }

  kindClass(kind: string) {
    return ({ 'Comunicado': 'kind-notice', 'Publicación': 'kind-publication', 'Inasistencia': 'kind-absence' } as Record<string, string>)[kind] ?? '';
  }

  send() {
    this.api.post('/api/notices', { title: this.title, body: this.body, courseId: this.courseId }).subscribe({
      next: () => {
        this.title = this.body = '';
        this.message.set('Comunicado enviado.');
        setTimeout(() => this.message.set(''), 3000);
        this.load();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  remove(n: Notice) {
    if (!confirm(`¿Eliminar el aviso "${n.title}"?`)) return;
    this.api.delete(`/api/notices/${n.id}`).subscribe({ next: () => this.load(), error: e => this.error.set(errorMessage(e)) });
  }
}
