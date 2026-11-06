import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Paged, ReportCard } from '../core/models';
import { CourseFilterComponent } from '../shared/course-filter.component';
import { PagerComponent } from '../shared/pager.component';
import { ReportCardComponent } from '../shared/report-card.component';

/** Fila de la tabla de alumnos (GET /api/students). */
interface StudentRow {
  studentId: number;
  fullName: string;
  dni: string;
  courseId: number;
  nivel: string;
  course: string;
  absences: number;
  acknowledged: boolean;
}

interface StudentsPage extends Paged<StudentRow> {
  year: { id: number; year: number; isActive: boolean; publishedStage: number };
}

/**
 * Boletines (Administración, Dirección, Secretaría y Preceptoría):
 *  1. Arriba, filtros: ciclo lectivo, nivel, año, curso y búsqueda por nombre o DNI.
 *  2. En el medio, la tabla de alumnos, paginada con flechas, con un botón "Ver boletín" por fila.
 *  3. Abajo, el boletín: vacío al principio; se carga al tocar "Ver boletín".
 */
@Component({
  selector: 'app-report-cards',
  imports: [FormsModule, CourseFilterComponent, PagerComponent, ReportCardComponent],
  template: `
    <header class="page-header no-print">
      <div>
        <h1>Boletines</h1>
        <p class="muted">Filtrá, elegí un alumno y su boletín aparece abajo.</p>
      </div>
    </header>

    @if (error()) { <div class="alert error no-print">{{ error() }}</div> }
    @if (message()) { <div class="alert success no-print"><i class="pi pi-check-circle"></i>{{ message() }}</div> }

    <!-- ===== 1. Filtros ===== -->
    <section class="card no-print">
      <div class="filters">
        <input class="search" [(ngModel)]="search" (keyup.enter)="goTo(1)" placeholder="Buscar por nombre o DNI">
        <app-course-filter [showYear]="true" [(yearId)]="yearId" [(nivel)]="nivel" [(level)]="level" [(courseId)]="courseId" (changed)="goTo(1)" />
        <button class="button secondary" (click)="goTo(1)"><i class="pi pi-search"></i> Buscar</button>
        <button class="button secondary" (click)="clear()">Limpiar</button>
      </div>

      <!-- ===== 2. Tabla de alumnos ===== -->
      <div class="table-scroll">
        <table class="simple-table">
          <thead>
            <tr><th>Alumno/a</th><th>DNI</th><th>Nivel</th><th>Curso</th><th>Faltas</th><th>Lectura</th><th></th></tr>
          </thead>
          <tbody>
            @for (s of result()?.items ?? []; track s.studentId) {
              <tr [class.selected-row]="selectedId() === s.studentId">
                <td><b>{{ s.fullName }}</b></td>
                <td>{{ s.dni }}</td>
                <td>{{ s.nivel }}</td>
                <td>{{ s.course }}</td>
                <td>{{ s.absences }}</td>
                <td>
                  @if (s.acknowledged) { <span class="tag ok" title="La familia confirmó la lectura del boletín publicado">✓ Leído</span> }
                  @else { <span class="muted">—</span> }
                </td>
                <td class="row-actions"><button class="button small primary" (click)="open(s)"><i class="pi pi-eye"></i> Ver boletín</button></td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="muted">No hay alumnos con esos filtros.</td></tr>
            }
          </tbody>
        </table>
      </div>

      @if (result(); as r) {
        <div class="action-bar">
          <app-pager [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="goTo($event)" />
          <div class="actions">
            <button class="button secondary small" (click)="exportCsv()"><i class="pi pi-file-excel"></i> Exportar lista</button>
            <button class="button secondary small" (click)="printCourse()" [disabled]="!courseId()" [title]="courseId() ? '' : 'Elegí un curso'">
              <i class="pi pi-print"></i> Imprimir todo el curso
            </button>
          </div>
        </div>
      }
    </section>

    <!-- ===== 3. Boletín(es) ===== -->
    <section #cardsPanel class="split-main">
      @for (card of cards(); track card.studentId) {
        <app-report-card [card]="card" />
      } @empty {
        <div class="card empty-card no-print">
          <i class="pi pi-file"></i>
          <p>Tocá <b>Ver boletín</b> en un alumno de la tabla para verlo acá.</p>
        </div>
      }

      @if (cards().length === 1) {
        <div class="card no-print">
          <div class="actions">
            <button class="button secondary" (click)="print()"><i class="pi pi-print"></i> Imprimir este boletín</button>
          </div>
          <!-- Observaciones generales de Dirección (solo Administración/Dirección) -->
          @if (auth.can('manageAcademic') && cards()[0].isCurrentYear) {
            <label class="spaced-top">Observaciones de Dirección (se imprimen en el boletín)
              <textarea rows="3" [(ngModel)]="comment" maxlength="1000"></textarea>
            </label>
            <button class="button primary small" (click)="saveComment()">Guardar observación</button>
          }
        </div>
      }
    </section>
  `
})
export class ReportCardsPage {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly cardsPanel = viewChild<ElementRef<HTMLElement>>('cardsPanel');

