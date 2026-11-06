import { Component, computed, input, output } from '@angular/core';

/**
 * Paginador simple: "Mostrando 16–30 de 120" y flechas para ir a la página anterior/siguiente.
 * No guarda estado propio: recibe la página actual y avisa cuál pidió el usuario.
 */
@Component({
  selector: 'app-pager',
  template: `
    <div class="pager">
      <span class="muted">
        @if (total() === 0) { Sin resultados }
        @else { Mostrando {{ from() }}–{{ to() }} de {{ total() }} }
      </span>
      <div class="actions">
        <button class="icon-button" [disabled]="page() <= 1" (click)="pageChange.emit(1)" title="Primera página"><i class="pi pi-angle-double-left"></i></button>
        <button class="icon-button" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)" title="Anterior"><i class="pi pi-angle-left"></i></button>
        <span class="page-number">{{ page() }} / {{ pages() }}</span>
        <button class="icon-button" [disabled]="page() >= pages()" (click)="pageChange.emit(page() + 1)" title="Siguiente"><i class="pi pi-angle-right"></i></button>
        <button class="icon-button" [disabled]="page() >= pages()" (click)="pageChange.emit(pages())" title="Última página"><i class="pi pi-angle-double-right"></i></button>
      </div>
    </div>
  `
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  readonly from = computed(() => (this.page() - 1) * this.pageSize() + 1);
  readonly to = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
}
