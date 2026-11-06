import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { CourseOption, Person, PERSON_TYPES } from '../core/models';

/** Datos del formulario de alta/edición. */
interface PersonForm {
  id: number | null; // null = alta nueva
  fullName: string;
  dni: string;
  type: string;
  email: string;
  phone: string;
  courseId: number | null;
}

const EMPTY_FORM: PersonForm = { id: null, fullName: '', dni: '', type: 'Alumno', email: '', phone: '', courseId: null };

/**
 * Personas: alumnos, profesores, preceptores, secretaría y dirección.
 * Cada alta crea automáticamente la cuenta (usuario y clave = DNI).
 * También permite importar listas pegadas desde Excel.
 */
@Component({
  selector: 'app-people',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <h1>Personas</h1>
      <div class="actions">
        <button class="button secondary" (click)="showImport.set(!showImport()); form.set(null)"><i class="pi pi-upload"></i> Importar lista</button>
        <button class="button primary" (click)="newPerson()"><i class="pi pi-plus"></i> Nueva persona</button>
      </div>
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
          <label>Tipo
            <select [(ngModel)]="f.type" [disabled]="!!f.id">
              @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          @if (f.type === 'Alumno') {
            <label>Curso
              <select [(ngModel)]="f.courseId">
                <option [ngValue]="null">Sin curso</option>
                @for (c of courses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
              </select>
            </label>
          }
          <label>Email (opcional) <input [(ngModel)]="f.email" type="email"></label>
          <label>Teléfono (opcional) <input [(ngModel)]="f.phone"></label>
        </div>
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
              @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          @if (importType === 'Alumno') {
            <label>Curso
              <select [(ngModel)]="importCourseId">
                <option [ngValue]="null">Sin curso</option>
                @for (c of courses(); track c.id) { <option [ngValue]="c.id">{{ c.label }}</option> }
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

    <!-- ===== Listado ===== -->
    <section class="card">
      <div class="filters">
        <input [(ngModel)]="search" (keyup.enter)="load()" placeholder="Buscar por nombre o DNI">
        <select [(ngModel)]="typeFilter" (ngModelChange)="load()">
          <option value="">Todos los tipos</option>
          @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
        </select>
        <button class="button secondary" (click)="load()"><i class="pi pi-search"></i> Buscar</button>
      </div>

      <div class="table-scroll">
        <table class="simple-table">
          <thead><tr><th>Nombre</th><th>DNI</th><th>Tipo</th><th>Curso</th><th>Estado</th><th></th></tr></thead>
          <tbody>
            @for (p of people(); track p.id) {
              <tr [class.inactive]="!p.isActive">
                <td>{{ p.fullName }}</td>
                <td>{{ p.dni }}</td>
                <td>{{ p.type }}</td>
                <td>{{ p.course ?? '–' }}</td>
                <td>
                  {{ p.isActive ? 'Activo' : 'Inactivo' }}
                  @if (p.usesDefaultPassword) { <small class="muted"> · clave = DNI</small> }
                </td>
                <td class="row-actions">
                  <button class="icon-button" title="Editar" (click)="edit(p)"><i class="pi pi-pencil"></i></button>
                  <button class="icon-button" title="Volver la clave al DNI" (click)="resetPassword(p)"><i class="pi pi-key"></i></button>
                  <button class="icon-button" [title]="p.isActive ? 'Desactivar' : 'Activar'" (click)="toggle(p)">
                    <i [class]="p.isActive ? 'pi pi-ban' : 'pi pi-check'"></i>
                  </button>
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="muted">No hay resultados.</td></tr> }
          </tbody>
        </table>
      </div>
    </section>
  `
})
export class PeoplePage implements OnInit {
  private readonly api = inject(ApiService);
  readonly types = PERSON_TYPES;

  readonly people = signal<Person[]>([]);
  readonly courses = signal<CourseOption[]>([]);
  /** Formulario abierto (null = cerrado). */
  readonly form = signal<PersonForm | null>(null);
  readonly showImport = signal(false);
  readonly importErrors = signal<string[]>([]);
  readonly message = signal('');
  readonly error = signal('');

  // Filtros e importación (propiedades simples para ngModel).
  search = '';
  typeFilter = '';
  importType = 'Alumno';
  importCourseId: number | null = null;
  importText = '';

  ngOnInit() {
    this.load();
    this.api.get<{ courses: CourseOption[] }>('/api/academic/structure').subscribe(s => this.courses.set(s.courses));
  }

  load() {
    const params = new URLSearchParams({ search: this.search, type: this.typeFilter });
    this.api.get<Person[]>(`/api/people?${params}`).subscribe({
      next: p => this.people.set(p),
      error: e => this.fail(e)
    });
  }

  newPerson() {
    this.showImport.set(false);
    this.form.set({ ...EMPTY_FORM });
  }

  edit(p: Person) {
    this.showImport.set(false);
    this.form.set({ id: p.id, fullName: p.fullName, dni: p.dni, type: p.type, email: p.email ?? '', phone: p.phone ?? '', courseId: p.courseId });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  saveForm(f: PersonForm) {
    const body = { fullName: f.fullName, dni: f.dni, type: f.type, email: f.email, phone: f.phone, courseId: f.courseId };
    const request = f.id ? this.api.put(`/api/people/${f.id}`, body) : this.api.post('/api/people', body);
    request.subscribe({
      next: () => {
        this.ok(f.id ? 'Datos actualizados.' : `Persona creada. Usuario y clave: ${f.dni.replace(/\D/g, '')}`);
        this.form.set(null);
        this.load();
      },
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
        this.load();
      },
      error: e => this.fail(e)
    });
  }

  resetPassword(p: Person) {
    if (!confirm(`¿Volver la clave de ${p.fullName} a su DNI (${p.dni})?`)) return;
    this.api.post(`/api/people/${p.id}/reset-password`).subscribe({
      next: () => { this.ok(`La clave de ${p.fullName} ahora es ${p.dni}.`); this.load(); },
      error: e => this.fail(e)
    });
  }

  toggle(p: Person) {
    const action = p.isActive ? 'desactivar' : 'activar';
    if (!confirm(`¿Seguro que querés ${action} a ${p.fullName}?`)) return;
    this.api.post(`/api/people/${p.id}/toggle`).subscribe({ next: () => this.load(), error: e => this.fail(e) });
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
