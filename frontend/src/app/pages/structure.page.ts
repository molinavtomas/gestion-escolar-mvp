import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { ApiService, errorMessage } from '../core/api.service';
import { FiltersService } from '../core/filters.service';
import { levelLabel, NivelItem, Option } from '../core/models';

interface CourseItem { id: number; nivel: string; level: number; division: string; label: string; students: number; }
interface SubjectItem { id: number; name: string; nivel: string; level: number; displayOrder: number; }
interface TeachingItem { id: number; courseId: number; subjectId: number; teacherId: number; teacher: string; }
interface PreceptorItem { id: number; courseId: number; preceptorId: number; preceptor: string; }

interface Structure {
  year: number;
  niveles: NivelItem[];
  canEdit: boolean;
  courses: CourseItem[];
  subjects: SubjectItem[];
  teachingAssignments: TeachingItem[];
  preceptorAssignments: PreceptorItem[];
  teachers: Option[];
  preceptors: Option[];
}

/**
 * Cursos y materias del año activo. Todo se organiza alrededor del curso elegido:
 *  - las materias de su nivel y año (son las filas del boletín, en ese orden);
 *  - qué profesor dicta cada materia en ese curso;
 *  - qué preceptor está a cargo.
 * Administración y Dirección editan; Secretaría y Preceptoría lo ven en modo lectura.
 */
