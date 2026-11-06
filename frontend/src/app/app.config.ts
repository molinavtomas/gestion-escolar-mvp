import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, TitleStrategy, RouterStateSnapshot } from '@angular/router';
import { Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { routes } from './app.routes';
import { credentialsInterceptor, sessionInterceptor } from './core/auth.service';

// Fechas y números en formato argentino (ej. 06/11/2026).
registerLocaleData(localeEsAr);

/** Pone en la pestaña del navegador "Pantalla · Colegio de los Buenos Ayres". */
@Injectable()
class SchoolTitleStrategy extends TitleStrategy {
  constructor(private readonly title: Title) { super(); }
  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · Colegio de los Buenos Ayres` : 'Colegio de los Buenos Ayres');
  }
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: 'es-AR' },
    provideRouter(routes),
    { provide: TitleStrategy, useClass: SchoolTitleStrategy },
    provideHttpClient(withInterceptors([credentialsInterceptor, sessionInterceptor]))
  ]
};
