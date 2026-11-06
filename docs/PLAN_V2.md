# Plan versión 2 — Matriz de permisos y casos de uso por rol

Fecha: 6/11/2026. Estado: **implementado** (ver `docs/Casos de uso y pruebas.xlsx`, columna "¿Está en nuestro sistema?").

## 0. Matriz de permisos

Un solo lugar define qué puede hacer cada rol: `backend/GestionEscolar.Api/Auth/Permissions.cs`.
La API la aplica en cada request y la envía al frontend en `/api/auth/me`; el frontend la usa solo para
armar el menú y mostrar u ocultar botones.

| Acción | Administrador | Director | Secretaría | Preceptor | Profesor | Familia |
|---|---|---|---|---|---|---|
| Ver notas, boletines, personas, panel | ✔ | ✔ | ✔ | ✔ | solo sus materias | solo sus hijos (publicado) |
| Cargar / corregir notas | ✔ | ✔ | — | — | sus materias | — |
| Publicar boletín y cerrar períodos | ✔ | ✔ | — | — | — | — |
| Cursos, materias y asignaciones | ✔ | ✔ | ver | ver | — | — |
| Alta/baja de alumnos y profesores | ✔ | ✔ | ✔ (SEC-05 pendiente) | — | — | — |
| Alta/baja de Directores y Administradores | ✔ | — | — | — | — | — |
| Tomar asistencia | ✔ | ✔ | ver | sus cursos | ver (sus cursos) | ver (sus hijos) |
| Volver claves al DNI | todas | salvo Admin | salvo Dir./Admin | solo familias | — | — |
| Comunicados | ✔ | ✔ | — | — | — | los reciben |
| Auditoría, configuración, años lectivos, exportación | ✔ | — | — | — | — | — |

## 1. Administrador (dueñas)
Rol nuevo; auditoría (ingresos, cambios administrativos, notas); configuración (nombre, logo, límite de faltas);
nuevo año lectivo y promoción; exportar todo (CSV en ZIP); borrar personas sin datos; cambiar rol; estadísticas.
Restauración: la provee Neon (Backup & Restore). **Fuera de alcance:** datos económicos (ADM-12).

## 2. Director
Todo lo académico + cierre de carga por cuatrimestre, observaciones generales en el boletín, comunicados,
vinculación de hermanos. No ve auditoría ni configuración.

## 3. Secretaría y Preceptor
Ven todo. Secretaría sigue dando altas/bajas (**SEC-05 queda pendiente**). No cargan notas ni publican.
Preceptor toma asistencia de sus cursos. Alerta de faltas, búsqueda con filtros, exportar lista, restablecer claves.

## 4. Profesor
Solo sus materias; observación por alumno; períodos cerrados bloqueados; asistencia en lectura; grilla con columna fija.

## 5. Familia
Hermanos con un solo ingreso; asistencia día por día; avisos dentro del sistema (publicación, inasistencias,
comunicados); confirmación de lectura. **Fuera de alcance por ahora:** recuperar clave por email (FAM-12).

## 6. Pantalla de boletines
Filtros (ciclo, nivel, año, curso, búsqueda) → tabla paginada de alumnos con "Ver boletín" → boletín debajo.
Niveles Inicial / Primaria / Secundaria con materias por nivel y año.

## Decisiones técnicas (para estudiar)
- **Avisos dentro del sistema** en lugar de email/WhatsApp: no requiere contratar servicios. Si se agregan,
  basta con enviar el mensaje en el mismo lugar donde hoy se crea la `Notification`.
- **Hermanos** = `Person.FamilyGroupId`: un número compartido. Simple y suficiente; no hace falta una tabla aparte.
- **CSV en vez de .xlsx**: Excel lo abre directo y no agrega dependencias al proyecto.
- **Auditoría** en una sola tabla (`AuditEntry`) con dos tipos: Acceso y Cambio.
- **Años lectivos**: cada inscripción pertenece a un curso de un año; los años cerrados quedan en solo lectura.
