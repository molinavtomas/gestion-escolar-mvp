# Plan del primer MVP funcional — Gestión escolar

Estado: propuesta para revisar antes de implementar. El repositorio está inicializado en `main`, conectado a `https://github.com/molinavtomas/gestion-escolar-mvp.git` y todavía no contiene aplicación.

## 1. Resultado que vamos a entregar

Una aplicación web pública de demostración, con una sola URL de Render, que permita recorrer una historia escolar completa con datos ficticios:

1. Secretaría crea o consulta personas, vincula responsables con alumnos, inscribe alumnos y asigna docentes y preceptores.
2. Un docente crea una evaluación, carga calificaciones y observaciones, guarda borradores, publica y corrige una nota dejando historial.
3. Un preceptor registra asistencia diaria y consulta un resumen del curso.
4. Un responsable entra con su propia cuenta y ve únicamente a sus hijos y la información publicada; ve promedios y boletín solo cuando Dirección los habilita.

La demo queda instalada en Neon y Render, con instrucciones breves para volver a desplegarla y para preparar la presentación al cliente.

## 2. Alcance funcional propuesto

| Área | Pantallas y acciones del primer MVP |
| --- | --- |
| Acceso | Inicio/cierre de sesión; cuentas de demostración individuales para Secretaría/Dirección, docente, preceptor y responsable; pantalla inicial según perfil; activación, desactivación y restablecimiento manual de cuentas desde administración. |
| Personas | Listar, buscar, crear, editar y desactivar personas; asociar perfil de alumno, docente, preceptor o responsable; vincular un responsable con uno o más alumnos. |
| Organización | Gestionar ciclo lectivo, tres trimestres, cursos/divisiones y materias; inscribir alumnos; asignar materias a docentes y cursos a preceptores. |
| Calificaciones | Docente ve el curso y todas sus notas; crea evaluación en una materia asignada; carga nota entera de 1 a 10 y observación; guarda borrador, publica y corrige. Un historial muestra valor anterior, nuevo valor, usuario y fecha. |
| Asistencia | Preceptor elige curso y fecha, marca presente/ausente/tarde/justificado, consulta información académica de sus cursos en modo lectura y ve resúmenes por alumno y curso. |
| Publicación | Secretaría/Dirección habilita o deshabilita promedios y boletín por trimestre. El responsable solo consulta notas y asistencia publicadas y sus hijos vinculados. |
| Boletín | Vista web imprimible con materias, notas publicadas, promedio simple y resumen de asistencia del trimestre. La impresión del navegador permite guardarlo como PDF. |
| Demo | Datos ficticios coherentes: al menos dos cursos, varias materias, docentes, un preceptor, alumnos, responsables y notas en distintos estados. |

El acceso por perfil, asignación, curso y vínculo familiar es parte del funcionamiento solicitado en el tablero. Lo implementaremos en las consultas y operaciones de la API, además de reflejarlo en la interfaz.

## 3. Supuestos para poder avanzar sin esperar definiciones

- Una sola institución y un ciclo lectivo de demostración. El modelo permite agregar otro ciclo, pero no habrá configuración multiinstitución.
- Tres trimestres. Las notas son enteros de 1 a 10. El promedio de demo es aritmético, sin ponderación ni reglas oficiales de promoción; se muestra únicamente cuando está habilitado.
- Asistencia por alumno, curso y día con los cuatro estados del tablero. Al guardar la asistencia, queda visible para la familia; una corrección reemplaza el estado anterior y queda identificada en el historial general de cambios.
- Boletín imprimible con formato de demo, sin pretender reproducir todavía el documento oficial del colegio.
- Los datos se cargan mediante formularios y datos iniciales ficticios. La importación del Excel real queda condicionada a recibir un archivo de muestra y confirmar sus columnas. Si llega antes de cerrar el MVP, agregaremos importación de ese formato concreto.
- Recuperación de contraseña por correo fuera de este primer corte. Secretaría/Dirección podrá restablecer una cuenta de demo manualmente.
- No se crearán pruebas automatizadas ni tareas independientes de auditoría o endurecimiento. Sí habrá comprobación manual de los recorridos de demo, compilación y despliegue.
- No se usarán datos reales de alumnos ni familias para esta demo pública.

