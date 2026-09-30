import type { Detected, ErrorKind, RunEvent } from "../bridge";

export type FileStatus = "en cola" | "subiendo" | "subido" | "procesando" | "procesado" | "fallido";

export interface FileRow {
  fileName: string;
  status: FileStatus;
  reason?: string;

  /** El agente lo nombró en alguno de sus pasos. No se apaga: marca por dónde ya pasó. */
  named?: boolean;
}

export interface Totals {
  processed: number;
  toReview: number;
  rejected: number;
  excludedByUser: number;
  failedMoves: number;
}

export interface RunState {
  total: number;
  step: string | null;
  files: FileRow[];
  totals: Totals | null;
  failure: { kind: ErrorKind; message: string } | null;
  cancelled: boolean;
}

export const initialRun: RunState = {
  total: 0,
  step: null,
  files: [],
  totals: null,
  failure: null,
  cancelled: false
};

/**
 * Arranca con la carpeta entera a la vista, en el orden en que se leyó. Sin esto las filas van
 * apareciendo según terminan las subidas, que corren en paralelo, y el orden sale al azar.
 */
export function seed(detected: Detected[]): RunState {
  return {
    ...initialRun,
    total: detected.length,
    files: detected.map((file) => ({ fileName: file.fileName, status: "en cola" }))
  };
}

/** Cuántos CV llegaron a un desenlace, que es lo que mide la barra. */
export function settled(state: RunState): number {
  return state.files.filter((file) => file.status === "procesado" || file.status === "fallido").length;
}

/** Cuántos quedaron con su fila escrita. */
export function processed(state: RunState): number {
  return state.files.filter((file) => file.status === "procesado").length;
}

/** Los CV que fallaron, que es la parte accionable del resumen. */
export function failures(state: RunState): FileRow[] {
  return state.files.filter((file) => file.status === "fallido");
}

export function reduce(state: RunState, event: RunEvent): RunState {
  switch (event.type) {
    case "runStarted":
      // Las filas ya están sembradas desde la previsualización de la carpeta.
      return { ...state, total: event.fileCount };

    case "fileUploading":
    case "fileReading":
      return track(state, event.fileName, "subiendo");

    case "fileReady":
      return track(state, event.fileName, "subido");

    case "fileRegistered":
      return track(state, event.fileName, "procesado");

    case "fileFailed":
      return track(state, event.fileName, "fallido", event.reason);

    case "analyzing":
      // El turno se lo lleva el grupo entero, así que todos los que subieron entran a la vez.
      return {
        ...state,
        step: "Leyendo los CV",
        files: state.files.map((file) =>
          file.status === "subido" ? { ...file, status: "procesando" } : file
        )
      };

    case "agentStep":
      return { ...naming(state, event.title), step: event.title };

    case "runCompleted":
      return { ...state, step: null, totals: event };

    case "runFailed":
      return { ...state, step: null, failure: { kind: event.kind, message: event.message } };

    case "runCancelled":
      return { ...state, step: null, cancelled: true };
  }
}

/**
 * Marca el archivo que el paso del agente nombra. Se busca por nombre y no interpretando la frase, así
 * que si el agente cambia su redacción se pierde la marca y nada más.
 */
function naming(state: RunState, step: string): RunState {
  const index = state.files.findIndex(
    (file) => file.status === "procesando" && !file.named && step.includes(file.fileName)
  );

  if (index < 0) return state;

  const files = [...state.files];

  files[index] = { ...files[index]!, named: true };

  return { ...state, files };
}

function track(state: RunState, fileName: string, status: FileStatus, reason?: string): RunState {
  const index = state.files.findIndex((file) => file.fileName === fileName);
  const row: FileRow = { fileName, status, reason };

  if (index < 0) return { ...state, files: [...state.files, row] };

  const files = [...state.files];

  // La marca del agente sobrevive al cambio de estado: dice por dónde pasó, no dónde está.
  row.named = files[index]!.named;

  files[index] = row;

  return { ...state, files };
}
