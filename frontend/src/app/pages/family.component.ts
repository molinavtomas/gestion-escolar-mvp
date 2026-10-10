import { ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-family',
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <header class="page-header"><div><span class="eyebrow">Portal de familias</span><h1>Trayectoria escolar</h1><p>Seguimiento claro y actualizado de cada estudiante.</p></div><span class="year-chip"><i class="pi pi-calendar"></i>Ciclo lectivo 2026</span></header>
    @if (error) { <div class="alert error"><i class="pi pi-exclamation-circle"></i>{{ error }}</div> }
    <section class="student-picker">
      @for (child of workspace?.children ?? []; track child.studentId) { <button [class.active]="studentId === child.studentId" (click)="studentId = child.studentId; loadOverview()"><span class="initial large">{{ child.name[0] }}</span><span><strong>{{ child.name }}</strong><small>{{ child.enrollment?.course }} · {{ child.relationship }}</small></span><i class="pi pi-chevron-right"></i></button> }
    </section>
    <section class="surface filters-panel family-filter"><label>Período<select [(ngModel)]="termId" (ngModelChange)="loadOverview()">@for (x of workspace?.terms ?? []; track x.id) { <option [ngValue]="x.id">{{ x.name }}</option> }</select></label><div class="publication-info"><i class="pi pi-info-circle"></i><span>La escuela define cuándo se habilitan promedios y boletines.</span></div></section>
    @if (overview) {
      <section class="family-hero"><div><span class="eyebrow light">{{ overview.course }} · {{ overview.term }}</span><h2>{{ overview.student }}</h2><p>Resumen del período seleccionado</p></div><div class="hero-score"><small>Promedio general</small><strong>{{ overview.average == null ? '—' : (overview.average | number:'1.1-2') }}</strong><span>{{ overview.areAveragesPublished ? 'Publicado' : 'Aún no publicado' }}</span></div></section>
      <section class="metrics-grid family-metrics">
        <article class="mini-metric"><span class="attendance-icon present"><i class="pi pi-check"></i></span><div><strong>{{ overview.attendanceSummary?.Presente ?? 0 }}</strong><small>Presentes</small></div></article>
        <article class="mini-metric"><span class="attendance-icon absent"><i class="pi pi-times"></i></span><div><strong>{{ overview.attendanceSummary?.Ausente ?? 0 }}</strong><small>Ausencias</small></div></article>
        <article class="mini-metric"><span class="attendance-icon late"><i class="pi pi-clock"></i></span><div><strong>{{ overview.attendanceSummary?.Tarde ?? 0 }}</strong><small>Llegadas tarde</small></div></article>
        <article class="mini-metric"><span class="attendance-icon justified"><i class="pi pi-file-check"></i></span><div><strong>{{ overview.attendanceSummary?.Justificado ?? 0 }}</strong><small>Justificadas</small></div></article>
      </section>
      <section class="family-columns">
        <article class="surface"><div class="section-title"><div><h2>Calificaciones publicadas</h2><p>{{ overview.grades.length }} resultados disponibles.</p></div><i class="pi pi-book section-icon"></i></div><div class="grade-list">@for (grade of overview.grades; track $index) { <div><span class="grade-badge large">{{ grade.score }}</span><span><strong>{{ grade.subject }}</strong><small>{{ grade.evaluation }} · {{ grade.date | date:'dd/MM/yyyy':'UTC' }}</small>@if (grade.observation) { <p>“{{ grade.observation }}”</p> }</span></div> } @empty { <div class="empty compact"><i class="pi pi-lock"></i> No hay calificaciones publicadas para este período.</div> }</div></article>
        <article class="surface"><div class="section-title"><div><h2>Asistencia reciente</h2><p>Movimientos dentro del período.</p></div><i class="pi pi-calendar section-icon"></i></div><div class="attendance-list">@for (item of overview.attendance; track $index) { <div><span [class]="'attendance-icon ' + statusClass(item.status)"><i [class]="statusIcon(item.status)"></i></span><span><strong>{{ item.status }}</strong><small>{{ item.date | date:"EEEE d 'de' MMMM":'UTC' }}</small>@if (item.observation) { <p>{{ item.observation }}</p> }</span></div> } @empty { <div class="empty compact"><i class="pi pi-calendar-times"></i> No hay registros en este período.</div> }</div></article>
      </section>
      <section class="surface spaced-top report-card"><div class="section-title"><div><h2>Boletín del período</h2><p>Promedios consolidados por materia.</p></div><span class="tag" [class.success]="overview.isReportCardPublished" [class.warning]="!overview.isReportCardPublished"><i [class]="overview.isReportCardPublished ? 'pi pi-lock-open' : 'pi pi-lock'"></i>{{ overview.isReportCardPublished ? 'Habilitado' : 'Próximamente' }}</span></div>
        @if (overview.isReportCardPublished) { <div class="subject-averages">@for (subject of overview.subjectAverages; track subject.subject) { <article><span><strong>{{ subject.subject }}</strong><small>{{ subject.grades }} calificaciones</small></span><b>{{ subject.average | number:'1.1-2' }}</b></article> }</div> } @else { <div class="locked-state"><span><i class="pi pi-lock"></i></span><div><h3>El boletín todavía no fue publicado</h3><p>Vas a verlo acá cuando la escuela complete y habilite el período.</p></div></div> }
      </section>
    } @else { <section class="surface loading-state"><i class="pi pi-spin pi-spinner"></i>Cargando información...</section> }
  `
})
export class FamilyComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  workspace: any; overview: any; studentId: number | null = null; termId: number | null = null; error = '';
  ngOnInit() { this.api.get<any>('/api/family/workspace').subscribe({ next: x => { this.workspace = x; this.studentId = x.children[0]?.studentId; this.termId = x.terms[x.terms.length - 1]?.id; this.loadOverview(); this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  loadOverview() { if (!this.studentId || !this.termId) return; this.overview = null; this.api.get<any>(`/api/family/children/${this.studentId}/overview?termId=${this.termId}`).subscribe({ next: x => { this.overview = x; this.cdr.detectChanges(); }, error: e => this.fail(e) }); }
  statusClass(status: string) { return ({ Presente: 'present', Ausente: 'absent', Tarde: 'late', Justificado: 'justified' } as Record<string, string>)[status]; }
  statusIcon(status: string) { return ({ Presente: 'pi pi-check', Ausente: 'pi pi-times', Tarde: 'pi pi-clock', Justificado: 'pi pi-file-check' } as Record<string, string>)[status]; }
  fail(e: any) { this.error = e.error?.message ?? 'No pudimos cargar la información.'; this.cdr.detectChanges(); }
}