## 4. Stack y estructura

- `frontend/`: Angular compatible con Node 24, TypeScript, SCSS, PrimeIcons y tema visual propio. SPA sin renderizado en servidor.
- `backend/`: ASP.NET Core 10 Web API, Entity Framework Core, proveedor Npgsql/PostgreSQL y ASP.NET Core Identity con sesión por cookie. Un proyecto de API con carpetas por área funcional; sin microservicios.
- `Dockerfile` en la raíz: compila Angular y .NET en etapas separadas y copia la salida Angular a `wwwroot` para servir interfaz y API desde el mismo origen.
- `README.md`, `.gitignore`, `.dockerignore` y un ejemplo de variables sin secretos.
- Neon PostgreSQL Free: una base para la demo, migraciones versionadas de EF Core y datos ficticios iniciales insertados una sola vez.
- Render Web Service Free: un servicio Docker vinculado a `main`, URL `onrender.com`, HTTPS y despliegue automático después de cada push.

## 5. Tareas, en orden de ejecución

### A. Base del proyecto

1. Crear el proyecto Angular dentro de `frontend/`, con routing, formularios, SCSS y versiones fijadas en `package-lock.json`.
2. Instalar y configurar PrimeIcons y el tema SCSS propio; preparar diseño adaptable a escritorio y celular: navegación lateral, encabezado, tablas, formularios y estados vacíos. Se descartó PrimeNG 22 porque mostraba un aviso de licencia de PrimeUI en ejecución.
3. Crear la API .NET 10 dentro de `backend/`, configurar JSON, errores comunes, OpenAPI para desarrollo y un endpoint `/health`.
4. Configurar llamadas locales del frontend a `/api` con proxy de desarrollo; en Render ambas partes compartirán dominio.
5. Crear `.gitignore`, `.dockerignore` y documentación mínima de arranque. Compilar ambos proyectos antes de seguir.

### B. Neon y datos

6. Crear el proyecto gratuito `gestion-escolar-mvp-db` en Neon y elegir la región más cercana a la región de Render que esté disponible.
7. Configurar la conexión de desarrollo fuera de Git y la conexión de Render como variable de entorno. Usar conexión PostgreSQL con TLS. No copiar credenciales al repositorio.
8. Crear entidades y migración inicial para personas, usuarios/roles, vínculos familiares, ciclo, trimestre, cursos, materias, inscripciones, asignaciones, evaluaciones, calificaciones, asistencia, publicaciones e historial.
9. Cargar datos de demostración mediante una rutina repetible que no duplique registros. Crear usuarios de demo con contraseñas definidas fuera del código fuente.

### C. API y reglas del flujo

10. Implementar inicio/cierre de sesión y endpoint `me` para saber perfil y alcance del usuario.
11. Implementar operaciones de personas, cuentas (incluidos alta, baja lógica y restablecimiento manual), responsables, alumnos, cursos, materias, inscripciones y asignaciones.
12. Implementar evaluaciones y calificaciones con estados borrador/publicada, observación, corrección e historial.
13. Implementar asistencia diaria y resumen por alumno/curso.
14. Implementar consulta familiar filtrada por vínculo y publicación, promedio simple habilitable y vista de boletín.
15. Bloquear en la API las operaciones fuera del perfil, la materia asignada, el curso asignado o el vínculo familiar. Mantener validaciones simples de datos y mensajes de error legibles.

### D. Interfaz completa de demostración

