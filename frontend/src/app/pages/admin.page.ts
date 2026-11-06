import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { FiltersService } from '../core/filters.service';
import { CourseItem, FiltersData } from '../core/models';

interface Settings { schoolName: string; absenceAlertThreshold: number; hasCustomLogo: boolean; }
interface YearRow { id: number; year: number; isActive: boolean; courses: number; students: number; }
interface PromotionData {
  from: { id: number; label: string; year: number; isActive: boolean };
  subjectCount: number;
  students: { studentId: number; fullName: string; dni: string; approved: number; pending: number; currentCourse: string | null }[];
}
interface StatGroup { count: number; average: number | null; failedPercent: number | null; }
interface Stats {
  byCourse: { course: string; term1: StatGroup; term2: StatGroup; final: StatGroup }[];
  bySubject: { subject: string; term1: StatGroup; term2: StatGroup; final: StatGroup }[];
}

type Tab = 'config' | 'years' | 'stats' | 'export';

/**
 * Administración (solo dueñas del colegio):
 *  - Configuración: nombre, logo y límite de inasistencias.
 *  - Años lectivos: abrir uno nuevo y pasar a los alumnos de curso (promoción).
 *  - Estadísticas: promedios y % de desaprobados.
 *  - Exportar: todos los datos en CSV (Excel), como respaldo propio.
 */
