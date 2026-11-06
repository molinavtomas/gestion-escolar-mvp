import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { Paged } from '../core/models';
import { CourseFilterComponent } from '../shared/course-filter.component';
import { PagerComponent } from '../shared/pager.component';

interface AuditRow { atUtc: string; userDni: string | null; userName: string | null; role: string | null; action: string; detail: string | null; ip: string | null; }
interface GradeChangeRow { changedAtUtc: string; changedBy: string; field: string; oldValue: string | null; newValue: string | null; student: string; subject: string; course: string; }

type Tab = 'Acceso' | 'Cambio' | 'Notas';

/**
 * Auditoría (solo Administrador):
 *  - Ingresos: cada intento de entrar (correcto, clave incorrecta, bloqueado...).
 *  - Cambios administrativos: altas, bajas, publicaciones, asignaciones, configuración.
 *  - Cambios de notas: historial global con filtros por curso y búsqueda.
 */
@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe, PagerComponent, CourseFilterComponent],
  template: `
    <header class="page-header"><h1>Auditoría</h1></header>

    <div class="chips tabs">
      <button class="chip" [class.active]="tab() === 'Acceso'" (click)="setTab('Acceso')">Ingresos</button>
      <button class="chip" [class.active]="tab() === 'Cambio'" (click)="setTab('Cambio')">Cambios administrativos</button>
      <button class="chip" [class.active]="tab() === 'Notas'" (click)="setTab('Notas')">Cambios de notas</button>
    </div>

    @if (error()) { <div class="alert error">{{ error() }}</div> }

    <section class="card">
      <div class="filters">
        <input class="search" [(ngModel)]="search" (keyup.enter)="goTo(1)" [placeholder]="tab() === 'Notas' ? 'Alumno, DNI, materia o quién cambió' : 'DNI, nombre o acción'">
        @if (tab() === 'Notas') {
          <app-course-filter [(courseId)]="courseId" (changed)="goTo(1)" />
        } @else {
          <label class="inline">Desde <input type="date" [(ngModel)]="from"></label>
          <label class="inline">Hasta <input type="date" [(ngModel)]="to"></label>
        }
        <button class="button secondary" (click)="goTo(1)"><i class="pi pi-search"></i> Buscar</button>
      </div>

      <div class="table-scroll">
        @if (tab() === 'Notas') {
          <table class="simple-table">
            <thead><tr><th>Fecha</th><th>Alumno/a</th><th>Curso</th><th>Materia</th><th>Columna</th><th>Antes</th><th>Después</th><th>Quién</th></tr></thead>
            <tbody>
              @for (r of grades()?.items ?? []; track $index) {
                <tr><td>{{ r.changedAtUtc | date:'dd/MM/yy HH:mm' }}</td><td>{{ r.student }}</td><td>{{ r.course }}</td><td>{{ r.subject }}</td>
                  <td>{{ r.field }}</td><td>{{ r.oldValue ?? '–' }}</td><td>{{ r.newValue ?? '–' }}</td><td>{{ r.changedBy }}</td></tr>
              } @empty { <tr><td colspan="8" class="muted">Sin resultados.</td></tr> }
            </tbody>
          </table>
        } @else {
          <table class="simple-table">
            <thead><tr><th>Fecha</th><th>Usuario</th><th>Rol</th><th>{{ tab() === 'Acceso' ? 'Resultado' : 'Acción' }}</th><th>Detalle</th><th>IP</th></tr></thead>
            <tbody>
              @for (r of entries()?.items ?? []; track $index) {
                <tr>
                  <td>{{ r.atUtc | date:'dd/MM/yy HH:mm' }}</td>
                  <td>{{ r.userName ?? '—' }} <small class="muted">{{ r.userDni }}</small></td>
                  <td>{{ r.role ?? '—' }}</td>
                  <td [class.low]="tab() === 'Acceso' && r.action !== 'Ingreso correcto'">{{ r.action }}</td>
                  <td>{{ r.detail ?? '' }}</td>
                  <td><small class="muted">{{ r.ip }}</small></td>
                </tr>
              } @empty { <tr><td colspan="6" class="muted">Sin resultados.</td></tr> }
            </tbody>
          </table>
        }
      </div>

      @if (tab() === 'Notas' ? grades() : entries(); as r) {
        <app-pager [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="goTo($event)" />
      }
    </section>
  `
})
export class AuditPage implements OnInit {
  private readonly api = inject(ApiService);

  readonly tab = signal<Tab>('Acceso');
  readonly entries = signal<Paged<AuditRow> | null>(null);
  readonly grades = signal<Paged<GradeChangeRow> | null>(null);
  readonly error = signal('');
  readonly courseId = signal<number | null>(null);

  search = '';
  from = '';
  to = '';

  ngOnInit() { this.goTo(1); }

  setTab(tab: Tab) {
    this.tab.set(tab);
    this.search = '';
    this.goTo(1);
  }

  goTo(page: number) {
    this.error.set('');
    if (this.tab() === 'Notas') {
      const p = new URLSearchParams({ page: String(page), search: this.search });
      if (this.courseId()) p.set('courseId', String(this.courseId()));
      this.api.get<Paged<GradeChangeRow>>(`/api/audit/grades?${p}`).subscribe({ next: r => this.grades.set(r), error: e => this.error.set(errorMessage(e)) });
    } else {
      const p = new URLSearchParams({ kind: this.tab(), page: String(page), search: this.search });
      if (this.from) p.set('from', this.from);
      if (this.to) p.set('to', this.to);
      this.api.get<Paged<AuditRow>>(`/api/audit?${p}`).subscribe({ next: r => this.entries.set(r), error: e => this.error.set(errorMessage(e)) });
    }
  }
}
