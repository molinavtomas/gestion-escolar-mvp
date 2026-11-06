import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { CourseOption, ReportCard } from '../core/models';
import { ReportCardComponent } from '../shared/report-card.component';

interface StudentItem { studentId: number; fullName: string; dni: string; }

/**
 * Boletines para Dirección, Secretaría y Preceptoría:
 * elegir curso → ver la lista → abrir el boletín de un alumno o imprimir los de todo el curso.
 * Acá se ven completos (sin el filtro de publicación de las familias).
 */
@Component({
  selector: 'app-report-cards',
  imports: [FormsModule, ReportCardComponent],
  template: `
    <header class="page-header no-print">
      <h1>Boletines</h1>
      <div class="actions">
        <label class="inline">Curso
          <select [(ngModel)]="courseId" (ngModelChange)="loadStudents()">
            @for (c of courses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
          </select>
        </label>
        <button class="button secondary" (click)="printCourse()" [disabled]="!courseId"><i class="pi pi-print"></i> Imprimir todo el curso</button>
      </div>
    </header>

    @if (error()) { <div class="alert error no-print">{{ error() }}</div> }

    <div class="split">
      <!-- Lista de alumnos del curso -->
      <aside class="card student-list no-print">
        @for (s of students(); track s.studentId) {
          <button [class.active]="selectedId() === s.studentId" (click)="open(s.studentId)">
            {{ s.fullName }} <small>{{ s.dni }}</small>
          </button>
        } @empty { <p class="muted">No hay alumnos en este curso.</p> }
      </aside>

      <!-- Boletín(es) -->
      <section class="split-main">
        @for (card of cards(); track card.studentId) {
          <app-report-card [card]="card" />
          @if (cards().length === 1) {
            <button class="button secondary no-print" (click)="print()"><i class="pi pi-print"></i> Imprimir este boletín</button>
          }
        } @empty {
          <p class="muted no-print">Elegí un alumno para ver su boletín.</p>
        }
      </section>
    </div>
  `
})
export class ReportCardsPage implements OnInit {
  private readonly api = inject(ApiService);

  courseId: number | null = null;
  readonly courses = signal<CourseOption[]>([]);
  readonly students = signal<StudentItem[]>([]);
  /** Boletines visibles: uno (alumno elegido) o todos los del curso (para imprimir). */
  readonly cards = signal<ReportCard[]>([]);
  readonly selectedId = signal<number | null>(null);
  readonly error = signal('');

  ngOnInit() {
    this.api.get<CourseOption[]>('/api/courses/mine').subscribe({
      next: courses => {
        this.courses.set(courses);
        this.courseId = courses[0]?.id ?? null;
        this.loadStudents();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  loadStudents() {
    this.cards.set([]);
    this.selectedId.set(null);
    if (!this.courseId) return;
    this.api.get<StudentItem[]>(`/api/courses/${this.courseId}/students`).subscribe({
      next: s => this.students.set(s),
      error: e => this.error.set(errorMessage(e))
    });
  }

  open(studentId: number) {
    this.selectedId.set(studentId);
    this.api.get<ReportCard>(`/api/report-cards/${studentId}`).subscribe({
      next: c => this.cards.set([c]),
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** Carga los boletines de todo el curso y abre la impresión (uno por hoja, ver styles.scss). */
  printCourse() {
    this.selectedId.set(null);
    this.api.get<ReportCard[]>(`/api/report-cards/course/${this.courseId}`).subscribe({
      next: cards => {
        this.cards.set(cards);
        // Esperamos a que Angular dibuje los boletines antes de abrir el diálogo de impresión.
        setTimeout(() => window.print(), 300);
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  print() { window.print(); }
}
