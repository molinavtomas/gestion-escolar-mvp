// =====================================================================================
// Tipos de los datos que devuelve la API. Reflejan los DTOs del backend (en camelCase,
// que es como ASP.NET serializa a JSON). Tenerlos tipados hace que el compilador avise
// si usamos un campo que no existe.
// =====================================================================================

export type Role = 'Administrador' | 'Director' | 'Secretaria' | 'Preceptor' | 'Profesor' | 'Familia';

/**
 * Matriz de permisos del rol (la calcula el backend en Auth/Permissions.cs).
 * Se usa solo para mostrar u ocultar menús y botones: la API vuelve a verificar todo.
 */
export interface Permissions {
  viewAll: boolean;        // ver todo lo académico (cursos, boletines, personas, panel)
  managePeople: boolean;   // altas, bajas y edición de personas
  manageAcademic: boolean; // cursos, materias, asignaciones, publicación, cierre
  editGrades: boolean;     // cargar notas (todas o solo las propias si es profesor)
  takeAttendance: boolean; // tomar asistencia (todos los cursos o los asignados)
  viewAttendance: boolean; // ver asistencia
  resetPasswords: boolean; // volver claves al DNI (según el tipo de persona)
  sendNotices: boolean;    // enviar comunicados
  admin: boolean;          // auditoría, configuración, años lectivos, exportación
  family: boolean;         // cuenta familiar
}

export interface SessionUser {
  displayName: string;
  role: Role;
  personId: number;
  dni: string;
  /** true mientras la clave siga siendo el DNI (se muestra un aviso para cambiarla). */
  usesDefaultPassword: boolean;
  permissions: Permissions;
}

/** Respuesta paginada genérica del backend. */
export interface Paged<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

/** Las 9 columnas de notas del boletín del colegio. */
export interface GradeValues {
  valuation1: string | null;
  grade1: number | null;
  intensification1: number | null;
  valuation2: string | null;
  grade2: number | null;
  intensification2: number | null;
  december: number | null;
  february: number | null;
  finalGrade: number | null;
}

export interface ReportCardRow {
  subjectId: number;
  subject: string;
  values: GradeValues;
  observation: string | null;
}

export interface ReportCard {
  studentId: number;
  student: string;
  dni: string;
  nivel: string;
  course: string;
  year: number;
  schoolYearId: number;
  isCurrentYear: boolean;
  schoolName: string;
  publishedStage: number;
  publishedStageLabel: string;
  isFamilyView: boolean;
  rows: ReportCardRow[];
  comment: string | null;
  attendance: { present: number; absent: number; late: number; justified: number };
  acknowledgedAtUtc: string | null;
  acknowledgedBy: string | null;
}

export interface Option { id: number; name: string; }

/** Curso tal como lo devuelve /api/courses/filters. */
export interface CourseItem {
  id: number;
  nivel: string;
  level: number;
  division: string;
  schoolYearId: number;
  label: string;
  canTakeAttendance: boolean;
}

export interface SchoolYearItem { id: number; year: number; isActive: boolean; }
export interface NivelItem { nivel: string; levels: number[]; }

/** Datos para los filtros de curso (años lectivos, niveles y cursos visibles). */
export interface FiltersData {
  years: SchoolYearItem[];
  niveles: NivelItem[];
  courses: CourseItem[];
}

export interface Person {
  id: number;
  fullName: string;
  dni: string;
  email: string | null;
  phone: string | null;
  type: string;
  isActive: boolean;
  usesDefaultPassword: boolean;
  courseId: number | null;
  course: string | null;
  siblings: string[];
  /** Acciones que el usuario actual puede hacer sobre esta fila (las calcula la API). */
  canEdit: boolean;
  canReset: boolean;
  canDelete: boolean;
}

/** Tipos de persona, en el orden en que se muestran. */
export const PERSON_TYPES = ['Alumno', 'Profesor', 'Preceptor', 'Secretaria', 'Director', 'Administrador'] as const;

/** Nombre "lindo" de cada rol para mostrar en pantalla. */
export const ROLE_LABELS: Record<Role, string> = {
  Administrador: 'Administración',
  Director: 'Dirección',
  Secretaria: 'Secretaría',
  Preceptor: 'Preceptoría',
  Profesor: 'Profesor/a',
  Familia: 'Familia'
};

/** Etiqueta del año dentro de un nivel ("1°" o "Sala 4" en Inicial). */
export function levelLabel(nivel: string, level: number): string {
  return nivel === 'Inicial' ? `Sala ${level}` : `${level}°`;
}
