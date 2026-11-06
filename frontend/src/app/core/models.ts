// =====================================================================================
// Tipos de los datos que devuelve la API. Reflejan los DTOs del backend (en camelCase,
// que es como ASP.NET serializa a JSON). Tenerlos tipados hace que el compilador avise
// si usamos un campo que no existe.
// =====================================================================================

export type Role = 'Director' | 'Secretaria' | 'Preceptor' | 'Profesor' | 'Familia';

export interface SessionUser {
  displayName: string;
  role: Role;
  personId: number;
  dni: string;
  /** true mientras la clave siga siendo el DNI (se muestra un aviso para cambiarla). */
  usesDefaultPassword: boolean;
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
}

export interface ReportCard {
  studentId: number;
  student: string;
  dni: string;
  course: string;
  year: number;
  publishedStage: number;
  publishedStageLabel: string;
  isFamilyView: boolean;
  rows: ReportCardRow[];
  attendance: { present: number; absent: number; late: number; justified: number };
}

export interface Option { id: number; name: string; }
export interface CourseOption { id: number; label: string; }

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
}

/** Tipos de persona, en el orden en que se muestran. */
export const PERSON_TYPES = ['Alumno', 'Profesor', 'Preceptor', 'Secretaria', 'Director'] as const;

/** Nombre "lindo" de cada rol para mostrar en pantalla. */
export const ROLE_LABELS: Record<Role, string> = {
  Director: 'Dirección',
  Secretaria: 'Secretaría',
  Preceptor: 'Preceptoría',
  Profesor: 'Profesor/a',
  Familia: 'Familia'
};