@Component({
  selector: 'app-admin',
  imports: [FormsModule],
  template: `
    <header class="page-header"><h1>Administración</h1></header>

    <div class="chips tabs">
      <button class="chip" [class.active]="tab() === 'config'" (click)="tab.set('config')">Configuración</button>
      <button class="chip" [class.active]="tab() === 'years'" (click)="openYears()">Años lectivos y promoción</button>
      <button class="chip" [class.active]="tab() === 'stats'" (click)="openStats()">Estadísticas</button>
      <button class="chip" [class.active]="tab() === 'export'" (click)="tab.set('export')">Exportar y respaldo</button>
    </div>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error">{{ error() }}</div> }

    <!-- ===== Configuración ===== -->
    @if (tab() === 'config' && settings(); as s) {
      <section class="card">
        <h2>Datos del colegio</h2>
        <div class="form-grid">
          <label>Nombre (aparece en el login y en el boletín) <input [(ngModel)]="s.schoolName" maxlength="150"></label>
          <label>Alerta de inasistencias a partir de <input type="number" min="1" max="200" [(ngModel)]="s.absenceAlertThreshold"></label>
        </div>
        <button class="button primary" (click)="saveSettings(s)">Guardar</button>
      </section>
      <section class="card">
        <h2>Logo</h2>
        <div class="logo-row">
          <img [src]="logoUrl()" alt="Logo actual" class="logo-preview">
          <div>
            <input type="file" accept="image/png,image/jpeg" (change)="uploadLogo($event)">
            <p class="hint">PNG o JPG de hasta 1 MB. Se usa en el menú, el login y los boletines.</p>
            @if (s.hasCustomLogo) { <button class="button secondary small" (click)="resetLogo()">Volver al escudo original</button> }
          </div>
        </div>
      </section>
    }

    <!-- ===== Años lectivos y promoción ===== -->
    @if (tab() === 'years') {
      <section class="card">
        <h2>Años lectivos</h2>
        <table class="simple-table">
          <thead><tr><th>Año</th><th>Estado</th><th>Cursos</th><th>Alumnos</th></tr></thead>
          <tbody>
            @for (y of years(); track y.id) {
              <tr><td><b>{{ y.year }}</b></td><td>{{ y.isActive ? 'Activo' : 'Cerrado (solo lectura)' }}</td><td>{{ y.courses }}</td><td>{{ y.students }}</td></tr>
            }
          </tbody>
        </table>
        <h3 class="spaced-top">Abrir nuevo año lectivo</h3>
        <p class="muted">El año actual queda cerrado (se pueden seguir viendo sus boletines, pero no modificar notas).</p>
        <div class="actions">
          <label class="inline">Año <input type="number" class="short" [(ngModel)]="newYear"></label>
          <label class="check"><input type="checkbox" [(ngModel)]="copyCourses"> Copiar los cursos</label>
          <label class="check"><input type="checkbox" [(ngModel)]="copyAssignments" [disabled]="!copyCourses"> Copiar profesores y preceptores asignados</label>
          <button class="button primary" (click)="openYear()">Abrir {{ newYear }}</button>
        </div>
      </section>

      <section class="card">
        <h2>Promoción de alumnos</h2>
        <p class="muted">Elegí un curso de un año anterior, marcá a los alumnos y el curso del año activo al que pasan.</p>
        <div class="actions">
          <label class="inline">Desde
            <select [(ngModel)]="fromCourseId" (ngModelChange)="loadPromotion()">
              <option [ngValue]="null">Elegir curso…</option>
              @for (c of pastCourses(); track c.id) { <option [ngValue]="c.id">{{ yearOf(c) }} · {{ c.label }}</option> }
            </select>
          </label>
          <label class="inline">Hacia
            <select [(ngModel)]="toCourseId">
              <option [ngValue]="null">Elegir curso…</option>
              @for (c of activeCourses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
            </select>
          </label>
        </div>
        @if (pastCourses().length === 0) { <p class="hint">Todavía no hay años anteriores: abrí un año nuevo primero.</p> }

        @if (promotion(); as p) {
          <table class="simple-table spaced-top">
            <thead><tr><th><input type="checkbox" (change)="toggleAll($event)"></th><th>Alumno/a</th><th>Aprobadas</th><th>Pendientes</th><th>Curso actual</th></tr></thead>
            <tbody>
              @for (s of p.students; track s.studentId) {
                <tr>
                  <td><input type="checkbox" [checked]="selected.has(s.studentId)" (change)="toggle(s.studentId)"></td>
                  <td>{{ s.fullName }}</td>
                  <td>{{ s.approved }} / {{ p.subjectCount }}</td>
                  <td [class.low]="s.pending > 0">{{ s.pending }}</td>
                  <td>{{ s.currentCourse ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
          <button class="button primary spaced-top" (click)="promote()" [disabled]="!toCourseId || selected.size === 0">
            Pasar {{ selected.size }} alumno(s)
          </button>
        }
      </section>
    }

    <!-- ===== Estadísticas ===== -->
    @if (tab() === 'stats' && stats(); as st) {
      <section class="card">
        <h2>Por curso (año activo)</h2>
        <table class="simple-table">
          <thead><tr><th>Curso</th><th>Prom. 1° cuatr.</th><th>% <7</th><th>Prom. 2° cuatr.</th><th>% <7</th><th>Prom. final</th><th>% <7</th></tr></thead>
          <tbody>
            @for (r of st.byCourse; track r.course) {
              <tr><td>{{ r.course }}</td>
                <td>{{ r.term1.average ?? '—' }}</td><td [class.low]="(r.term1.failedPercent ?? 0) > 30">{{ pct(r.term1) }}</td>
                <td>{{ r.term2.average ?? '—' }}</td><td [class.low]="(r.term2.failedPercent ?? 0) > 30">{{ pct(r.term2) }}</td>
                <td>{{ r.final.average ?? '—' }}</td><td [class.low]="(r.final.failedPercent ?? 0) > 30">{{ pct(r.final) }}</td></tr>
            } @empty { <tr><td colspan="7" class="muted">Sin notas cargadas.</td></tr> }
          </tbody>
        </table>
      </section>
      <section class="card">
        <h2>Por materia</h2>
        <table class="simple-table">
          <thead><tr><th>Materia</th><th>Prom. 1° cuatr.</th><th>% <7</th><th>Prom. 2° cuatr.</th><th>% <7</th><th>Prom. final</th><th>% <7</th></tr></thead>
          <tbody>
            @for (r of st.bySubject; track r.subject) {
              <tr><td>{{ r.subject }}</td>
                <td>{{ r.term1.average ?? '—' }}</td><td>{{ pct(r.term1) }}</td>
                <td>{{ r.term2.average ?? '—' }}</td><td>{{ pct(r.term2) }}</td>
                <td>{{ r.final.average ?? '—' }}</td><td>{{ pct(r.final) }}</td></tr>
            }
          </tbody>
        </table>
      </section>
    }

    <!-- ===== Exportar y respaldo ===== -->
    @if (tab() === 'export') {
      <section class="card">
        <h2>Exportar todos los datos</h2>
        <p class="muted">Descarga un ZIP con personas, cursos, materias, inscripciones, notas y asistencia en archivos CSV que se abren con Excel.</p>
        <a class="button primary" href="/api/admin/export"><i class="pi pi-download"></i> Descargar</a>
      </section>
      <section class="card">
        <h2>Restaurar la base a un momento anterior</h2>
        <p class="muted">
          La base de datos está en Neon, que guarda el historial y permite volver a cualquier momento de los últimos días
          (panel de Neon → <b>Backup &amp; Restore</b>). No hace falta hacerlo desde este sistema.
        </p>
      </section>
    }
  `
})
export class AdminPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly filtersService = inject(FiltersService);

  readonly tab = signal<Tab>('config');
  readonly settings = signal<Settings | null>(null);
  readonly years = signal<YearRow[]>([]);
  readonly filters = signal<FiltersData | null>(null);
  readonly promotion = signal<PromotionData | null>(null);
  readonly stats = signal<Stats | null>(null);
  readonly message = signal('');
  readonly error = signal('');
  /** Cambia en cada subida para que el navegador no muestre el logo viejo en caché. */
  readonly logoVersion = signal(Date.now());
  readonly logoUrl = computed(() => `/api/settings/logo?v=${this.logoVersion()}`);

  readonly activeYearId = computed(() => this.filters()?.years.find(y => y.isActive)?.id ?? null);
  readonly activeCourses = computed(() => (this.filters()?.courses ?? []).filter(c => c.schoolYearId === this.activeYearId()));
  readonly pastCourses = computed(() => (this.filters()?.courses ?? []).filter(c => c.schoolYearId !== this.activeYearId()));

  newYear = new Date().getFullYear() + 1;
  copyCourses = true;
  copyAssignments = true;
  fromCourseId: number | null = null;
  toCourseId: number | null = null;
  selected = new Set<number>();

  ngOnInit() {
    this.api.get<Settings>('/api/admin/settings').subscribe({ next: s => this.settings.set(s), error: e => this.fail(e) });
  }

  // ---------------- Configuración
  saveSettings(s: Settings) {
    this.api.put('/api/admin/settings', { schoolName: s.schoolName, absenceAlertThreshold: s.absenceAlertThreshold })
      .subscribe({ next: () => this.ok('Configuración guardada. El nombre nuevo se ve al recargar la página.'), error: e => this.fail(e) });
  }

  uploadLogo(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    // Se usa HttpClient directo porque el cuerpo es un formulario (no JSON).
    this.http.post('/api/admin/settings/logo', form).subscribe({
      next: () => { this.logoVersion.set(Date.now()); this.settings.update(s => s ? { ...s, hasCustomLogo: true } : s); this.ok('Logo actualizado.'); },
      error: e => this.fail(e)
    });
  }

  resetLogo() {
    this.api.delete('/api/admin/settings/logo').subscribe({
      next: () => { this.logoVersion.set(Date.now()); this.settings.update(s => s ? { ...s, hasCustomLogo: false } : s); this.ok('Se volvió al escudo original.'); },
      error: e => this.fail(e)
    });
  }

  // ---------------- Años lectivos y promoción
  openYears() {
    this.tab.set('years');
    this.api.get<YearRow[]>('/api/admin/years').subscribe(y => this.years.set(y));
    this.filtersService.reset();
    this.filtersService.load().subscribe(f => this.filters.set(f));
  }

  yearOf(c: CourseItem) { return this.filters()?.years.find(y => y.id === c.schoolYearId)?.year ?? ''; }

  openYear() {
    if (!confirm(`¿Abrir el año lectivo ${this.newYear}? El año actual quedará cerrado (solo lectura).`)) return;
    this.api.post('/api/admin/years', { year: this.newYear, copyCourses: this.copyCourses, copyAssignments: this.copyAssignments }).subscribe({
      next: () => { this.ok(`Año ${this.newYear} abierto.`); this.openYears(); },
      error: e => this.fail(e)
    });
  }

  loadPromotion() {
    this.selected = new Set();
    this.promotion.set(null);
    if (!this.fromCourseId) return;
    this.api.get<PromotionData>(`/api/admin/promotion?fromCourseId=${this.fromCourseId}`).subscribe({
      next: p => this.promotion.set(p),
      error: e => this.fail(e)
    });
  }

  toggle(id: number) {
    const next = new Set(this.selected);
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selected = next;
  }

  toggleAll(event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected = new Set(checked ? this.promotion()?.students.map(s => s.studentId) ?? [] : []);
  }

  promote() {
    this.api.post<{ promoted: number }>('/api/admin/promotion', { toCourseId: this.toCourseId, studentIds: [...this.selected] }).subscribe({
      next: r => { this.ok(`${r.promoted} alumno(s) pasaron de curso.`); this.loadPromotion(); },
      error: e => this.fail(e)
    });
  }

  // ---------------- Estadísticas
  openStats() {
    this.tab.set('stats');
    this.api.get<Stats>('/api/admin/stats').subscribe({ next: s => this.stats.set(s), error: e => this.fail(e) });
  }

  pct(g: StatGroup) { return g.failedPercent === null ? '—' : `${g.failedPercent}%`; }

  private ok(text: string) {
    this.error.set('');
    this.message.set(text);
    setTimeout(() => this.message.set(''), 4000);
  }

  private fail(e: unknown) {
    this.message.set('');
    this.error.set(errorMessage(e));
  }
}
