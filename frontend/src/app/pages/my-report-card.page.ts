import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService, errorMessage } from '../core/api.service';
import { ReportCard } from '../core/models';
import { ReportCardComponent } from '../shared/report-card.component';

interface Child { studentId: number; fullName: string; course: string | null; isAccountOwner: boolean; }
interface AttendanceDay { date: string; status: string; observation: string | null; }

/**
 * Pantalla de la familia:
 *  - si hay hermanos vinculados, un selector para elegir hijo/a;
 *  - el boletín (solo lo publicado), para imprimir o guardar en PDF;
 *  - botón para confirmar la lectura (reemplaza la firma en papel);
 *  - la asistencia día por día.
 */
@Component({
  selector: 'app-my-report-card',
  imports: [ReportCardComponent, DatePipe],
  template: `
    <header class="page-header no-print">
      <h1>Boletín</h1>
      @if (card()) {
        <button class="button secondary" (click)="print()"><i class="pi pi-print"></i> Imprimir / guardar PDF</button>
      }
    </header>

    <!-- Selector de hijo/a: solo si la familia tiene más de uno -->
    @if (children().length > 1) {
      <div class="chips no-print">
        @for (c of children(); track c.studentId) {
          <button class="chip" [class.active]="c.studentId === selectedId()" (click)="select(c.studentId)">
            {{ c.fullName }} <small>{{ c.course }}</small>
          </button>
        }
      </div>
    }

    @if (error()) { <div class="alert error">{{ error() }}</div> }

    @if (card(); as c) {
      <app-report-card [card]="c" />

      <!-- Confirmación de lectura -->
      @if (c.publishedStage > 0 && c.isCurrentYear) {
        <section class="card no-print">
          @if (c.acknowledgedAtUtc) {
            <p><i class="pi pi-check-circle ok-text"></i> Ya confirmaste la lectura de esta etapa del boletín.</p>
          } @else {
            <p>¿Leíste el boletín? Confirmalo para que el colegio sepa que lo recibiste.</p>
            <button class="button primary" (click)="acknowledge()"><i class="pi pi-check"></i> Confirmar lectura</button>
          }
        </section>
      }

      <!-- Asistencia día por día -->
      <section class="card no-print">
        <h2>Asistencia día por día</h2>
        @if (attendance().length) {
          <div class="table-scroll attendance-days">
            <table class="simple-table">
              <thead><tr><th>Fecha</th><th>Estado</th><th>Observación</th></tr></thead>
              <tbody>
                @for (d of attendance(); track d.date) {
                  <tr>
                    <td>{{ d.date | date:'EEEE dd/MM' }}</td>
                    <td><span class="status-pill" [class]="statusClass(d.status)">{{ d.status }}</span></td>
                    <td>{{ d.observation ?? '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else { <p class="muted">Todavía no hay asistencia registrada.</p> }
      </section>
    } @else if (!error()) { <p class="muted">Cargando…</p> }
  `
})
export class MyReportCardPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly children = signal<Child[]>([]);
  readonly selectedId = signal<number | null>(null);
  readonly card = signal<ReportCard | null>(null);
  readonly attendance = signal<AttendanceDay[]>([]);
  readonly error = signal('');

  ngOnInit() {
    this.api.get<Child[]>('/api/family/children').subscribe({
      next: children => {
        this.children.set(children);
        // Arrancamos con el hijo/a de la cuenta con la que se ingresó.
        const first = children.find(c => c.isAccountOwner) ?? children[0];
        if (first) this.select(first.studentId);
      },
      error: e => this.error.set(errorMessage(e))
    });
  }

  select(studentId: number) {
    this.selectedId.set(studentId);
    this.error.set('');
    this.card.set(null);
    this.api.get<ReportCard>(`/api/report-cards/${studentId}`).subscribe({
      next: c => this.card.set(c),
      error: e => this.error.set(errorMessage(e))
    });
    this.api.get<AttendanceDay[]>(`/api/family/attendance/${studentId}`).subscribe(a => this.attendance.set(a));
  }

  acknowledge() {
    const c = this.card();
    if (!c) return;
    this.api.post(`/api/report-cards/${c.studentId}/ack`).subscribe({
      next: () => this.select(c.studentId),
      error: e => this.error.set(errorMessage(e))
    });
  }

  statusClass(status: string) {
    return ({ Presente: 'present', Ausente: 'absent', Tarde: 'late', Justificado: 'justified' } as Record<string, string>)[status] ?? '';
  }

  /** El diálogo de impresión del navegador permite elegir "Guardar como PDF". */
  print() { window.print(); }
}