@Component({
  selector: 'app-structure',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <div>
        <h1>Cursos y materias</h1>
        <p class="muted">Ciclo lectivo {{ data()?.year }}</p>
      </div>
    </header>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error() }}</div> }

    <!-- ===== Cursos (agrupados por nivel) ===== -->
    <section class="card">
      <div class="card-title">
        <h2>Cursos</h2>
        <div class="chips tabs">
          @for (n of data()?.niveles ?? []; track n.nivel) {
            <button class="chip" [class.active]="nivelTab() === n.nivel" (click)="selectTab(n.nivel)">{{ n.nivel }}</button>
          }
        </div>
      </div>
      <div class="chips">
        @for (c of coursesOfTab(); track c.id) {
          <button class="chip" [class.active]="c.id === courseId()" (click)="courseId.set(c.id)">
            {{ levelText(c.nivel, c.level) }} {{ c.division }} <small>({{ c.students }})</small>
          </button>
        } @empty { <span class="muted">No hay cursos de {{ nivelTab() }}.</span> }
      </div>
      @if (data()?.canEdit) {
        <div class="actions">
          <label class="inline">{{ nivelTab() === 'Inicial' ? 'Sala' : 'Año' }}
            <select [(ngModel)]="newLevel">@for (l of levelsOfTab(); track l) { <option [ngValue]="l">{{ levelText(nivelTab(), l) }}</option> }</select>
          </label>
          <label class="inline">División <input class="short" [(ngModel)]="newDivision" maxlength="3" placeholder="A"></label>
          <button class="button secondary" (click)="createCourse()"><i class="pi pi-plus"></i> Agregar curso de {{ nivelTab() }}</button>
        </div>
      }
    </section>

    @if (course(); as c) {
      <!-- ===== Materias del año y profesores del curso ===== -->
      <section class="card">
        <h2>Materias de {{ c.nivel }} {{ levelText(c.nivel, c.level) }} · profesores de {{ c.label }}</h2>
        <p class="muted">El orden de esta lista es el orden en que aparecen en el boletín de todos los cursos de {{ c.nivel }} {{ levelText(c.nivel, c.level) }}.</p>
        <div class="table-scroll">
          <table class="simple-table">
            <thead><tr><th></th><th>Materia</th><th>Profesor/a en {{ c.label }}</th><th>Asignar</th><th></th></tr></thead>
            <tbody>
              @for (s of subjectsOfCourse(); track s.id; let first = $first; let last = $last) {
                <tr>
                  <td class="order-buttons">
                    @if (data()?.canEdit) {
                      <button class="icon-button" [disabled]="first" (click)="moveSubject(s, true)" title="Subir"><i class="pi pi-arrow-up"></i></button>
                      <button class="icon-button" [disabled]="last" (click)="moveSubject(s, false)" title="Bajar"><i class="pi pi-arrow-down"></i></button>
                    }
                  </td>
                  <td>{{ s.name }}</td>
                  <td>
                    @for (t of teachersOf(s.id); track t.id) {
                      <span class="tag">{{ t.teacher }} @if (data()?.canEdit) { <button (click)="unassignTeacher(t)" title="Quitar"><i class="pi pi-times"></i></button> }</span>
                    } @empty { <span class="low">Sin asignar</span> }
                  </td>
                  <td>
                    @if (data()?.canEdit) {
                      <select [(ngModel)]="teacherChoice[s.id]" (ngModelChange)="assignTeacher(s.id, $event)">
                        <option [ngValue]="undefined">Elegir…</option>
                        @for (t of data()?.teachers ?? []; track t.id) { <option [ngValue]="t.id">{{ t.name }}</option> }
                      </select>
                    }
                  </td>
                  <td>@if (data()?.canEdit) { <button class="icon-button" title="Quitar materia del boletín" (click)="removeSubject(s)"><i class="pi pi-trash"></i></button> }</td>
                </tr>
              } @empty { <tr><td colspan="5" class="muted">Este año todavía no tiene materias.</td></tr> }
            </tbody>
          </table>
        </div>
        @if (data()?.canEdit) {
          <div class="actions">
            <input [(ngModel)]="newSubject" placeholder="Nueva materia" (keyup.enter)="createSubject(c)">
            <button class="button secondary" (click)="createSubject(c)"><i class="pi pi-plus"></i> Agregar materia</button>
          </div>
        }
      </section>

      <!-- ===== Preceptores ===== -->
      <section class="card">
        <h2>Preceptor/a de {{ c.label }}</h2>
        <div class="chips">
          @for (p of preceptorsOfCourse(); track p.id) {
            <span class="tag">{{ p.preceptor }} @if (data()?.canEdit) { <button (click)="unassignPreceptor(p)" title="Quitar"><i class="pi pi-times"></i></button> }</span>
          } @empty { <span class="low">Sin asignar</span> }
        </div>
        @if (data()?.canEdit) {
        <div class="actions">
          <select [(ngModel)]="preceptorChoice">
            <option [ngValue]="null">Elegir preceptor…</option>
            @for (p of data()?.preceptors ?? []; track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
          </select>
          <button class="button secondary" (click)="assignPreceptor()" [disabled]="!preceptorChoice"><i class="pi pi-plus"></i> Asignar</button>
        </div>
        }
      </section>
    }
  `
})
export class StructurePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly filters = inject(FiltersService);

  readonly data = signal<Structure | null>(null);
  readonly courseId = signal<number | null>(null);
  readonly message = signal('');
  readonly error = signal('');

  /** Pestaña de nivel elegida (Inicial / Primaria / Secundaria). */
  readonly nivelTab = signal('Secundaria');
  readonly coursesOfTab = computed(() => (this.data()?.courses ?? []).filter(c => c.nivel === this.nivelTab()));
  readonly levelsOfTab = computed(() => this.data()?.niveles.find(n => n.nivel === this.nivelTab())?.levels ?? []);

  readonly course = computed(() => this.data()?.courses.find(c => c.id === this.courseId()) ?? null);
  readonly subjectsOfCourse = computed(() =>
    (this.data()?.subjects ?? []).filter(s => s.nivel === this.course()?.nivel && s.level === this.course()?.level));
  readonly preceptorsOfCourse = computed(() =>
    (this.data()?.preceptorAssignments ?? []).filter(p => p.courseId === this.courseId()));

  // Campos de formularios (ngModel).
  newLevel = 1;
  newDivision = '';
  newSubject = '';
  preceptorChoice: number | null = null;
  /** Profesor elegido en el <select> de cada materia (se limpia al asignar). */
  teacherChoice: Record<number, number | undefined> = {};

  ngOnInit() { this.load(); }

  load() {
    this.api.get<Structure>('/api/academic/structure').subscribe({
      next: s => {
        this.data.set(s);
        // Mantener el curso elegido; si no hay, elegir el primero de la pestaña.
        if (!s.courses.some(c => c.id === this.courseId())) {
          const first = s.courses.find(c => c.nivel === this.nivelTab()) ?? s.courses[0];
          if (first) this.nivelTab.set(first.nivel);
          this.courseId.set(first?.id ?? null);
        }
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  levelText(nivel: string, level: number) { return levelLabel(nivel, level); }

  /** Cambia de nivel: elige el primer curso de ese nivel y ajusta el año propuesto para un curso nuevo. */
  selectTab(nivel: string) {
    this.nivelTab.set(nivel);
    this.newLevel = this.levelsOfTab()[0] ?? 1;
    this.courseId.set(this.coursesOfTab()[0]?.id ?? null);
  }

  teachersOf(subjectId: number) {
    return (this.data()?.teachingAssignments ?? []).filter(t => t.courseId === this.courseId() && t.subjectId === subjectId);
  }

  // ---- Acciones: todas siguen el mismo patrón (llamar a la API, mostrar resultado, recargar) ----

  createCourse() {
    this.run(this.api.post('/api/academic/courses', { nivel: this.nivelTab(), level: this.newLevel, division: this.newDivision }), 'Curso creado.',
      () => { this.newDivision = ''; this.filters.reset(); });
  }

  createSubject(c: CourseItem) {
    if (!this.newSubject.trim()) return;
    this.run(this.api.post('/api/academic/subjects', { name: this.newSubject, nivel: c.nivel, level: c.level }), 'Materia agregada.',
      () => this.newSubject = '');
  }

  removeSubject(s: SubjectItem) {
    if (!confirm(`¿Quitar ${s.name} del boletín de ${s.nivel} ${levelLabel(s.nivel, s.level)}? Las notas ya cargadas se conservan.`)) return;
    this.run(this.api.delete(`/api/academic/subjects/${s.id}`), 'Materia quitada.');
  }

  moveSubject(s: SubjectItem, up: boolean) {
    this.run(this.api.post(`/api/academic/subjects/${s.id}/move`, { up }));
  }

  assignTeacher(subjectId: number, teacherId: number | undefined) {
    if (!teacherId) return;
    this.run(this.api.post('/api/academic/teaching-assignments', { courseId: this.courseId(), subjectId, teacherId }),
      'Profesor asignado.', () => this.teacherChoice[subjectId] = undefined);
  }

  unassignTeacher(t: TeachingItem) {
    this.run(this.api.delete(`/api/academic/teaching-assignments/${t.id}`), 'Asignación quitada.');
  }

  assignPreceptor() {
    this.run(this.api.post('/api/academic/preceptor-assignments', { courseId: this.courseId(), preceptorId: this.preceptorChoice }),
      'Preceptor asignado.', () => this.preceptorChoice = null);
  }

  unassignPreceptor(p: PreceptorItem) {
    this.run(this.api.delete(`/api/academic/preceptor-assignments/${p.id}`), 'Asignación quitada.');
  }

  /** Ejecuta una acción, muestra el mensaje (o el error) y recarga la estructura. */
  private run(request: Observable<unknown>, success = '', after?: () => void) {
    this.error.set('');
    request.subscribe({
      next: () => {
        after?.();
        if (success) {
          this.message.set(success);
          setTimeout(() => this.message.set(''), 3000);
        }
        this.load();
      },
      error: e => this.error.set(errorMessage(e))
    });
  }
}
