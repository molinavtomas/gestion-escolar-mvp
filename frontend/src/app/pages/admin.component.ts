import { DatePipe } from '@angular/common';
import { ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-admin',
  imports: [FormsModule, DatePipe],
  template: `
    <header class="page-header">
      <div><span class="eyebrow">Secretaría y dirección</span><h1>Panel de gestión</h1><p>Una mirada rápida a la actividad de la escuela.</p></div>
      <button class="button primary" (click)="activeTab = 'personas'; editingId = null"><i class="pi pi-user-plus"></i>Nueva persona</button>
    </header>

    @if (message) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message }}</div> }
    @if (error) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error }}</div> }

    <section class="metrics-grid">
      <article class="metric-card accent-blue"><span><i class="pi pi-users"></i></span><div><small>Personas activas</small><strong>{{ summary?.people ?? '—' }}</strong><p>{{ summary?.students ?? 0 }} estudiantes</p></div></article>
      <article class="metric-card accent-violet"><span><i class="pi pi-book"></i></span><div><small>Docentes</small><strong>{{ summary?.teachers ?? '—' }}</strong><p>con asignaciones</p></div></article>
      <article class="metric-card accent-green"><span><i class="pi pi-building"></i></span><div><small>Cursos activos</small><strong>{{ summary?.courses ?? '—' }}</strong><p>Ciclo lectivo 2026</p></div></article>
      <article class="metric-card accent-orange"><span><i class="pi pi-send"></i></span><div><small>Notas publicadas</small><strong>{{ summary?.publishedGrades ?? '—' }}</strong><p>{{ summary?.pendingGrades ?? 0 }} en borrador</p></div></article>
    </section>

    <div class="tabs">
      <button [class.active]="activeTab === 'personas'" (click)="activeTab = 'personas'"><i class="pi pi-users"></i>Personas</button>
      <button [class.active]="activeTab === 'vinculos'" (click)="activeTab = 'vinculos'"><i class="pi pi-sitemap"></i>Vínculos e inscripciones</button>
      <button [class.active]="activeTab === 'asignaciones'" (click)="activeTab = 'asignaciones'"><i class="pi pi-briefcase"></i>Asignaciones</button>
      <button [class.active]="activeTab === 'publicacion'" (click)="activeTab = 'publicacion'"><i class="pi pi-eye"></i>Publicación</button>
    </div>

    @if (activeTab === 'personas') {
      <section class="two-column">
        <article class="surface">
          <div class="section-title"><div><h2>Directorio</h2><p>Altas, edición y estado de acceso.</p></div><div class="search"><i class="pi pi-search"></i><input [(ngModel)]="search" (ngModelChange)="loadPeople()" placeholder="Buscar por nombre o DNI"></div></div>
          <div class="table-wrap"><table><thead><tr><th>Persona</th><th>Tipo</th><th>Contacto</th><th>Estado</th><th></th></tr></thead><tbody>
            @for (person of people; track person.id) {
              <tr><td><div class="person-cell"><span class="initial">{{ person.fullName[0] }}</span><div><strong>{{ person.fullName }}</strong><small>{{ person.identifier }}</small></div></div></td><td><span class="tag neutral">{{ person.type }}</span></td><td><span>{{ person.email || 'Sin email' }}</span><small class="block">{{ person.phone || '' }}</small></td><td><span class="status" [class.off]="!person.isActive"><i></i>{{ person.isActive ? 'Activo' : 'Inactivo' }}</span></td><td><div class="row-actions"><button title="Editar" (click)="edit(person)"><i class="pi pi-pencil"></i></button><button title="Activar o desactivar" (click)="toggle(person)"><i class="pi pi-power-off"></i></button></div></td></tr>
            } @empty { <tr><td colspan="5" class="empty">No encontramos personas.</td></tr> }
          </tbody></table></div>
        </article>
        <article class="surface form-card">
          <div class="section-title"><div><h2>{{ editingId ? 'Editar persona' : 'Nueva persona' }}</h2><p>Datos mínimos para comenzar.</p></div>@if (editingId) { <button class="icon-button" (click)="resetPerson()"><i class="pi pi-times"></i></button> }</div>
          <form (ngSubmit)="savePerson()" class="form-grid">
            <label class="full">Nombre completo<input [(ngModel)]="personForm.fullName" name="fullName" required placeholder="Ej. Valentina Suárez"></label>
            <label>Identificador<input [(ngModel)]="personForm.identifier" name="identifier" required placeholder="DNI o legajo"></label>
            <label>Tipo<select [(ngModel)]="personForm.type" name="type">@for (type of catalog?.personTypes ?? []; track type) { <option [value]="type">{{ type }}</option> }</select></label>
            <label class="full">Email<input [(ngModel)]="personForm.email" name="email" type="email" placeholder="nombre@escuela.edu"></label>
            <label class="full">Teléfono<input [(ngModel)]="personForm.phone" name="phone" placeholder="11 5555 5555"></label>
            @if (!editingId) { <label class="check-row full"><input type="checkbox" [(ngModel)]="personForm.createAccount" name="createAccount"><span>Crear también una cuenta de acceso</span></label> }
            <button class="button primary full" type="submit"><i class="pi pi-save"></i>{{ editingId ? 'Guardar cambios' : 'Crear persona' }}</button>
          </form>
        </article>
      </section>
    }

    @if (activeTab === 'vinculos') {
      <section class="cards-grid two">
        <article class="surface form-card"><div class="section-title"><div><h2>Vínculo familiar</h2><p>Relacioná un responsable con un estudiante.</p></div></div><form class="form-grid" (ngSubmit)="createFamilyLink()"><label>Responsable<select [(ngModel)]="linkForm.responsibleId" name="responsibleId">@for (x of catalog?.responsibles ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><label>Estudiante<select [(ngModel)]="linkForm.studentId" name="studentId">@for (x of catalog?.students ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><label class="full">Parentesco<input [(ngModel)]="linkForm.relationship" name="relationship" placeholder="Madre, padre, tutor..."></label><button class="button primary full"><i class="pi pi-link"></i>Crear vínculo</button></form><div class="mini-list">@for (x of catalog?.familyLinks ?? []; track x.id) { <div><span><strong>{{ x.responsible }}</strong><small>{{ x.relationship }} de {{ x.student }}</small></span><i class="pi pi-check-circle"></i></div> }</div></article>
        <article class="surface form-card"><div class="section-title"><div><h2>Inscripción a curso</h2><p>Ubicá al estudiante en su curso activo.</p></div></div><form class="form-grid" (ngSubmit)="createEnrollment()"><label>Estudiante<select [(ngModel)]="enrollmentForm.studentId" name="studentId">@for (x of catalog?.students ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><label>Curso<select [(ngModel)]="enrollmentForm.courseId" name="courseId">@for (x of catalog?.courses ?? []; track x.id) { <option [ngValue]="x.id">{{ x.label }}</option> }</select></label><button class="button primary full"><i class="pi pi-user-plus"></i>Inscribir estudiante</button></form><div class="mini-list">@for (x of catalog?.enrollments ?? []; track x.id) { <div><span><strong>{{ x.student }}</strong><small>{{ x.course }}</small></span><i class="pi pi-check-circle"></i></div> }</div></article>
      </section>
    }

    @if (activeTab === 'asignaciones') {
      <section class="cards-grid two">
        <article class="surface form-card"><div class="section-title"><div><h2>Docente, curso y materia</h2><p>Define qué puede calificar cada docente.</p></div></div><form class="form-grid" (ngSubmit)="createTeachingAssignment()"><label>Docente<select [(ngModel)]="teachingForm.teacherId" name="teacherId">@for (x of catalog?.teachers ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><label>Curso<select [(ngModel)]="teachingForm.courseId" name="courseId">@for (x of catalog?.courses ?? []; track x.id) { <option [ngValue]="x.id">{{ x.label }}</option> }</select></label><label class="full">Materia<select [(ngModel)]="teachingForm.subjectId" name="subjectId">@for (x of catalog?.subjects ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><button class="button primary full"><i class="pi pi-plus"></i>Asignar docente</button></form><div class="mini-list">@for (x of catalog?.teachingAssignments ?? []; track x.id) { <div><span><strong>{{ x.teacher }}</strong><small>{{ x.subject }} · {{ x.course }}</small></span><i class="pi pi-check-circle"></i></div> }</div></article>
        <article class="surface form-card"><div class="section-title"><div><h2>Preceptor y curso</h2><p>Habilita la toma de asistencia.</p></div></div><form class="form-grid" (ngSubmit)="createPreceptorAssignment()"><label>Preceptor<select [(ngModel)]="preceptorForm.preceptorId" name="preceptorId">@for (x of catalog?.preceptors ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><label>Curso<select [(ngModel)]="preceptorForm.courseId" name="courseId">@for (x of catalog?.courses ?? []; track x.id) { <option [ngValue]="x.id">{{ x.label }}</option> }</select></label><button class="button primary full"><i class="pi pi-plus"></i>Asignar preceptor</button></form><div class="mini-list">@for (x of catalog?.preceptorAssignments ?? []; track x.id) { <div><span><strong>{{ x.preceptor }}</strong><small>{{ x.course }}</small></span><i class="pi pi-check-circle"></i></div> }</div></article>
      </section>
    }

    @if (activeTab === 'publicacion') {
      <section class="surface"><div class="section-title"><div><h2>Visibilidad para familias</h2><p>Controlá cuándo aparecen promedios y boletines.</p></div></div><div class="publication-grid">@for (term of catalog?.terms ?? []; track term.id) { <article><div><span class="tag neutral">2026</span><h3>{{ term.name }}</h3><small>{{ term.startDate | date:'dd/MM/yyyy':'UTC' }} — {{ term.endDate | date:'dd/MM/yyyy':'UTC' }}</small></div><label class="switch-row"><span><strong>Promedios</strong><small>Mostrar promedio general</small></span><input type="checkbox" [(ngModel)]="term.areAveragesPublished"></label><label class="switch-row"><span><strong>Boletín</strong><small>Mostrar detalle por materia</small></span><input type="checkbox" [(ngModel)]="term.isReportCardPublished"></label><button class="button secondary wide" (click)="savePublication(term)"><i class="pi pi-save"></i>Guardar configuración</button></article> }</div></section>
    }
  `
})
export class AdminComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  summary: any; catalog: any; people: any[] = []; activeTab = 'personas'; search = ''; error = ''; message = ''; editingId: number | null = null;
  personForm: any = { fullName: '', identifier: '', type: 'Alumno', email: '', phone: '', createAccount: false };
  linkForm: any = { responsibleId: null, studentId: null, relationship: 'Responsable' };
  enrollmentForm: any = { studentId: null, courseId: null };
  teachingForm: any = { teacherId: null, courseId: null, subjectId: null };
  preceptorForm: any = { preceptorId: null, courseId: null };
  ngOnInit() { this.refresh(); }
  refresh() { forkJoin({ summary: this.api.get<any>('/api/admin/summary'), catalog: this.api.get<any>('/api/admin/catalog') }).subscribe({ next: x => { this.summary = x.summary; this.catalog = x.catalog; this.setDefaults(); this.loadPeople(); this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  loadPeople() { this.api.get<any[]>(`/api/admin/people?search=${encodeURIComponent(this.search)}`).subscribe({ next: x => { this.people = x; this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  setDefaults() {
    this.linkForm.responsibleId ??= this.catalog.responsibles[0]?.id; this.linkForm.studentId ??= this.catalog.students[0]?.id;
    this.enrollmentForm.studentId ??= this.catalog.students[0]?.id; this.enrollmentForm.courseId ??= this.catalog.courses[0]?.id;
    this.teachingForm.teacherId ??= this.catalog.teachers[0]?.id; this.teachingForm.courseId ??= this.catalog.courses[0]?.id; this.teachingForm.subjectId ??= this.catalog.subjects[0]?.id;
    this.preceptorForm.preceptorId ??= this.catalog.preceptors[0]?.id; this.preceptorForm.courseId ??= this.catalog.courses[0]?.id;
  }
  edit(p: any) { this.editingId = p.id; this.personForm = { ...p, createAccount: false }; }
  resetPerson() { this.editingId = null; this.personForm = { fullName: '', identifier: '', type: 'Alumno', email: '', phone: '', createAccount: false }; }
  savePerson() { const request = this.editingId ? this.api.put(`/api/admin/people/${this.editingId}`, this.personForm) : this.api.post('/api/admin/people', this.personForm); request.subscribe({ next: () => { this.ok(this.editingId ? 'Persona actualizada.' : 'Persona creada.'); this.resetPerson(); this.refresh(); }, error: e => this.fail(e) }); }
  toggle(p: any) { this.api.post(`/api/admin/people/${p.id}/toggle`, {}).subscribe({ next: () => { this.ok('Estado actualizado.'); this.refresh(); }, error: e => this.fail(e) }); }
  createFamilyLink() { this.action('/api/admin/family-links', this.linkForm, 'Vínculo creado.'); }
  createEnrollment() { this.action('/api/admin/enrollments', this.enrollmentForm, 'Inscripción creada.'); }
  createTeachingAssignment() { this.action('/api/admin/teaching-assignments', this.teachingForm, 'Asignación docente creada.'); }
  createPreceptorAssignment() { this.action('/api/admin/preceptor-assignments', this.preceptorForm, 'Asignación de preceptor creada.'); }
  savePublication(term: any) { this.api.put(`/api/admin/terms/${term.id}/publication`, term).subscribe({ next: () => this.ok('Publicación actualizada.'), error: e => this.fail(e) }); }
  action(url: string, body: any, text: string) { this.api.post(url, body).subscribe({ next: () => { this.ok(text); this.refresh(); }, error: e => this.fail(e) }); }
  ok(text: string) { this.error = ''; this.message = text; this.cdr.detectChanges(); setTimeout(() => { this.message = ''; this.cdr.detectChanges(); }, 3000); }
  fail(e: any) { this.message = ''; this.error = e.error?.message ?? 'No pudimos completar la operación.'; this.cdr.detectChanges(); }
}
