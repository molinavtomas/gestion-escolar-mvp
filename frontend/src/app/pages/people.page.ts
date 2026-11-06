import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { FiltersService } from '../core/filters.service';
import { CourseItem, Paged, Person, PERSON_TYPES } from '../core/models';
import { CourseFilterComponent } from '../shared/course-filter.component';
import { PagerComponent } from '../shared/pager.component';

/** Datos del formulario de alta/edición. */
interface PersonForm {
  id: number | null; // null = alta nueva
  fullName: string;
  dni: string;
  type: string;
  email: string;
  phone: string;
  courseId: number | null;
  siblings: string[];
  originalType: string | null;
}

const EMPTY_FORM: PersonForm = { id: null, fullName: '', dni: '', type: 'Alumno', email: '', phone: '', courseId: null, siblings: [], originalType: null };

/**
 * Personas: alumnos, profesores, preceptores, secretaría, dirección y administración.
 *  - Todos los que "ven todo" pueden buscar y consultar.
 *  - Altas, bajas y edición: según permisos (cada fila trae qué se puede hacer con ella).
 *  - Cada alta crea automáticamente la cuenta (usuario y clave = DNI).
 *  - Importación de listas pegadas desde Excel y vinculación de hermanos.
 */
@Component({
  selector: 'app-people',
  imports: [FormsModule, CourseFilterComponent, PagerComponent],
  template: `
    <header class="page-header">
      <h1>Personas</h1>
      @if (auth.can('managePeople')) {
        <div class="actions">
          <button class="button secondary" (click)="showImport.set(!showImport()); form.set(null)"><i class="pi pi-upload"></i> Importar lista</button>
          <button class="button primary" (click)="newPerson()"><i class="pi pi-plus"></i> Nueva persona</button>
        </div>
      }
    </header>

    @if (message()) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message() }}</div> }
    @if (error()) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error() }}</div> }

    <!-- ===== Alta / edición ===== -->
    @if (form(); as f) {
      <section class="card">
        <h2>{{ f.id ? 'Editar persona' : 'Nueva persona' }}</h2>
        <div class="form-grid">
          <label>Apellido y nombre <input [(ngModel)]="f.fullName" placeholder="Ej. Pérez Juan"></label>
          <label>DNI <input [(ngModel)]="f.dni" inputmode="numeric" placeholder="Sin puntos"></label>
          <label>Tipo (rol)
            <!-- Un alumno no puede pasar a personal ni al revés: al editar, se limitan las opciones. -->
            <select [(ngModel)]="f.type" [disabled]="f.originalType === 'Alumno'">
              @for (t of typesFor(f); track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          @if (f.type === 'Alumno') {
            <label>Curso
              <select [(ngModel)]="f.courseId">
                <option [ngValue]="null">Sin curso</option>
                @for (c of activeCourses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
              </select>
            </label>
          }
          <label>Email (opcional) <input [(ngModel)]="f.email" type="email"></label>
          <label>Teléfono (opcional) <input [(ngModel)]="f.phone"></label>
        </div>

        <!-- Hermanos: solo al editar un alumno -->
        @if (f.id && f.type === 'Alumno') {
          <div class="siblings">
            <b>Hermanos/as vinculados:</b>
            @for (s of f.siblings; track s) { <span class="tag">{{ s }}</span> } @empty { <span class="muted">ninguno</span> }
            @if (f.siblings.length) { <button class="link-button danger-link" (click)="unlinkSibling(f)">Desvincular a este alumno</button> }
            <div class="actions">
              <input [(ngModel)]="siblingDni" placeholder="DNI del hermano/a" inputmode="numeric">
              <button class="button secondary small" (click)="linkSibling(f)" [disabled]="!siblingDni.trim()">Vincular</button>
            </div>
            <p class="hint">Con el DNI de cualquiera de los hermanos, la familia ve los boletines de todos.</p>
          </div>
        }

        @if (!f.id) {
          <p class="hint">Se crea la cuenta con usuario y clave = DNI. {{ f.type === 'Alumno' ? 'La familia ingresa con el DNI del alumno.' : '' }}</p>
        }
        <div class="actions">
          <button class="button primary" (click)="saveForm(f)">Guardar</button>
          <button class="button secondary" (click)="form.set(null)">Cancelar</button>
        </div>
      </section>
    }

    <!-- ===== Importación masiva ===== -->
    @if (showImport()) {
      <section class="card">
        <h2>Importar lista</h2>
        <p class="muted">Copiá dos columnas de Excel (<b>Apellido y nombre</b> y <b>DNI</b>) y pegalas acá. Una persona por línea.</p>
        <div class="form-grid">
          <label>Tipo
            <select [(ngModel)]="importType">
              @for (t of creatableTypes(); track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          @if (importType === 'Alumno') {
            <label>Curso
              <select [(ngModel)]="importCourseId">
                <option [ngValue]="null">Sin curso</option>
                @for (c of activeCourses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
              </select>
            </label>
          }
        </div>
        <textarea rows="8" [(ngModel)]="importText" placeholder="Pérez Juan	45123456&#10;Gómez Ana	45987654"></textarea>
        <div class="actions">
          <button class="button primary" (click)="runImport()" [disabled]="!importText.trim()">Importar</button>
        </div>
        @if (importErrors().length) {
          <div class="alert warning"><div>
            <b>Algunas líneas no se importaron:</b>
            <ul>@for (e of importErrors(); track $index) { <li>{{ e }}</li> }</ul>
          </div></div>
        }
      </section>
    }

    <!-- ===== Filtros + listado ===== -->
    <section class="card">
      <div class="filters">
        <input class="search" [(ngModel)]="search" (keyup.enter)="goTo(1)" placeholder="Buscar por nombre o DNI">
        <label class="inline">Tipo
          <select [(ngModel)]="typeFilter" (ngModelChange)="goTo(1)">
            <option value="">Todos</option>
            @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
          </select>
        </label>
        <app-course-filter [(nivel)]="nivel" [(level)]="level" [(courseId)]="courseId" (changed)="goTo(1)" />
        <label class="inline">Estado
          <select [(ngModel)]="status" (ngModelChange)="goTo(1)">
            <option value="">Todos</option>
            <option value="activos">Activos</option>
            <option value="inactivos">Inactivos</option>
          </select>
        </label>
        <button class="button secondary" (click)="goTo(1)"><i class="pi pi-search"></i> Buscar</button>
      </div>

      <div class="table-scroll">
        <table class="simple-table">
          <thead><tr><th>Nombre</th><th>DNI</th><th>Tipo</th><th>Curso</th><th>Estado</th><th></th></tr></thead>
          <tbody>
            @for (p of result()?.items ?? []; track p.id) {
              <tr [class.inactive]="!p.isActive">
                <td>
                  {{ p.fullName }}
                  @if (p.siblings.length) { <small class="muted" [title]="'Hermanos: ' + p.siblings.join(', ')"> · <i class="pi pi-users"></i> {{ p.siblings.length }}</small> }
                </td>
                <td>{{ p.dni }}</td>
                <td>{{ p.type }}</td>
                <td>{{ p.course ?? '–' }}</td>
                <td>
                  {{ p.isActive ? 'Activo' : 'Inactivo' }}
                  @if (p.usesDefaultPassword) { <small class="muted"> · clave = DNI</small> }
                </td>
                <td class="row-actions">
                  @if (p.canEdit) { <button class="icon-button" title="Editar" (click)="edit(p)"><i class="pi pi-pencil"></i></button> }
                  @if (p.canReset) { <button class="icon-button" title="Volver la clave al DNI" (click)="resetPassword(p)"><i class="pi pi-key"></i></button> }
                  @if (p.canEdit) {
                    <button class="icon-button" [title]="p.isActive ? 'Desactivar' : 'Activar'" (click)="toggle(p)">
                      <i [class]="p.isActive ? 'pi pi-ban' : 'pi pi-check'"></i>
                    </button>
                  }
                  @if (p.canDelete) { <button class="icon-button danger" title="Borrar definitivamente" (click)="remove(p)"><i class="pi pi-trash"></i></button> }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="muted">No hay resultados.</td></tr> }
          </tbody>
        </table>
      </div>
      @if (result(); as r) {
        <app-pager [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="goTo($event)" />
      }
    </section>
  `
})
export class PeoplePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly filters = inject(FiltersService);
  readonly auth = inject(AuthService);
  readonly types = PERSON_TYPES;

  readonly result = signal<Paged<Person> | null>(null);
  readonly courses = signal<CourseItem[]>([]);
  readonly activeYearId = signal<number | null>(null);
  /** Cursos del año activo (para inscribir). */
  readonly activeCourses = computed(() => this.courses().filter(c => c.schoolYearId === this.activeYearId()));
  /** Formulario abierto (null = cerrado). */
  readonly form = signal<PersonForm | null>(null);
  readonly showImport = signal(false);
  readonly importErrors = signal<string[]>([]);
  readonly message = signal('');
  readonly error = signal('');

  /** Tipos que este usuario puede crear (solo el Administrador crea Directores y Administradores). */
  readonly creatableTypes = computed(() =>
    PERSON_TYPES.filter(t => this.auth.user()?.role === 'Administrador' || (t !== 'Director' && t !== 'Administrador')));

  // Filtros e importación (propiedades simples para ngModel; los de curso son signals compartidas).
  search = '';
  typeFilter = '';
  status = 'activos';
  readonly nivel = signal('');
  readonly level = signal<number | null>(null);
  readonly courseId = signal<number | null>(null);
  importType = 'Alumno';
  importCourseId: number | null = null;
  importText = '';
  siblingDni = '';

  ngOnInit() {
    this.filters.load().subscribe(f => {
      this.courses.set(f.courses);
      this.activeYearId.set(f.years.find(y => y.isActive)?.id ?? null);
    });
  }

  goTo(page: number) {
    const params = new URLSearchParams({ page: String(page), pageSize: '20', search: this.search, type: this.typeFilter, status: this.status });
    if (this.nivel()) params.set('nivel', this.nivel());
    if (this.courseId()) params.set('courseId', String(this.courseId()));
    this.api.get<Paged<Person>>(`/api/people?${params}`).subscribe({
      next: r => this.result.set(r),
      error: e => this.fail(e)
    });
  }

  /** Opciones de tipo en el formulario: un alumno sigue siendo alumno; el personal elige entre tipos de personal. */
  typesFor(f: PersonForm) {
    if (f.originalType === 'Alumno') return ['Alumno'];
    if (f.originalType) return this.creatableTypes().filter(t => t !== 'Alumno');
    return this.creatableTypes();
  }

  newPerson() {
    this.showImport.set(false);
    this.form.set({ ...EMPTY_FORM, siblings: [] });
  }

  edit(p: Person) {
    this.showImport.set(false);
    this.siblingDni = '';
    this.form.set({
      id: p.id, fullName: p.fullName, dni: p.dni, type: p.type, email: p.email ?? '', phone: p.phone ?? '',
      courseId: p.courseId, siblings: [...p.siblings], originalType: p.type
    });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  saveForm(f: PersonForm) {
    const body = { fullName: f.fullName, dni: f.dni, type: f.type, email: f.email, phone: f.phone, courseId: f.courseId };
    const request = f.id ? this.api.put(`/api/people/${f.id}`, body) : this.api.post('/api/people', body);
    request.subscribe({
      next: () => {
        this.ok(f.id ? 'Datos actualizados.' : `Persona creada. Usuario y clave: ${f.dni.replace(/\D/g, '')}`);
        this.form.set(null);
        this.goTo(this.result()?.page ?? 1);
      },
      error: e => this.fail(e)
    });
  }

  /** Busca al hermano por DNI y lo vincula. */
  linkSibling(f: PersonForm) {
    const dni = this.siblingDni.replace(/\D/g, '');
    this.api.get<Paged<Person>>(`/api/people?type=Alumno&search=${dni}&pageSize=5`).subscribe({
      next: r => {
        const sibling = r.items.find(x => x.dni === dni);
        if (!sibling) { this.error.set(`No hay un alumno con DNI ${dni}.`); return; }
        this.api.post(`/api/people/${f.id}/siblings`, { siblingId: sibling.id }).subscribe({
          next: () => {
            this.form.set({ ...f, siblings: [...f.siblings, sibling.fullName] });
            this.siblingDni = '';
            this.ok(`Vinculado con ${sibling.fullName}.`);
            this.goTo(this.result()?.page ?? 1);
          },
          error: e => this.fail(e)
        });
      },
      error: e => this.fail(e)
    });
  }

  unlinkSibling(f: PersonForm) {
    this.api.delete(`/api/people/${f.id}/siblings`).subscribe({
      next: () => { this.form.set({ ...f, siblings: [] }); this.ok('Alumno desvinculado de sus hermanos.'); this.goTo(this.result()?.page ?? 1); },
      error: e => this.fail(e)
    });
  }

  runImport() {
    this.importErrors.set([]);
    this.api.post<{ created: number; errors: string[] }>('/api/people/import', {
      type: this.importType, courseId: this.importCourseId, text: this.importText
    }).subscribe({
      next: r => {
        this.ok(`Se importaron ${r.created} persona(s).`);
        this.importErrors.set(r.errors);
        if (r.errors.length === 0) this.importText = '';
        this.goTo(1);
      },
      error: e => this.fail(e)
    });
  }

  resetPassword(p: Person) {
    if (!confirm(`¿Volver la clave de ${p.fullName} a su DNI (${p.dni})?`)) return;
    this.api.post(`/api/people/${p.id}/reset-password`).subscribe({
      next: () => { this.ok(`La clave de ${p.fullName} ahora es ${p.dni}.`); this.goTo(this.result()?.page ?? 1); },
      error: e => this.fail(e)
    });
  }

  toggle(p: Person) {
    const action = p.isActive ? 'desactivar' : 'activar';
    if (!confirm(`¿Seguro que querés ${action} a ${p.fullName}?`)) return;
    this.api.post(`/api/people/${p.id}/toggle`).subscribe({ next: () => this.goTo(this.result()?.page ?? 1), error: e => this.fail(e) });
  }

  remove(p: Person) {
    if (!confirm(`¿Borrar DEFINITIVAMENTE a ${p.fullName}? Solo se puede si no tiene notas, asistencia ni asignaciones.`)) return;
    this.api.delete(`/api/people/${p.id}`).subscribe({
      next: () => { this.ok(`${p.fullName} fue borrado.`); this.goTo(this.result()?.page ?? 1); },
      error: e => this.fail(e)
    });
  }

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