16. Construir inicio de sesión y tablero por perfil, con navegación visible solo hacia sus tareas.
17. Construir administración de personas, vínculos, estructura académica, inscripciones y asignaciones.
18. Construir la pantalla docente: seleccionar curso/materia/trimestre, crear evaluación, editar grilla de notas, publicar, corregir y abrir historial.
19. Construir la pantalla de preceptor: fecha, curso, lista de alumnos, marcado rápido de asistencia y resumen.
20. Construir el portal de responsable: selector de hijo, notas y observaciones publicadas, asistencia, promedio y boletín condicionado por habilitación.
21. Construir controles de publicación para Secretaría/Dirección y vista imprimible del boletín.
22. Ajustar textos, estados vacíos, confirmaciones y diseño adaptable. La demo debe poder recorrerse sin explicar el diagrama técnico.

### E. Preparación y despliegue

23. Crear `Dockerfile` multi etapa y configurar ASP.NET Core para escuchar en `0.0.0.0` y el puerto `PORT` que proporciona Render (habitualmente `10000`).
24. Servir el build Angular desde ASP.NET Core, con fallback a `index.html` para rutas de la SPA y `/api` reservado para la API.
25. Ejecutar la migración pendiente y el sembrado idempotente al arrancar el único servicio de demo; el nivel Free de Render no tiene comando previo al despliegue.
26. Crear commits y subir `main` a GitHub. Conectar Render al repositorio, crear Web Service con runtime Docker y plan Free, y cargar la conexión de Neon y contraseñas iniciales como variables de entorno.
27. Revisar logs del build y arranque, corregir fallos y verificar que `/health`, la portada y los cuatro recorridos funcionen en la URL pública.
28. Documentar URL final, cuentas de demo, cómo se actualiza el despliegue y la recomendación de abrir la app unos minutos antes de una reunión por el arranque en frío.

## 6. Puntos de control de la entrega

- Secretaría puede preparar un alumno y sus relaciones sin intervenir en la base manualmente.
- Docente puede publicar y corregir una nota de su materia; el historial conserva ambos valores.
- Preceptor puede guardar asistencia de un curso en una fecha y verla en el resumen.
- Responsable ve solo su hijo y solo contenido publicado; el boletín aparece cuando Dirección lo habilita.
- La aplicación se abre desde una URL `onrender.com`, mantiene los datos tras un nuevo despliegue y puede volver a desplegarse con un push a `main`.

## 7. Intervención manual que podrías necesitar

| Momento | Intervención tuya | Lo demás lo hago yo |
| --- | --- | --- |
| GitHub | Si Git pide autenticación o segundo factor al subir el primer commit, completar esa autorización en tu cuenta. | Preparar archivos, commits, remote y push. |
| Neon/Render | Mantener la sesión iniciada y autorizar a Render a leer este repositorio de GitHub si aparece la pantalla de permisos. | Crear proyecto y servicio Free, elegir región, configurar variables y despliegue. |
| Reglas escolares | Si querés fidelidad al colegio, proporcionar el Excel de ejemplo y las reglas reales de promedio/boletín. | Con el alcance acordado, adaptar carga y cálculos. |

**Docker Desktop no es requisito.** El comando `docker` no está instalado en esta computadora, pero Render construye el contenedor desde el `Dockerfile` del repositorio. Para trabajar localmente usaremos .NET y Node ya instalados; el contenedor se verificará en el build y los logs de Render.

## 8. Límites conocidos de esta primera demo

- Render Free suspende el servicio tras 15 minutos sin tráfico y puede tardar alrededor de un minuto en responder a la primera visita. La base está en Neon porque el PostgreSQL gratuito de Render expira a los 30 días.
- La sesión podría requerir volver a iniciar sesión después de un reinicio o despliegue del servicio gratuito.
- Sin datos reales, formato oficial de boletín, integración de correo, importador Excel sin muestra, múltiples escuelas ni pruebas automatizadas.

Referencias de despliegue: [Render Free](https://render.com/docs/free), [Docker en Render](https://render.com/docs/docker), [puertos de Web Services](https://render.com/docs/web-services), [Neon Free](https://neon.com/blog/neon-free-plan-1-gb-per-project).
