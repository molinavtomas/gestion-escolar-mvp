import { Component, inject, OnInit, signal } from '@angular/core';
import { ApiService, errorMessage } from '../core/api.service';
import { ReportCard } from '../core/models';
import { ReportCardComponent } from '../shared/report-card.component';

/** Pantalla de la familia: el boletín de su hijo/a, listo para imprimir o guardar en PDF. */
@Component({
  selector: 'app-my-report-card',
  imports: [ReportCardComponent],
  template: `
    <header class="page-header no-print">
      <h1>Boletín</h1>
      @if (card()) {
        <button class="button secondary" (click)="print()"><i class="pi pi-print"></i> Imprimir / guardar PDF</button>
      }
    </header>

    @if (error()) { <div class="alert error">{{ error() }}</div> }
    @if (card(); as c) { <app-report-card [card]="c" /> }
    @else if (!error()) { <p class="muted">Cargando…</p> }
  `
})
export class MyReportCardPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly card = signal<ReportCard | null>(null);
  readonly error = signal('');

  ngOnInit() {
    this.api.get<ReportCard>('/api/report-cards/mine').subscribe({
      next: c => this.card.set(c),
      error: e => this.error.set(errorMessage(e))
    });
  }

  /** El diálogo de impresión del navegador permite elegir "Guardar como PDF". */
  print() { window.print(); }
}
