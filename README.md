# Colegio de los Buenos Ayres — Gestión escolar

Sistema web para cargar calificaciones, tomar asistencia y publicar el **boletín** de cada alumno con el mismo formato que la planilla del colegio.

## Qué hace cada rol

| Rol | Ingresa con | Puede |
|---|---|---|
| Dirección / Secretaría | su DNI | Panel de avance de carga, publicar el boletín por etapas, alta e importación de personas, cursos y materias, asignar profesores y preceptores, ver e imprimir boletines, corregir notas. |
| Profesor | su DNI | Cargar notas **solo de sus materias y cursos** en una grilla con las columnas del boletín. Ver el historial de cambios. |
| Preceptor | su DNI | Tomar asistencia de sus cursos y ver/imprimir sus boletines. |
| Familia | **DNI del alumno** | Ver el boletín de su hijo/a (solo lo publicado por Dirección) e imprimirlo o guardarlo en PDF. |

- La clave inicial es el mismo DNI. Cada usuario puede cambiarla desde "Cambiar clave" (se recomienda con un aviso). Secretaría puede volverla al DNI si alguien la olvida.
- Tras 5 intentos fallidos la cuenta se bloquea 5 minutos.

## El boletín

Columnas (iguales a la planilla): 1° cuatrimestre (valoración preliminar, calificación, intensificación), 2° cuatrimestre (ídem), intensificación de diciembre y febrero, y calificación final.

- Valoración preliminar: `TEA`, `TEP`, `TED` o un número del 1 al 10.
- Calificaciones: enteros del 1 al 10. Las menores a 7 se resaltan en rojo.
- Las materias se definen **por año** (1° a 6°) y su orden es el del boletín. Las de 1° son las de la planilla del colegio; las de 2° y 3° son las del diseño curricular de la Provincia (confirmar); 4° a 6° se cargan desde "Cursos y materias".
- **Publicación por etapas**: Dirección elige hasta dónde ven las familias (1° valoración → 1° cuatrimestre → 2° valoración → 2° cuatrimestre → final). Los profesores pueden cargar siempre.
- Cada cambio de nota queda en un historial (quién, cuándo, valor anterior y nuevo).

## Ejecutar localmente

Sin cadena de conexión, la API usa una base **en memoria** con datos de ejemplo (se pierden al cerrar).

```powershell
# Terminal 1 — API en http://localhost:5088
dotnet run --project backend/GestionEscolar.Api --urls http://localhost:5088

# Terminal 2 — Frontend en http://localhost:4200 (redirige /api al puerto 5088)
cd frontend
npm install
npm start
```

### Cuentas de ejemplo (clave = DNI)

| Rol | DNI |
|---|---|
| Dirección | 20111111 |
| Secretaría | 20222222 |
| Preceptor | 20333333 |
| Profesora (Matemática y Cs. Naturales) | 20444444 |
| Familia de Acosta Martina (1° A) | 50111001 |

## Configuración

| Variable | Uso |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL (Neon). Acepta la URL `postgresql://...` tal como la da Neon. |
| `Seed__DemoData` | `true` carga datos de ejemplo si la base está vacía. Con datos reales: `false`. |
| `Seed__DirectorDni` / `Seed__DirectorName` | Con `DemoData=false`, crea la primera cuenta de Dirección para empezar a cargar. |
| `ASPNETCORE_ENVIRONMENT` | `Production` en el servidor. |

Al arrancar, la app aplica las migraciones pendientes y carga los datos iniciales (sin duplicar).

## Despliegue

- **Render** (servicio Docker). Recomendado el plan **Starter** (US$7/mes): no se duerme y el link abre al instante. El plan Free tarda ~1 minuto en despertar tras 15 minutos sin uso.
- **Neon** (PostgreSQL, plan gratuito) para la base.
- Cada push a `main` despliega automáticamente.

## Estructura del código

```text
backend/GestionEscolar.Api/
  Program.cs              configuración: base, login por cookie, servicios
  Data/                   entidades, roles, DbContext y datos iniciales
  Auth/                   claims de la sesión y reglas de acceso (quién ve qué)
  Services/               lógica del boletín y alta de personas
  Controllers/            endpoints HTTP (/api/...)
  Migrations/             migraciones de EF Core
frontend/src/app/
  core/                   modelos, servicio HTTP, sesión y guards
  shell/                  barra lateral con menú por rol
  pages/                  una pantalla por archivo
  shared/                 componente del boletín (pantalla e impresión)
frontend/src/styles.scss  todos los estilos (colores del escudo)
```

Para generar una migración nueva después de cambiar entidades:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add NombreDelCambio --project backend/GestionEscolar.Api
```
