# Aula Clara — MVP de gestión escolar

Aplicación web demostrable para centralizar personas, cursos, calificaciones, asistencia y consulta familiar. El frontend Angular y la API ASP.NET Core se publican juntos en un único contenedor; PostgreSQL se aloja en Neon.

## Alcance funcional

- Secretaría / Dirección: tablero, altas y edición de personas, activación, vínculos familiares, inscripciones, asignaciones docentes y de preceptoría, publicación de promedios y boletines.
- Docente: cursos y materias asignados, creación de evaluaciones, carga 1–10, observaciones, borradores, publicación, vista integral e historial de cambios.
- Preceptor: cursos asignados, asistencia por fecha, estados presente/ausente/tarde/justificado y resumen del período.
- Familia: estudiantes vinculados, notas publicadas, observaciones, asistencia, promedios y boletín cuando la institución los habilita.

## Tecnologías

- Angular 22, PrimeIcons y SCSS.
- ASP.NET Core 10, Identity, Entity Framework Core 10.
- PostgreSQL en Neon.
- Docker y Render para el despliegue.

## Ejecutar localmente

No hace falta PostgreSQL para probar el proyecto: si no existe `ConnectionStrings__DefaultConnection`, la API usa una base en memoria y carga datos demo.

Terminal 1:

```powershell
dotnet run --project backend/GestionEscolar.Api --urls http://localhost:5088
```

Terminal 2:

```powershell
cd frontend
npm install
npm start
```

Abrir `http://localhost:4200`.

## Cuentas demo

Todas usan la contraseña `Demo1234!` por defecto.

| Perfil | Usuario |
|---|---|
| Secretaría / Dirección | `secretaria@demo.edu` |
| Docente | `docente@demo.edu` |
| Preceptor | `preceptor@demo.edu` |
| Familia | `familia@demo.edu` |

En Render, la contraseña se configura con `DemoSeed__Password` y puede ser distinta.

## Base de datos y despliegue

La aplicación ejecuta automáticamente las migraciones pendientes y una carga inicial idempotente al arrancar. Variables necesarias en Render:

- `ConnectionStrings__DefaultConnection`: cadena PostgreSQL de Neon con SSL.
- `DemoSeed__Password`: contraseña inicial de las cuatro cuentas demo.
- `ASPNETCORE_ENVIRONMENT=Production`.

Render construye el `Dockerfile`; no es necesario tener Docker Desktop instalado localmente. El endpoint `/health` permite verificar el servicio.

## Estructura

```text
backend/GestionEscolar.Api/   API, dominio, migraciones y seed
frontend/                     SPA Angular
Dockerfile                    compilación multi-stage
render.yaml                   configuración declarativa de Render
PLAN_MVP.md                   plan de implementación aprobado
```

Este repositorio prioriza velocidad de validación comercial. No incluye todavía la etapa de endurecimiento de seguridad ni una suite automatizada de pruebas.