  // Filtros: los selects de curso son signals (model) que comparte <app-course-filter>.
  search = '';
  readonly yearId = signal<number | null>(null);
  readonly nivel = signal('');
  readonly level = signal<number | null>(null);
  readonly courseId = signal<number | null>(null);
  comment = '';

  readonly result = signal<StudentsPage | null>(null);
  /** Boletines visibles: uno (alumno elegido) o todos los del curso (para imprimir). */
  readonly cards = signal<ReportCard[]>([]);
  readonly selectedId = signal<number | null>(null);
  readonly error = signal('');
  readonly message = signal('');

  /** Arma los parámetros de búsqueda a partir de los filtros (omitimos los vacíos). */
  private params(page: number) {
    const p = new URLSearchParams({ page: String(page), pageSize: '15' });
    if (this.search.trim()) p.set('search', this.search.trim());
    if (this.yearId()) p.set('yearId', String(this.yearId()));
    if (this.nivel()) p.set('nivel', this.nivel());
    if (this.level() !== null) p.set('level', String(this.level()));
    if (this.courseId()) p.set('courseId', String(this.courseId()));
    return p;
  }

  goTo(page: number) {
    this.error.set('');
    this.api.get<StudentsPage>(`/api/students?${this.params(page)}`).subscribe({
      next: r => this.result.set(r),
      error: e => this.error.set(errorMessage(e))
    });
  }

  clear() {
    this.search = '';
    this.nivel.set('');
    this.level.set(null);
    this.courseId.set(null);
    this.goTo(1);
  }

  open(s: StudentRow) {
    this.selectedId.set(s.studentId);
    const year = this.yearId() ? `?yearId=${this.yearId()}` : '';
    this.api.get<ReportCard>(`/api/report-cards/${s.studentId}${year}`).subscribe({
      next: c => {
        this.cards.set([c]);
        this.comment = c.comment ?? '';
        // Llevamos la vista al boletín (está debajo de la tabla).
        setTimeout(() => this.cardsPanel()?.nativeElement.scrollIntoView({ behavior: 'smooth' }));
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Carga los boletines de todo el curso y abre la impresión (uno por hoja, ver styles.scss). */
  printCourse() {
    this.selectedId.set(null);
    this.api.get<ReportCard[]>(`/api/report-cards/course/${this.courseId()}`).subscribe({
      next: cards => {
        this.cards.set(cards);
        // Esperamos a que Angular dibuje los boletines antes de abrir el diálogo de impresión.
        setTimeout(() => window.print(), 400);
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Descarga la lista filtrada como CSV (se abre con Excel). La cookie viaja sola: es el mismo sitio. */
  exportCsv() {
    window.location.href = `/api/students/export?${this.params(1)}`;
  }

  saveComment() {
    const card = this.cards()[0];
    this.api.put(`/api/report-cards/${card.studentId}/comment`, { comment: this.comment, yearId: card.schoolYearId }).subscribe({
      next: () => {
        this.cards.set([{ ...card, comment: this.comment.trim() || null }]);
        this.message.set('Observación guardada.');
        setTimeout(() => this.message.set(''), 3000);
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  print() { window.print(); }
}
