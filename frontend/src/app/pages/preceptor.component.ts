import { DecimalPipe } from '@angular/common';
import { ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-preceptor',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-header"><div><span class="eyebrow">Preceptoría</span><h1>Asistencia diaria</h1><p>Registrá el presente y acompañá la trayectoria del curso.</p></div><button class="button secondary" (click)="loadSummary()"><i class="pi pi-chart-bar"></i>Resumen del período</button></header>
    @if (message) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message }}</div> }
    @if (error) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error }}</div> }
    <section class="surface filters-panel">
      <label>Curso<select [(ngModel)]="courseId" (ngModelChange)="loadAttendance()">@for (x of workspace?.courses ?? []; track x.courseId) { <option [ngValue]="x.courseId">{{ x.label }}</option> }</select></label>
      <label>Período<select [(ngModel)]="termId" (ngModelChange)="loadAttendance()">@for (x of workspace?.terms ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label>
      <label>Fecha<input type="date" [(ngModel)]="date" (ngModelChange)="loadAttendance()"></label>
      <div class="quick-actions"><span>Marcar todos</span><button class="tag success" (click)="markAll('Presente')">Presentes</button></div>
    </section>
    <section class="metrics-grid attendance-metrics">
      @for (status of statuses; track status) { <article class="mini-metric"><span [class]="'attendance-icon ' + statusClass(status)"><i [class]="statusIcon(status)"></i></span><div><strong>{{ count(status) }}</strong><small>{{ status }}</small></div></article> }
    </section>
    <section class="surface">
      <div class="section-title"><div><h2>Lista del curso</h2><p>{{ rows.length }} estudiantes · asistencia del {{ date }}</p></div><span class="tag neutral"><i class="pi pi-clock"></i> Actualización en línea</span></div>
      <div class="table-wrap"><table class="attendance-table"><thead><tr><th>Estudiante</th><th>Estado</th><th>Observación</th><th>Notas publicadas</th><th>Promedio</th></tr></thead><tbody>
        @for (row of rows; track row.enrollmentId) { <tr><td><div class="person-cell"><span class="initial alt">{{ row.student[0] }}</span><div><strong>{{ row.student }}</strong><small>{{ row.identifier }}</small></div></div></td><td><div class="segmented">@for (status of statuses; track status) { <button [class.active]="row.status === status" [class]="statusClass(status)" (click)="row.status = status" [title]="status"><i [class]="statusIcon(status)"></i><span>{{ shortStatus(status) }}</span></button> }</div></td><td><input [(ngModel)]="row.observation" placeholder="Opcional"></td><td><span class="tag neutral">{{ row.publishedGrades }}</span></td><td><strong>{{ row.average == null ? '—' : (row.average | number:'1.1-1') }}</strong></td></tr> }
      </tbody></table></div>
      <div class="action-bar"><div><i class="pi pi-info-circle"></i>Podés corregir la asistencia volviendo a guardar esta fecha.</div><button class="button primary" (click)="save()"><i class="pi pi-save"></i>Guardar asistencia</button></div>
    </section>
    @if (summary) { <section class="surface spaced-top"><div class="section-title"><div><h2>Resumen del período</h2><p>Asistencia acumulada y desempeño publicado.</p></div><button class="icon-button" (click)="summary = null"><i class="pi pi-times"></i></button></div><div class="table-wrap"><table><thead><tr><th>Estudiante</th><th>Presentes</th><th>Ausentes</th><th>Tardes</th><th>Justificadas</th><th>Promedio</th></tr></thead><tbody>@for (x of summary; track x.student) { <tr><td><strong>{{ x.student }}</strong></td><td>{{ x.present }}</td><td>{{ x.absent }}</td><td>{{ x.late }}</td><td>{{ x.justified }}</td><td><strong>{{ x.average == null ? '—' : (x.average | number:'1.1-1') }}</strong></td></tr> }</tbody></table></div></section> }
  `
})
export class PreceptorComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  workspace: any; rows: any[] = []; summary: any[] | null = null; courseId: number | null = null; termId: number | null = null; date = new Date().toISOString().slice(0, 10); statuses: string[] = []; message = ''; error = '';
  ngOnInit() { this.api.get<any>('/api/preceptor/workspace').subscribe({ next: x => { this.workspace = x; this.statuses = x.statuses; this.courseId = x.courses[0]?.courseId; this.termId = x.terms[x.terms.length - 1]?.id; this.loadAttendance(); this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  loadAttendance() { if (!this.courseId || !this.date) return; const term = this.termId ? `&termId=${this.termId}` : ''; this.api.get<any[]>(`/api/preceptor/attendance?courseId=${this.courseId}&date=${this.date}${term}`).subscribe({ next: x => { this.rows = x.map(row => ({ ...row, status: row.attendance?.status ?? 'Presente', observation: row.attendance?.observation ?? '' })); this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  markAll(status: string) { this.rows.forEach(x => x.status = status); }
  count(status: string) { return this.rows.filter(x => x.status === status).length; }
  save() { if (!this.courseId) return; const entries = this.rows.map(x => ({ enrollmentId: x.enrollmentId, status: x.status, observation: x.observation })); this.api.post('/api/preceptor/attendance', { courseId: this.courseId, date: this.date, entries }).subscribe({ next: () => this.ok('Asistencia guardada.'), error: e => this.fail(e) }); }
  loadSummary() { if (!this.courseId || !this.termId) return; this.api.get<any[]>(`/api/preceptor/course-summary?courseId=${this.courseId}&termId=${this.termId}`).subscribe({ next: x => { this.summary = x; this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  statusClass(status: string) { return ({ Presente: 'present', Ausente: 'absent', Tarde: 'late', Justificado: 'justified' } as Record<string, string>)[status]; }
  statusIcon(status: string) { return ({ Presente: 'pi pi-check', Ausente: 'pi pi-times', Tarde: 'pi pi-clock', Justificado: 'pi pi-file-check' } as Record<string, string>)[status]; }
  shortStatus(status: string) { return ({ Presente: 'P', Ausente: 'A', Tarde: 'T', Justificado: 'J' } as Record<string, string>)[status]; }
  ok(text: string) { this.error = ''; this.message = text; this.cdr.detectChanges(); setTimeout(() => { this.message = ''; this.cdr.detectChanges(); }, 3000); }
  fail(e: any) { this.message = ''; this.error = e.error?.message ?? 'No pudimos completar la operación.'; this.cdr.detectChanges(); }
}
