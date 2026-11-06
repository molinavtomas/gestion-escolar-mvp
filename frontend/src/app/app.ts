import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** Componente raíz: solo muestra la pantalla que corresponde a la URL. */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet />'
})
export class App {}
