import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';

/** Avance de carga de una materia en un curso. Los números son "cantidad de alumnos con nota". */
interface ProgressRow {
  courseId: number; course: string; subjectId: number; subject: string; teacher: string;
  students: number; valuation1: number; grade1: number; valuation2: number; grade2: number; final: number;
}

interface Dashboard {
  year: number;
  publishedStage: number;
  stages: string[];
  totals: { students: number; teachers: number; courses: number };
  rows: ProgressRow[];
}

/** Columnas del tablero de avance (clave del dato + título). */
const PROGRESS_COLUMNS: { key: keyof ProgressRow; label: string }[] = [
  { key: 'valuation1', label: '1° valor.' },
  { key: 'grade1', label: '1° cuatr.' },
  { key: 'valuation2', label: '2° valor.' },
  { key: 'grade2', label: '2° cuatr.' },
  { key: 'final', label: 'Final' }
];

/**
 * Panel de Dirección/Secretaría:
 *  - qué ven las familias (etapa publicada del boletín);
 *  - avance de carga de notas por curso y materia, para detectar quién falta cargar.
 */
@Component({
  selector: 'app-dashboard',
  imports: [FormsModule],
  template: `
    @if (data(); as d) {
      <header class="page-header">
        <div>
          <h1>Panel</h1>
          <p class="muted">Ciclo lectivo {{ d.year }}</p>
        </div>
      </header>

      @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      <div class="stats">
        <div class="stat"><strong>{{ d.totals.students }}</strong><span>alumnos</span></div>
        <div class="stat"><strong>{{ d.totals.teachers }}</strong><span>profesores</span></div>
        <div class="stat"><strong>{{ d.totals.courses }}</strong><span>cursos</span></div>
        <div class="stat"><strong>{{ missingTeachers() }}</strong><span>materias sin profesor</span></div>
      </div>

      <!-- Publicación del boletín -->
      <section class="card">
        <h2>¿Qué ven las familias?</h2>
        <p class="muted">Las familias ven el boletín solo hasta la etapa elegida. Profesores pueden seguir cargando igual.</p>
        <div class="actions">
          <select [(ngModel)]="stage">
            @for (label of d.stages; track $index) { <option [ngValue]="$index">{{ label }}</option> }
          </select>
          <button class="button primary" (click)="publish()" [disabled]="stage === d.publishedStage">Publicar</button>
        </div>
      </section>

      <!-- Avance de carga -->
      <section class="card">
        <div class="card-title">
          <h2>Avance de carga de notas</h2>
          <label class="inline">Curso
            <select [(ngModel)]="courseFilter">
              <option [ngValue]="null">Todos</option>
              @for (c of courseNames(); track c) { <option [ngValue]="c">{{ c }}</option> }
            </select>
          </label>
        </div>
        <p class="muted">Cantidad de alumnos con nota cargada en cada columna. En verde: completo.</p>
        <div class="table-scroll">
          <table class="simple-table progress-table">
            <thead>
              <tr>
                <th>Curso</th><th>Materia</th><th>Profesor/a</th>
                @for (col of columns; track col.key) { <th>{{ col.label }}</th> }
              </tr>
            </thead>
            <tbody>
              @for (row of filteredRows(); track row.courseId + '-' + row.subjectId) {
                <tr>
                  <td>{{ row.course }}</td>
                  <td>{{ row.subject }}</td>
                  <td [class.low]="!row.teacher">{{ row.teacher || 'Sin asignar' }}</td>
                  @for (col of columns; track col.key) {
                    <td [class]="cellClass(row, row[col.key])">{{ row[col.key] }}/{{ row.students }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    } @else if (error()) {
      <div class="alert error">{{ error() }}</div>
    } @else {
      <p class="muted">Cargando…</p>
    }
  `
})
export class DashboardPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly columns = PROGRESS_COLUMNS;

  readonly data = signal<Dashboard | null>(null);
  readonly message = signal('');
  readonly error = signal('');

  stage = 0;
  courseFilter: string | null = null;

  /** Nombres de curso distintos, para el filtro. */
  readonly courseNames = computed(() => [...new Set(this.data()?.rows.map(r => r.course) ?? [])]);
  readonly missingTeachers = computed(() => this.data()?.rows.filter(r => !r.teacher).length ?? 0);

  ngOnInit() { this.load(); }

  load() {
    this.api.get<Dashboard>('/api/academic/dashboard').subscribe({
      next: d => { this.data.set(d); this.stage = d.publishedStage; },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Filtrado en el template (no es computed porque courseFilter es una propiedad simple de ngModel). */
  filteredRows() {
    const rows = this.data()?.rows ?? [];
    return this.courseFilter ? rows.filter(r => r.course === this.courseFilter) : rows;
  }

  /** Verde si están todos, amarillo si hay algunos, sin color si no hay ninguno. */
  cellClass(row: ProgressRow, value: string | number) {
    const n = Number(value);
    if (row.students === 0 || n === 0) return 'cell-empty';
    return n >= row.students ? 'cell-done' : 'cell-partial';
  }

  publish() {
    this.api.put('/api/academic/publication', { stage: this.stage }).subscribe({
      next: () => {
        this.message.set('Publicación actualizada. Las familias ya ven el cambio.');
        setTimeout(() => this.message.set(''), 3000);
        this.load();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }
}
