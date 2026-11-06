import { Component, input } from '@angular/core';
import { ReportCard } from '../core/models';

/**
 * Boletín con el mismo formato que la planilla del colegio.
 * Es solo de presentación: recibe el boletín ya armado por la API y lo dibuja.
 * Lo usan la familia (Mi boletín) y Dirección/Preceptoría (Boletines).
 * Al imprimir (Ctrl+P o el botón "Imprimir") se ve igual que en papel, sin menús.
 */
@Component({
  selector: 'app-report-card',
  template: `
    @let c = card();
    <article class="report-card">
      <header class="rc-header">
        <img src="logo.png" alt="Escudo">
        <div>
          <h2>Colegio de los Buenos Ayres</h2>
          <p>Boletín de calificaciones · Ciclo lectivo {{ c.year }}</p>
        </div>
      </header>

      <dl class="rc-student">
        <div><dt>Alumno/a</dt><dd>{{ c.student }}</dd></div>
        <div><dt>DNI</dt><dd>{{ c.dni }}</dd></div>
        <div><dt>Curso</dt><dd>{{ c.course }}</dd></div>
      </dl>

      <div class="table-scroll">
        <table class="rc-table">
          <thead>
            <tr>
              <th class="rc-num" rowspan="3"></th>
              <th class="rc-subject" rowspan="3">MATERIAS</th>
              <th colspan="9" class="rc-title">CALIFICACIONES</th>
            </tr>
            <tr>
              <th colspan="3">1° CUATRIMESTRE</th>
              <th colspan="3">2° CUATRIMESTRE</th>
              <th colspan="2">INTENSIFICACIÓN</th>
              <th rowspan="2" class="rc-small">CALIFICACIÓN FINAL</th>
            </tr>
            <tr class="rc-small">
              <th>1° VALORACIÓN PRELIMINAR</th>
              <th>CALIFICACIÓN</th>
              <th>INTENSIFICACIÓN 1° CUATRIMESTRE</th>
              <th>2° VALORACIÓN PRELIMINAR</th>
              <th>CALIFICACIÓN</th>
              <th>INTENSIFICACIÓN 2° CUATRIMESTRE</th>
              <th>DICIEMBRE</th>
              <th>FEBRERO</th>
            </tr>
          </thead>
          <tbody>
            @for (row of c.rows; track row.subjectId; let i = $index) {
              <tr>
                <td class="rc-num">{{ i + 1 }}</td>
                <td class="rc-subject">{{ row.subject }}</td>
                <td>{{ row.values.valuation1 }}</td>
                <td [class.low]="isLow(row.values.grade1)">{{ row.values.grade1 }}</td>
                <td>{{ row.values.intensification1 }}</td>
                <td>{{ row.values.valuation2 }}</td>
                <td [class.low]="isLow(row.values.grade2)">{{ row.values.grade2 }}</td>
                <td>{{ row.values.intensification2 }}</td>
                <td>{{ row.values.december }}</td>
                <td>{{ row.values.february }}</td>
                <td class="rc-final" [class.low]="isLow(row.values.finalGrade)">{{ row.values.finalGrade }}</td>
              </tr>
            } @empty {
              <tr><td colspan="11" class="muted">Este año todavía no tiene materias cargadas.</td></tr>
            }
          </tbody>
        </table>
      </div>

      <footer class="rc-footer">
        <p>
          <strong>Asistencia:</strong>
          {{ c.attendance.present }} presentes · {{ c.attendance.absent }} ausentes ·
          {{ c.attendance.late }} tardes · {{ c.attendance.justified }} justificadas
        </p>
        @if (c.isFamilyView) {
          <p class="muted no-print">Publicado hasta: {{ c.publishedStageLabel }}. Las columnas vacías se completan a medida que Dirección publica.</p>
        }
        <p class="muted rc-legend">TEA: Trayectoria Educativa Alcanzada · TEP: En Proceso · TED: Discontinua</p>
        <div class="rc-signatures">
          <span>Firma Dirección</span>
          <span>Firma del responsable</span>
        </div>
      </footer>
    </article>
  `
})
export class ReportCardComponent {
  /** Boletín a mostrar (obligatorio). */
  readonly card = input.required<ReportCard>();

  /** En la Provincia de Buenos Aires se aprueba con 7: las notas menores se resaltan. */
  isLow(value: number | null) {
    return value !== null && value < 7;
  }
}
