import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { CourseOption } from '../core/models';

interface AttendanceRow { enrollmentId: number; student: string; dni: string; status: string | null; observation: string | null; }
interface SummaryRow { student: string; present: number; absent: number; late: number; justified: number; }

const STATUSES = [
  { value: 'Presente', short: 'P', css: 'present' },
  { value: 'Ausente', short: 'A', css: 'absent' },
  { value: 'Tarde', short: 'T', css: 'late' },
  { value: 'Justificado', short: 'J', css: 'justified' }
];

/** Fecha de hoy en formato yyyy-mm-dd según la hora local (no UTC). */
function today(): string {
  const d = new Date();
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}

/**
 * Asistencia diaria. El preceptor elige curso y fecha, marca a cada alumno y guarda.
 * Volver a guardar la misma fecha corrige lo anterior.
 */
@Component({
  selector: 'app-attendance',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <h1>Asistencia</h1>
      <div class="actions">
        <label class="inline">Curso
          <select [(ngModel)]="courseId" (ngModelChange)="load()">
            @for (c of courses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
          </select>
        </label>
        <label class="inline">Fecha <input type="date" [(ngModel)]="date" (ngModelChange)="load()"></label>
      </div>
    </header>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error">{{ error() }}</div> }

    <section class="card">
      <div class="card-title">
        <h2>Lista del curso</h2>
        <div class="actions">
          <span class="muted">{{ counts() }}</span>
          <button class="button secondary small" (click)="markAll('Presente')">Todos presentes</button>
        </div>
      </div>
      @if (!alreadyTaken()) { <p class="hint">Todavía no se tomó asistencia este día.</p> }

      <div class="table-scroll">
        <table class="simple-table">
          <thead><tr><th>Alumno/a</th><th>Estado</th><th>Observación</th></tr></thead>
          <tbody>
            @for (row of rows(); track row.enrollmentId) {
              <tr>
                <td>{{ row.student }}</td>
                <td>
                  <div class="segmented">
                    @for (s of statuses; track s.value) {
                      <button [class]="s.css" [class.active]="row.status === s.value" (click)="setStatus(row, s.value)" [title]="s.value">{{ s.short }}</button>
                    }
                  </div>
                </td>
                <td><input [(ngModel)]="row.observation" placeholder="Opcional"></td>
              </tr>
            } @empty { <tr><td colspan="3" class="muted">No hay alumnos.</td></tr> }
          </tbody>
        </table>
      </div>
      <div class="action-bar">
        <span class="muted">P: presente · A: ausente · T: tarde · J: justificado</span>
        <button class="button primary" (click)="save()"><i class="pi pi-save"></i> Guardar asistencia</button>
      </div>
    </section>

    <section class="card">
      <div class="card-title">
        <h2>Resumen del año</h2>
        <button class="button secondary small" (click)="loadSummary()">Actualizar</button>
      </div>
      @if (summary().length) {
        <table class="simple-table">
          <thead><tr><th>Alumno/a</th><th>Presentes</th><th>Ausentes</th><th>Tardes</th><th>Justificadas</th></tr></thead>
          <tbody>
            @for (s of summary(); track s.student) {
              <tr><td>{{ s.student }}</td><td>{{ s.present }}</td><td [class.low]="s.absent > 0">{{ s.absent }}</td><td>{{ s.late }}</td><td>{{ s.justified }}</td></tr>
            }
          </tbody>
        </table>
      }
    </section>
  `
})
export class AttendancePage implements OnInit {
  private readonly api = inject(ApiService);
  readonly statuses = STATUSES;

  courseId: number | null = null;
  date = today();

  readonly courses = signal<CourseOption[]>([]);
  readonly rows = signal<AttendanceRow[]>([]);
  readonly summary = signal<SummaryRow[]>([]);
  readonly alreadyTaken = signal(false);
  readonly message = signal('');
  readonly error = signal('');

  /** Texto tipo "8 presentes · 1 ausente". Se recalcula al cambiar las filas. */
  readonly counts = computed(() => {
    const rows = this.rows();
    return STATUSES.map(s => `${rows.filter(r => r.status === s.value).length} ${s.value.toLowerCase()}`).join(' · ');
  });

  ngOnInit() {
    this.api.get<CourseOption[]>('/api/courses/mine').subscribe({
      next: courses => {
        this.courses.set(courses);
        this.courseId = courses[0]?.id ?? null;
        this.load();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  load() {
    if (!this.courseId || !this.date) return;
    this.api.get<AttendanceRow[]>(`/api/courses/${this.courseId}/attendance?date=${this.date}`).subscribe({
      next: rows => {
        this.alreadyTaken.set(rows.some(r => r.status !== null));
        // Si todavía no se tomó, proponemos "Presente" para todos (lo más común).
        this.rows.set(rows.map(r => ({ ...r, status: r.status ?? 'Presente' })));
      },
      error: e => this.error.set(errorMessage(e))
    });
    this.loadSummary();
  }

  /** Reemplazamos la fila (en vez de modificarla) para que la signal avise y se actualicen los contadores. */
  setStatus(row: AttendanceRow, status: string) {
    this.rows.update(rows => rows.map(r => r === row ? { ...r, status } : r));
  }

  markAll(status: string) {
    this.rows.update(rows => rows.map(r => ({ ...r, status })));
  }

  save() {
    const entries = this.rows().map(r => ({ enrollmentId: r.enrollmentId, status: r.status, observation: r.observation }));
    this.api.post(`/api/courses/${this.courseId}/attendance`, { date: this.date, entries }).subscribe({
      next: () => {
        this.alreadyTaken.set(true);
        this.message.set('Asistencia guardada.');
        setTimeout(() => this.message.set(''), 3000);
        this.loadSummary();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  loadSummary() {
    if (!this.courseId) return;
    this.api.get<SummaryRow[]>(`/api/courses/${this.courseId}/attendance-summary`).subscribe(s => this.summary.set(s));
  }
}
