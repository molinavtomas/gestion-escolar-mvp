import { DatePipe } from '@angular/common';
import { ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-teacher',
  imports: [FormsModule, DatePipe],
  template: `
    <header class="page-header">
      <div><span class="eyebrow">Espacio docente</span><h1>Calificaciones</h1><p>Cargá, revisá y publicá el trabajo de tus cursos.</p></div>
      <button class="button secondary" (click)="showNewEvaluation = !showNewEvaluation"><i class="pi pi-plus"></i>Nueva evaluación</button>
    </header>
    @if (message) { <div class="alert success"><i class="pi pi-check-circle"></i>{{ message }}</div> }
    @if (error) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error }}</div> }

    <section class="surface filters-panel">
      <label>Curso y materia<select [(ngModel)]="assignmentId" (ngModelChange)="loadGradebook()">@for (x of workspace?.assignments ?? []; track x.id) { <option [ngValue]="x.id">{{ x.course }} · {{ x.subject }}</option> }</select></label>
      <label>Período<select [(ngModel)]="termId" (ngModelChange)="loadGradebook()">@for (x of workspace?.terms ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label>
      <label>Evaluación<select [(ngModel)]="evaluationId" (ngModelChange)="loadGradebook(evaluationId)">@for (x of book?.evaluations ?? []; track x.id) { <option [ngValue]="x.id">{{ x.title }}</option> }</select></label>
      <button class="button ghost" (click)="loadOverview()"><i class="pi pi-chart-bar"></i>Vista del curso</button>
    </section>

    @if (showNewEvaluation) {
      <section class="surface compact-form"><div><h2>Nueva evaluación</h2><p>Se agregará a {{ selectedAssignmentLabel() }}.</p></div><label>Título<input [(ngModel)]="newEvaluation.title" placeholder="Ej. Trabajo práctico"></label><label>Fecha<input type="date" [(ngModel)]="newEvaluation.date"></label><button class="button primary" (click)="createEvaluation()"><i class="pi pi-plus"></i>Crear</button></section>
    }

    <section class="surface">
      <div class="section-title"><div><h2>{{ selectedEvaluation()?.title || 'Libro de calificaciones' }}</h2><p>{{ book?.assignment?.course }} · {{ book?.assignment?.subject }} · {{ rows.length }} estudiantes</p></div><div class="legend"><span><i class="dot draft"></i>Borrador</span><span><i class="dot published"></i>Publicado</span></div></div>
      @if (book?.evaluations?.length) {
        <div class="table-wrap"><table class="grade-table"><thead><tr><th>Estudiante</th><th class="score-col">Nota</th><th>Observación</th><th>Estado</th><th>Historial</th></tr></thead><tbody>
          @for (row of rows; track row.enrollmentId) {
            <tr><td><div class="person-cell"><span class="initial alt">{{ row.student[0] }}</span><strong>{{ row.student }}</strong></div></td><td><input class="score-input" type="number" min="1" max="10" [(ngModel)]="row.score" placeholder="—"></td><td><input [(ngModel)]="row.observation" placeholder="Comentario opcional"></td><td><span class="tag" [class.success]="row.isPublished" [class.warning]="!row.isPublished">{{ row.isPublished ? 'Publicado' : 'Borrador' }}</span></td><td><button class="icon-button" [disabled]="!row.gradeId" (click)="loadHistory(row)"><i class="pi pi-history"></i></button></td></tr>
          }
        </tbody></table></div>
        <div class="action-bar"><div><i class="pi pi-info-circle"></i>Las familias solo ven las notas publicadas.</div><div><button class="button secondary" (click)="save(false)"><i class="pi pi-save"></i>Guardar borrador</button><button class="button primary" (click)="save(true)"><i class="pi pi-send"></i>Guardar y publicar</button></div></div>
      } @else {
        <div class="empty-state"><span><i class="pi pi-file-edit"></i></span><h3>Todavía no hay evaluaciones</h3><p>Creá la primera evaluación para comenzar a cargar notas.</p><button class="button primary" (click)="showNewEvaluation = true">Crear evaluación</button></div>
      }
    </section>

    @if (overview) {
      <section class="surface spaced-top"><div class="section-title"><div><h2>Vista integral del curso</h2><p>Calificaciones propias y publicadas de otras materias.</p></div><button class="icon-button" (click)="overview = null"><i class="pi pi-times"></i></button></div><div class="table-wrap"><table><thead><tr><th>Estudiante</th><th>Materia</th><th>Evaluación</th><th>Nota</th><th>Estado</th></tr></thead><tbody>@for (x of overview; track $index) { <tr><td>{{ x.student }}</td><td>{{ x.subject }}</td><td>{{ x.evaluation }}</td><td><strong class="grade-badge">{{ x.score }}</strong></td><td><span class="tag" [class.success]="x.isPublished" [class.warning]="!x.isPublished">{{ x.isPublished ? 'Publicado' : 'Borrador' }}</span></td></tr> } @empty { <tr><td colspan="5" class="empty">No hay notas en este período.</td></tr> }</tbody></table></div></section>
    }

    @if (history) {
      <div class="modal-backdrop" (click)="history = null"><section class="modal" (click)="$event.stopPropagation()"><div class="section-title"><div><h2>Historial de {{ historyStudent }}</h2><p>Cambios registrados para esta calificación.</p></div><button class="icon-button" (click)="history = null"><i class="pi pi-times"></i></button></div><div class="timeline">@for (x of history; track $index) { <div><i class="pi pi-history"></i><span><strong>{{ x.changedBy }}</strong><p>Nota: {{ x.previousScore ?? 'sin nota' }} → {{ x.newScore }} · {{ x.newPublished ? 'Publicado' : 'Borrador' }}</p><small>{{ x.changedAtUtc | date:'dd/MM/yyyy HH:mm' }}</small></span></div> }</div></section></div>
    }
  `
})
export class TeacherComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  workspace: any; book: any; rows: any[] = []; assignmentId: number | null = null; termId: number | null = null; evaluationId: number | null = null;
  overview: any[] | null = null; history: any[] | null = null; historyStudent = ''; showNewEvaluation = false; message = ''; error = '';
  newEvaluation = { title: '', date: new Date().toISOString().slice(0, 10) };
  ngOnInit() { this.api.get<any>('/api/teacher/workspace').subscribe({ next: x => { this.workspace = x; this.assignmentId = x.assignments[0]?.id; this.termId = x.terms[x.terms.length - 1]?.id; this.loadGradebook(); this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  loadGradebook(evaluationId?: number | null) {
    if (!this.assignmentId || !this.termId) return;
    const query = evaluationId ? `&evaluationId=${evaluationId}` : '';
    this.api.get<any>(`/api/teacher/gradebook?assignmentId=${this.assignmentId}&termId=${this.termId}${query}`).subscribe({ next: x => {
      this.book = x; this.evaluationId = x.selectedEvaluationId;
      this.rows = x.students.map((student: any) => { const grade = x.grades.find((g: any) => g.enrollmentId === student.enrollmentId); return { ...student, gradeId: grade?.id, score: grade?.score ?? null, observation: grade?.observation ?? '', isPublished: grade?.isPublished ?? false }; }); this.cdr.detectChanges();
    }, error: e => this.fail(e) });
  }
  selectedEvaluation() { return this.book?.evaluations?.find((x: any) => x.id === this.evaluationId); }
  selectedAssignmentLabel() { const x = this.workspace?.assignments?.find((a: any) => a.id === this.assignmentId); return x ? `${x.course} · ${x.subject}` : 'la materia'; }
  createEvaluation() { if (!this.assignmentId || !this.termId || !this.newEvaluation.title.trim()) return; this.api.post<any>('/api/teacher/evaluations', { assignmentId: this.assignmentId, termId: this.termId, ...this.newEvaluation }).subscribe({ next: x => { this.showNewEvaluation = false; this.newEvaluation.title = ''; this.ok('Evaluación creada.'); this.loadGradebook(x.id); }, error: e => this.fail(e) }); }
  save(publish: boolean) {
    if (!this.evaluationId) return;
    const grades = this.rows.filter(x => Number(x.score) >= 1 && Number(x.score) <= 10).map(x => ({ enrollmentId: x.enrollmentId, score: Number(x.score), observation: x.observation }));
    this.api.post('/api/teacher/grades', { evaluationId: this.evaluationId, publish, grades }).subscribe({ next: () => { this.ok(publish ? 'Notas publicadas para las familias.' : 'Borrador guardado.'); this.loadGradebook(this.evaluationId); }, error: e => this.fail(e) });
  }
  loadOverview() { const a = this.workspace?.assignments?.find((x: any) => x.id === this.assignmentId); if (!a || !this.termId) return; this.api.get<any[]>(`/api/teacher/course-overview?courseId=${a.courseId}&termId=${this.termId}`).subscribe({ next: x => { this.overview = x; this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  loadHistory(row: any) { if (!row.gradeId) return; this.historyStudent = row.student; this.api.get<any[]>(`/api/teacher/grades/${row.gradeId}/history`).subscribe({ next: x => { this.history = x; this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  ok(text: string) { this.error = ''; this.message = text; this.cdr.detectChanges(); setTimeout(() => { this.message = ''; this.cdr.detectChanges(); }, 3000); }
  fail(e: any) { this.message = ''; this.error = e.error?.message ?? 'No pudimos completar la operación.'; this.cdr.detectChanges(); }
}
