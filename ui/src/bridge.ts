/** How the run reacts to an error, as declared by the engine. */
export type ErrorKind = "Transient" | "File" | "Fatal";

/** Engine error that came through the bridge, with its kind intact. */
export class BridgeError extends Error {
  readonly kind: ErrorKind;

  /** Flags the fatal errors that have their own screen. Null for the rest. */
  readonly code: string | null;

  constructor(kind: ErrorKind, message: string, code: string | null = null) {
    super(message);
    this.name = "BridgeError";
    this.kind = kind;
    this.code = code;
  }
}

export interface Opening {
  id: string;
  title: string;
  department: string;
  location: string;
  employmentType: string;
  status: string;
  candidateCount: number;
}

export interface Detected {
  fileName: string;
  sizeBytes: number;
}

export interface Excluded {
  fileName: string;
  reason: string;
}

export interface Discovery {
  detected: Detected[];
  excluded: Excluded[];
  failed: Detected[];
}

export interface Failure {
  fileName: string;
  reason: string;
}

export interface Settings {
  appCode: string;
  maxFileSizeMb: number;
  responseLanguage: string;
  auditLogRetentionDays: number;
  batchSize: number;
  maxBatchMb: number;
}

export interface PastRun {
  id: string;
  startedAt: string;
  openingTitle: string;
  completed: boolean;
  processed: number;
  toReview: number;
  rejected: number;
  failures: Failure[];
}

export type RunEvent =
  | { type: "runStarted"; fileCount: number }
  | { type: "fileUploading"; fileName: string }
  | { type: "fileReading"; fileName: string }
  | { type: "fileReady"; fileName: string }
  | { type: "fileRegistered"; fileName: string }
  | { type: "fileFailed"; fileName: string; reason: string }
  | { type: "analyzing"; fileCount: number }
  | { type: "agentStep"; title: string; percent: number | null }
  | {
      type: "runCompleted";
      processed: number;
      toReview: number;
      rejected: number;
      excludedByUser: number;
      failedMoves: number;
    }
  | { type: "runFailed"; kind: ErrorKind; message: string }
  | { type: "runCancelled" };

interface Reply {
  id: number;
  result: unknown;
  error: { kind: ErrorKind; message: string; code: string | null } | null;
}

interface Pushed {
  event: string;
  payload: unknown;
}

interface Pending {
  resolve: (result: unknown) => void;
  reject: (error: BridgeError) => void;
}

const pending = new Map<number, Pending>();
const listeners = new Map<string, Set<(payload: never) => void>>();

let nextId = 1;

window.chrome?.webview?.addEventListener("message", ({ data }) => {
  if (data && typeof data === "object" && "event" in data) {
    const pushed = data as Pushed;

    for (const listener of listeners.get(pushed.event) ?? []) {
      (listener as (payload: unknown) => void)(pushed.payload);
    }

    return;
  }

  const reply = data as Reply;
  const waiting = pending.get(reply.id);

  if (!waiting) return;

  pending.delete(reply.id);

  if (reply.error) {
    waiting.reject(new BridgeError(reply.error.kind, reply.error.message, reply.error.code));
  }
  else waiting.resolve(reply.result);
});

function send<T>(type: string, payload?: unknown): Promise<T> {
  const host = window.chrome?.webview;

  if (!host) {
    return Promise.reject(
      new BridgeError("Fatal", "La interfaz no se está ejecutando dentro de la ventana de la aplicación.")
    );
  }

  const id = nextId++;

  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (result: unknown) => void, reject });
    host.postMessage({ id, type, payload });
  });
}

/** Listens to the notifications the engine pushes on its own. Returns how to stop listening. */
export function on(event: "run", handler: (progress: RunEvent) => void): () => void;
export function on(event: "closeRequested", handler: () => void): () => void;
export function on(event: string, handler: (payload: never) => void): () => void {
  const forEvent = listeners.get(event) ?? new Set();

  forEvent.add(handler);
  listeners.set(event, forEvent);

  return () => forEvent.delete(handler);
}

/** Authenticates against the AI Hub and leaves the session ready to operate on the agent. */
export function login(email: string, password: string): Promise<void> {
  return send<void>("login", { email, password });
}

/** Ends the session and forgets what was loaded with it. */
export function logout(): Promise<void> {
  return send<void>("logout");
}

/** Returns the current settings. */
export function readSettings(): Promise<Settings> {
  return send<Settings>("readSettings");
}

/** Saves the settings. The engine rejects values it doesn't accept. */
export function saveSettings(settings: Settings): Promise<void> {
  return send<void>("saveSettings", settings);
}

/** Returns the organization's job openings, newest to oldest. */
export function listOpenings(): Promise<Opening[]> {
  return send<Opening[]>("listOpenings");
}

/** Returns the folder last used for this job opening, if it still exists. */
export function rememberedFolder(openingId: string): Promise<string | null> {
  return send<string | null>("rememberedFolder", { openingId });
}

/** Opens the system folder dialog. */
export function pickFolder(openingId: string): Promise<string | null> {
  return send<string | null>("pickFolder", { openingId });
}

/** Scans the folder and returns what goes in and what is left out. */
export function discover(folder: string): Promise<Discovery> {
  return send<Discovery>("discover", { folder });
}

/** Moves the given files from the `fallidos/` subfolder back to the folder, and rescans it. */
export function restoreFailed(folder: string, fileNames: string[]): Promise<Discovery> {
  return send<Discovery>("restoreFailed", { folder, fileNames });
}

/** Starts the analysis without the files left out. Progress arrives through the «run» notice. */
export function startRun(openingId: string, folder: string, excluded: string[]): Promise<void> {
  return send<void>("startRun", { openingId, folder, excluded });
}

/** Stops the analysis in progress. */
export function cancelRun(): Promise<void> {
  return send<void>("cancelRun");
}

/** Returns the previous analyses. */
export function listRuns(): Promise<PastRun[]> {
  return send<PastRun[]>("listRuns");
}

/** Opens Candidate Screening Studio in the browser. */
export function openResults(): Promise<void> {
  return send<void>("openResults");
}

/** Closes the window, once it has been confirmed that it can. */
export function closeWindow(): Promise<void> {
  return send<void>("closeWindow");
}
