import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';

/** Avance de carga de una materia en un curso. Los números son "cantidad de alumnos con nota". */
interface ProgressRow {
  courseId: number; nivel: string; course: string; subjectId: number; subject: string; teacher: string;
  students: number; valuation1: number; grade1: number; valuation2: number; grade2: number; final: number;
}

interface Dashboard {
  year: number;
  publishedStage: number;
  stages: string[];
  closed: { term1: boolean; term2: boolean; final: boolean };
  canManage: boolean;
  absenceThreshold: number;
  absenceAlerts: { studentId: number; fullName: string; course: string; absences: number }[];
  totals: { students: number; teachers: number; courses: number };
  rows: ProgressRow[];
}

/** Columnas del tablero de avance (clave del dato + título). */
const PROGRESS_COLUMNS: { key: 'valuation1' | 'grade1' | 'valuation2' | 'grade2' | 'final'; label: string }[] = [
  { key: 'valuation1', label: '1° valor.' },
  { key: 'grade1', label: '1° cuatr.' },
  { key: 'valuation2', label: '2° valor.' },
  { key: 'grade2', label: '2° cuatr.' },
  { key: 'final', label: 'Final' }
];

/**
 * Panel. Lo ven Administración, Dirección, Secretaría y Preceptoría.
 *  - Publicación del boletín y cierre de carga: solo Administración/Dirección (el resto lo ve en modo lectura).
 *  - Alerta de alumnos con muchas faltas.
 *  - Avance de carga de notas por curso y materia, para detectar quién falta cargar.
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
        <div class="stat"><strong>{{ d.absenceAlerts.length }}</strong><span>alumnos con {{ d.absenceThreshold }}+ faltas</span></div>
      </div>

      <div class="two-columns">
        <!-- Publicación del boletín -->
        <section class="card">
          <h2>¿Qué ven las familias?</h2>
          @if (d.canManage) {
            <p class="muted">Las familias ven el boletín solo hasta la etapa elegida. Al publicar algo nuevo reciben un aviso.</p>
            <div class="actions">
              <select [(ngModel)]="stage">
                @for (label of d.stages; track $index) { <option [ngValue]="$index">{{ label }}</option> }
              </select>
              <button class="button primary" (click)="publish()" [disabled]="stage === d.publishedStage">Publicar</button>
            </div>
          } @else {
            <p><b>{{ d.stages[d.publishedStage] }}</b></p>
          }
        </section>

        <!-- Cierre de carga -->
        <section class="card">
          <h2>Cierre de carga</h2>
          <p class="muted">Con un período cerrado, los profesores ya no pueden modificar esas notas. Dirección sí puede corregir.</p>
          <div class="checks">
            <label class="check"><input type="checkbox" [(ngModel)]="closed.term1" [disabled]="!d.canManage"> 1° cuatrimestre cerrado</label>
            <label class="check"><input type="checkbox" [(ngModel)]="closed.term2" [disabled]="!d.canManage"> 2° cuatrimestre cerrado</label>
            <label class="check"><input type="checkbox" [(ngModel)]="closed.final" [disabled]="!d.canManage"> Diciembre, febrero y final cerrados</label>
          </div>
          @if (d.canManage) {
            <button class="button secondary small" (click)="saveClosing()">Guardar cierre</button>
          }
        </section>
      </div>

      <!-- Alerta de inasistencias -->
      @if (d.absenceAlerts.length) {
        <section class="card">
          <h2>Alumnos con muchas inasistencias ({{ d.absenceThreshold }} o más)</h2>
          <table class="simple-table">
            <thead><tr><th>Alumno/a</th><th>Curso</th><th>Faltas</th></tr></thead>
            <tbody>
              @for (a of d.absenceAlerts; track a.studentId) {
                <tr><td>{{ a.fullName }}</td><td>{{ a.course }}</td><td class="low">{{ a.absences }}</td></tr>
              }
            </tbody>
          </table>
        </section>
      }

      <!-- Avance de carga -->
      <section class="card">
        <div class="card-title">
          <h2>Avance de carga de notas</h2>
          <div class="actions">
            <label class="inline">Nivel
              <select [(ngModel)]="nivelFilter" (ngModelChange)="courseFilter = null">
                <option [ngValue]="null">Todos</option>
                @for (n of niveles(); track n) { <option [ngValue]="n">{{ n }}</option> }
              </select>
            </label>
            <label class="inline">Curso
              <select [(ngModel)]="courseFilter">
                <option [ngValue]="null">Todos</option>
                @for (c of courseNames(); track c) { <option [ngValue]="c">{{ c }}</option> }
              </select>
            </label>
            <label class="check"><input type="checkbox" [(ngModel)]="onlyPending"> Solo con carga pendiente</label>
          </div>
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
              } @empty { <tr><td colspan="8" class="muted">No hay filas con esos filtros.</td></tr> }
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

  // Campos de formulario (ngModel).
  stage = 0;
  closed = { term1: false, term2: false, final: false };
  nivelFilter: string | null = null;
  courseFilter: string | null = null;
  onlyPending = false;

  readonly niveles = computed(() => [...new Set(this.data()?.rows.map(r => r.nivel) ?? [])]);
  readonly missingTeachers = computed(() => this.data()?.rows.filter(r => !r.teacher).length ?? 0);

  ngOnInit() { this.load(); }

  load() {
    this.api.get<Dashboard>('/api/academic/dashboard').subscribe({
      next: d => { this.data.set(d); this.stage = d.publishedStage; this.closed = { ...d.closed }; },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Cursos del nivel elegido (para el segundo filtro). Es un método porque nivelFilter no es signal. */
  courseNames() {
    const rows = (this.data()?.rows ?? []).filter(r => !this.nivelFilter || r.nivel === this.nivelFilter);
    return [...new Set(rows.map(r => r.course))];
  }

  filteredRows() {
    return (this.data()?.rows ?? []).filter(r =>
      (!this.nivelFilter || r.nivel === this.nivelFilter) &&
      (!this.courseFilter || r.course === this.courseFilter) &&
      (!this.onlyPending || r.grade1 < r.students || r.grade2 < r.students || !r.teacher));
  }

  /** Verde si están todos, amarillo si hay algunos, sin color si no hay ninguno. */
  cellClass(row: ProgressRow, value: number) {
    if (row.students === 0 || value === 0) return 'cell-empty';
    return value >= row.students ? 'cell-done' : 'cell-partial';
  }

  publish() {
    this.api.put('/api/academic/publication', { stage: this.stage }).subscribe({
      next: () => this.ok('Publicación actualizada. Las familias ya ven el cambio.'),
      error: e => this.error.set(errorMessage(e))
    });
  }

  saveClosing() {
    this.api.put('/api/academic/closing', this.closed).subscribe({
      next: () => this.ok('Cierre de carga actualizado.'),
      error: e => this.error.set(errorMessage(e))
    });
  }

  private ok(text: string) {
    this.error.set('');
    this.message.set(text);
    setTimeout(() => this.message.set(''), 3000);
    this.load();
  }
}
