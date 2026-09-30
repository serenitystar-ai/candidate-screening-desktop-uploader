/** Cómo reacciona la corrida ante un error, tal como lo declara el motor. */
export type ErrorKind = "Transient" | "File" | "Fatal";

/** Error del motor que llegó por el puente, con su clase intacta. */
export class BridgeError extends Error {
  readonly kind: ErrorKind;

  /** Marca los fatales que tienen su propia pantalla. Null en el resto. */
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

/** Escucha los avisos que el motor empuja por su cuenta. Devuelve cómo dejar de escucharlos. */
export function on(event: "run", handler: (progress: RunEvent) => void): () => void;
export function on(event: "closeRequested", handler: () => void): () => void;
export function on(event: string, handler: (payload: never) => void): () => void {
  const forEvent = listeners.get(event) ?? new Set();

  forEvent.add(handler);
  listeners.set(event, forEvent);

  return () => forEvent.delete(handler);
}

/** Autentica contra el AI Hub y deja la sesión lista para operar sobre el agente. */
export function login(email: string, password: string): Promise<void> {
  return send<void>("login", { email, password });
}

/** Cierra la sesión y olvida lo que se cargó con ella. */
export function logout(): Promise<void> {
  return send<void>("logout");
}

/** Devuelve los ajustes vigentes. */
export function readSettings(): Promise<Settings> {
  return send<Settings>("readSettings");
}

/** Guarda los ajustes. El motor rechaza los valores que no admite. */
export function saveSettings(settings: Settings): Promise<void> {
  return send<void>("saveSettings", settings);
}

/** Devuelve las ofertas de la organización, de la más reciente a la más antigua. */
export function listOpenings(): Promise<Opening[]> {
  return send<Opening[]>("listOpenings");
}

/** Devuelve la carpeta que se usó la última vez para esta oferta, si sigue existiendo. */
export function rememberedFolder(openingId: string): Promise<string | null> {
  return send<string | null>("rememberedFolder", { openingId });
}

/** Abre el diálogo de carpeta del sistema. */
export function pickFolder(openingId: string): Promise<string | null> {
  return send<string | null>("pickFolder", { openingId });
}

/** Revisa la carpeta y devuelve qué entra y qué queda fuera. */
export function discover(folder: string): Promise<Discovery> {
  return send<Discovery>("discover", { folder });
}

/** Devuelve a la carpeta los archivos indicados de la subcarpeta de fallidos, y la vuelve a leer. */
export function restoreFailed(folder: string, fileNames: string[]): Promise<Discovery> {
  return send<Discovery>("restoreFailed", { folder, fileNames });
}

/** Lanza el análisis, sin los archivos que se dejaron fuera. El avance llega por el aviso «run». */
export function startRun(openingId: string, folder: string, excluded: string[]): Promise<void> {
  return send<void>("startRun", { openingId, folder, excluded });
}

/** Detiene el análisis en marcha. */
export function cancelRun(): Promise<void> {
  return send<void>("cancelRun");
}

/** Devuelve los análisis anteriores. */
export function listRuns(): Promise<PastRun[]> {
  return send<PastRun[]>("listRuns");
}

/** Abre Candidate Screening Studio en el navegador. */
export function openResults(): Promise<void> {
  return send<void>("openResults");
}

/** Cierra la ventana, ya confirmado que se puede. */
export function closeWindow(): Promise<void> {
  return send<void>("closeWindow");
}
