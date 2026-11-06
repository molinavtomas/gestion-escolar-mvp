import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { GradeValues } from '../core/models';

/** Una materia de un curso que el usuario puede cargar. */
interface SubjectOption { courseId: number; course: string; subjectId: number; subject: string; }

/** Una fila de la grilla: un alumno con sus notas en la materia elegida. */
interface GridRow {
  enrollmentId: number;
  studentId: number;
  student: string;
  dni: string;
  values: GradeValues;
  updatedAtUtc: string | null;
  updatedBy: string | null;
}

interface Change { field: string; oldValue: string | null; newValue: string | null; changedBy: string; changedAtUtc: string; }

/** Opciones de la valoración preliminar: conceptual (Bs As) o numérica. */
const VALUATIONS = ['TEA', 'TEP', 'TED', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10'];

/**
 * Carga de calificaciones. El profesor elige una de SUS materias y completa una grilla
 * con las mismas columnas del boletín. Dirección/Secretaría pueden elegir cualquier materia.
 */
@Component({
  selector: 'app-grades',
  imports: [FormsModule, DatePipe],
  template: `
    <header class="page-header">
      <div>
        <h1>Calificaciones</h1>
        <p class="muted">Elegí la materia, completá las columnas y tocá <b>Guardar</b>. Se aprueba con 7.</p>
      </div>
      <label class="inline">Materia
        <select [ngModel]="selectedKey()" (ngModelChange)="select($event)">
          @for (o of options(); track key(o)) {
            <option [value]="key(o)">{{ o.course }} · {{ o.subject }}</option>
          }
        </select>
      </label>
    </header>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error() }}</div> }

    @if (options().length === 0 && loaded()) {
      <div class="card"><p>No tenés materias asignadas. Pedile a Secretaría que te asigne tus cursos.</p></div>
    }

    @if (selected(); as s) {
      <section class="card">
        <h2>{{ s.course }} · {{ s.subject }}</h2>
        <div class="table-scroll">
          <table class="grid-table">
            <thead>
              <tr>
                <th rowspan="2">Alumno/a</th>
                <th colspan="3">1° cuatrimestre</th>
                <th colspan="3">2° cuatrimestre</th>
                <th colspan="2">Intensificación</th>
                <th rowspan="2">Final</th>
              </tr>
              <tr>
                <th title="1° valoración preliminar">Valor.</th><th>Calif.</th><th title="Intensificación 1° cuatrimestre">Intens.</th>
                <th title="2° valoración preliminar">Valor.</th><th>Calif.</th><th title="Intensificación 2° cuatrimestre">Intens.</th>
                <th>Dic.</th><th>Feb.</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.enrollmentId) {
                <tr>
                  <td>
                    <button class="link-button" (click)="showHistory(row)" title="Ver historial de cambios">{{ row.student }}</button>
                  </td>
                  <td><select [(ngModel)]="row.values.valuation1" (ngModelChange)="touch()"><option [ngValue]="null">–</option>@for (v of valuations; track v) {<option [ngValue]="v">{{ v }}</option>}</select></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.grade1" (ngModelChange)="touch()" [class.low]="isLow(row.values.grade1)"></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.intensification1" (ngModelChange)="touch()"></td>
                  <td><select [(ngModel)]="row.values.valuation2" (ngModelChange)="touch()"><option [ngValue]="null">–</option>@for (v of valuations; track v) {<option [ngValue]="v">{{ v }}</option>}</select></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.grade2" (ngModelChange)="touch()" [class.low]="isLow(row.values.grade2)"></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.intensification2" (ngModelChange)="touch()"></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.december" (ngModelChange)="touch()"></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.february" (ngModelChange)="touch()"></td>
                  <td><input type="number" min="1" max="10" [(ngModel)]="row.values.finalGrade" (ngModelChange)="touch()" [class.low]="isLow(row.values.finalGrade)"></td>
                </tr>
              } @empty {
                <tr><td colspan="10" class="muted">Este curso no tiene alumnos.</td></tr>
              }
            </tbody>
          </table>
        </div>

        <div class="action-bar">
          <span class="muted">{{ dirty() ? 'Hay cambios sin guardar.' : 'Todo guardado.' }} Tocá el nombre de un alumno para ver su historial.</span>
          <button class="button primary" (click)="save()" [disabled]="saving() || !dirty()">
            <i class="pi pi-save"></i> Guardar
          </button>
        </div>
      </section>

      <!-- Historial de cambios del alumno elegido -->
      @if (history(); as h) {
        <section class="card">
          <div class="card-title">
            <h2>Historial · {{ h.student }}</h2>
            <button class="icon-button" (click)="history.set(null)" aria-label="Cerrar"><i class="pi pi-times"></i></button>
          </div>
          @if (h.changes.length) {
            <table class="simple-table">
              <thead><tr><th>Fecha</th><th>Columna</th><th>Antes</th><th>Después</th><th>Quién</th></tr></thead>
              <tbody>
                @for (c of h.changes; track $index) {
                  <tr><td>{{ c.changedAtUtc | date:'dd/MM/yyyy HH:mm' }}</td><td>{{ c.field }}</td><td>{{ c.oldValue ?? '–' }}</td><td>{{ c.newValue ?? '–' }}</td><td>{{ c.changedBy }}</td></tr>
                }
              </tbody>
            </table>
          } @else { <p class="muted">Sin cambios registrados.</p> }
        </section>
      }
    }
  `
})
export class GradesPage implements OnInit {
  private readonly api = inject(ApiService);

  readonly valuations = VALUATIONS;
  readonly options = signal<SubjectOption[]>([]);
  readonly selectedKey = signal('');
  readonly selected = computed(() => this.options().find(o => this.key(o) === this.selectedKey()) ?? null);
  readonly rows = signal<GridRow[]>([]);
  readonly history = signal<{ student: string; changes: Change[] } | null>(null);

  readonly loaded = signal(false);
  readonly dirty = signal(false);
  readonly saving = signal(false);
  readonly message = signal('');
  readonly error = signal('');

  ngOnInit() {
    this.api.get<SubjectOption[]>('/api/grades/my-subjects').subscribe({
      next: options => {
        this.options.set(options);
        this.loaded.set(true);
        if (options.length) this.select(this.key(options[0]), true);
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Clave única de una opción (curso + materia), para usar como valor del <select>. */
  key(o: SubjectOption) { return `${o.courseId}-${o.subjectId}`; }

  select(key: string, force = false) {
    // Evita perder lo escrito por cambiar de materia sin querer.
    if (!force && this.dirty() && !confirm('Hay cambios sin guardar. ¿Descartarlos?')) {
      // Re-emitimos la clave actual para que el <select> vuelva a la materia anterior.
      const current = this.selectedKey();
      this.selectedKey.set('');
      setTimeout(() => this.selectedKey.set(current));
      return;
    }
    this.selectedKey.set(key);
    this.loadGrid();
  }

  loadGrid() {
    const s = this.selected();
    if (!s) return;
    this.history.set(null);
    this.error.set('');
    this.api.get<GridRow[]>(`/api/grades/grid?courseId=${s.courseId}&subjectId=${s.subjectId}`).subscribe({
      next: rows => { this.rows.set(rows); this.dirty.set(false); },
      error: e => this.error.set(errorMessage(e))
    });
  }

  touch() { this.dirty.set(true); }

  isLow(value: number | null) { return value !== null && value !== undefined && value < 7; }

  save() {
    const s = this.selected();
    if (!s) return;
    this.saving.set(true);
    this.error.set('');

    // Los <input type="number"> vacíos pueden quedar como "" o undefined: los pasamos a null.
    const clean = (v: unknown) => (v === '' || v === undefined ? null : v);
    const rows = this.rows().map(r => ({
      enrollmentId: r.enrollmentId,
      values: Object.fromEntries(Object.entries(r.values).map(([k, v]) => [k, clean(v)]))
    }));

    this.api.post('/api/grades/grid', { courseId: s.courseId, subjectId: s.subjectId, rows }).subscribe({
      next: () => {
        this.saving.set(false);
        this.dirty.set(false);
        this.flash('Calificaciones guardadas.');
      },
      error: e => { this.saving.set(false); this.error.set(errorMessage(e)); }
    });
  }

  showHistory(row: GridRow) {
    this.api.get<Change[]>(`/api/grades/history?enrollmentId=${row.enrollmentId}&subjectId=${this.selected()?.subjectId}`).subscribe({
      next: changes => this.history.set({ student: row.student, changes }),
      error: e => this.error.set(errorMessage(e))
    });
  }

  private flash(text: string) {
    this.message.set(text);
    setTimeout(() => this.message.set(''), 3000);
  }
}
