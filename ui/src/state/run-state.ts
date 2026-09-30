import type { Detected, ErrorKind, RunEvent } from "../bridge";

export type FileStatus = "en cola" | "subiendo" | "subido" | "procesando" | "procesado" | "fallido";

export interface FileRow {
  fileName: string;
  status: FileStatus;
  reason?: string;

  /** The agent named it in one of its steps. It never clears: it marks where the agent has been. */
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
 * Starts with the whole folder in view, in the order it was read. Without this, rows appear as
 * uploads finish, and since those run in parallel the order comes out random.
 */
export function seed(detected: Detected[]): RunState {
  return {
    ...initialRun,
    total: detected.length,
    files: detected.map((file) => ({ fileName: file.fileName, status: "en cola" }))
  };
}

/** How many CVs reached an outcome, which is what the progress bar measures. */
export function settled(state: RunState): number {
  return state.files.filter((file) => file.status === "procesado" || file.status === "fallido").length;
}

/** How many ended up with their row written. */
export function processed(state: RunState): number {
  return state.files.filter((file) => file.status === "procesado").length;
}

/** The CVs that failed, which is the actionable part of the summary. */
export function failures(state: RunState): FileRow[] {
  return state.files.filter((file) => file.status === "fallido");
}

export function reduce(state: RunState, event: RunEvent): RunState {
  switch (event.type) {
    case "runStarted":
      // The rows are already seeded from the folder preview.
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
      // The whole group takes the turn, so everything that was uploaded goes in at once.
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
 * Marks the file the agent's step names. It matches by name rather than by interpreting the sentence,
 * so if the agent changes its wording only the mark is lost.
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

  // The agent's mark survives the status change: it says where the agent has been, not where it is.
  row.named = files[index]!.named;

  files[index] = row;

  return { ...state, files };
}
