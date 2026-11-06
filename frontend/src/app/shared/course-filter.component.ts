import { Component, computed, inject, input, model, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FiltersService } from '../core/filters.service';
import { CourseItem, FiltersData, levelLabel } from '../core/models';

/**
 * Filtros en cascada: Año lectivo → Nivel → Año → Curso.
 * Cada select acota las opciones del siguiente, así con muchos cursos (Inicial, Primaria y
 * Secundaria) se llega rápido al que se busca.
 *
 * Uso: <app-course-filter [(nivel)]="nivel" [(level)]="level" [(courseId)]="courseId" (changed)="buscar()" />
 * Los valores son "model()" (signals de doble vía): el padre los lee y también puede cambiarlos.
 */
@Component({
  selector: 'app-course-filter',
  imports: [FormsModule],
  template: `
    @if (showYear()) {
      <label class="inline">Ciclo
        <select [ngModel]="yearId()" (ngModelChange)="setYear($event)">
          @for (y of data()?.years ?? []; track y.id) { <option [ngValue]="y.id">{{ y.year }}{{ y.isActive ? '' : ' (cerrado)' }}</option> }
        </select>
      </label>
    }
    <label class="inline">Nivel
      <select [ngModel]="nivel()" (ngModelChange)="setNivel($event)">
        @if (allowAll()) { <option value="">Todos</option> }
        @for (n of niveles(); track n) { <option [value]="n">{{ n }}</option> }
      </select>
    </label>
    <label class="inline">Año
      <select [ngModel]="level()" (ngModelChange)="setLevel($event)" [disabled]="!nivel()">
        @if (allowAll()) { <option [ngValue]="null">Todos</option> }
        @for (l of levels(); track l) { <option [ngValue]="l">{{ label(l) }}</option> }
      </select>
    </label>
    <label class="inline">Curso
      <select [ngModel]="courseId()" (ngModelChange)="setCourse($event)">
        @if (allowAll()) { <option [ngValue]="null">Todos</option> }
        @for (c of courses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
      </select>
    </label>
  `,
  host: { class: 'filter-group' }
})
export class CourseFilterComponent implements OnInit {
  private readonly filters = inject(FiltersService);

  /** Mostrar el selector de año lectivo (para ver boletines de años anteriores). */
  readonly showYear = input(false);
  /** Permitir "Todos" en cada filtro. Si es false, siempre queda un curso elegido. */
  readonly allowAll = input(true);
  /** Mostrar solo cursos donde se puede tomar asistencia. */
  readonly onlyAttendance = input(false);

  readonly yearId = model<number | null>(null);
  readonly nivel = model('');
  readonly level = model<number | null>(null);
  readonly courseId = model<number | null>(null);

  /** Se emite después de cualquier cambio (para que el padre vuelva a buscar). */
  readonly changed = output<void>();

  readonly data = signal<FiltersData | null>(null);

  /** Cursos del año elegido (y, si corresponde, solo los de asistencia). */
  private readonly yearCourses = computed<CourseItem[]>(() => {
    const d = this.data();
    if (!d) return [];
    return d.courses.filter(c => c.schoolYearId === this.yearId() && (!this.onlyAttendance() || c.canTakeAttendance));
  });

  /** Niveles que tienen al menos un curso visible. */
  readonly niveles = computed(() => [...new Set(this.yearCourses().map(c => c.nivel))]);
  readonly levels = computed(() =>
    [...new Set(this.yearCourses().filter(c => c.nivel === this.nivel()).map(c => c.level))].sort((a, b) => a - b));
  readonly courses = computed(() => this.yearCourses().filter(c =>
    (!this.nivel() || c.nivel === this.nivel()) && (this.level() === null || c.level === this.level())));

  ngOnInit() {
    this.filters.load().subscribe(d => {
      this.data.set(d);
      if (this.yearId() === null) this.yearId.set(d.years.find(y => y.isActive)?.id ?? d.years[0]?.id ?? null);
      // Sin "Todos", arrancamos con el primer curso disponible.
      if (!this.allowAll() && this.courseId() === null) this.selectFirstCourse();
      this.changed.emit();
    });
  }

  label(level: number) { return levelLabel(this.nivel(), level); }

  setYear(id: number) {
    this.yearId.set(id);
    this.nivel.set('');
    this.level.set(null);
    this.courseId.set(null);
    if (!this.allowAll()) this.selectFirstCourse();
    this.changed.emit();
  }

  setNivel(nivel: string) {
    this.nivel.set(nivel);
    this.level.set(null);
    this.courseId.set(null);
    if (!this.allowAll()) this.selectFirstCourse();
    this.changed.emit();
  }

  setLevel(level: number | null) {
    this.level.set(level);
    this.courseId.set(null);
    if (!this.allowAll()) this.selectFirstCourse();
    this.changed.emit();
  }

  setCourse(id: number | null) {
    this.courseId.set(id);
    // Elegir un curso completa nivel y año automáticamente (útil cuando se elige directo).
    const course = this.yearCourses().find(c => c.id === id);
    if (course) {
      this.nivel.set(course.nivel);
      this.level.set(course.level);
    }
    this.changed.emit();
  }

  private selectFirstCourse() {
    const first = this.courses()[0] ?? this.yearCourses()[0];
    if (!first) return;
    this.nivel.set(first.nivel);
    this.level.set(first.level);
    this.courseId.set(first.id);
  }
}
