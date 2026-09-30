import { CircleAlert, CircleCheck, ExternalLink } from "lucide-react";

import { Badge } from "@repo/ui/components/ui/badge";
import { Button } from "@repo/ui/components/ui/button";
import { Progress } from "@repo/ui/components/ui/progress";

import type { FileRow, FileStatus, RunState } from "../state/run-state";
import { failures, processed, settled } from "../state/run-state";

interface RunProgressProps {
  run: RunState;
  onStop: (() => void) | null;
}

export function RunProgress({ run, onStop }: RunProgressProps) {
  const resolved = settled(run);
  const done = processed(run);
  const failed = resolved - done;

  return (
    <div className="flex h-full flex-col overflow-hidden px-6 py-6">
      <div className="flex shrink-0 flex-col gap-3">
        <div className="flex items-baseline justify-between gap-4">
          <h2 className="text-page-title">
            {run.total === 0 ? "Preparando" : `Analizando ${run.total} CV`}
          </h2>
          {onStop ? (
            <Button variant="outline" onClick={onStop}>
              Detener
            </Button>
          ) : null}
        </div>

        <div className="flex items-center gap-4">
          <Progress value={run.total === 0 ? 0 : (resolved / run.total) * 100} className="flex-1" />
          <span className="text-caption shrink-0 tabular-nums">
            {resolved} de {run.total}
          </span>
        </div>

        <div className="flex h-5 items-baseline justify-between gap-4">
          <p className="text-caption truncate">{run.step ?? ""}</p>
          <p className="text-caption shrink-0">{tally(done, failed)}</p>
        </div>
      </div>

      <ul className="border-border minimal-scroll mt-5 flex-1 divide-y divide-[var(--border)] overflow-y-auto rounded-lg border">
        {run.files.map((file) => (
          <ActivityRow key={file.fileName} file={file} />
        ))}
      </ul>
    </div>
  );
}

function tally(done: number, failed: number): string {
  if (done === 0 && failed === 0) return "";

  const parts = [`${done} procesados`];

  if (failed > 0) parts.push(`${failed} fallidos`);

  return parts.join(" · ");
}

interface RunSummaryProps {
  run: RunState;
  stopped: boolean;
  onOpenResults: () => void;
  onFinish: () => void;
}

export function RunSummary({ run, stopped, onOpenResults, onFinish }: RunSummaryProps) {
  const totals = run.totals;
  const failed = failures(run);

  return (
    <div className="flex h-full flex-col overflow-hidden px-6 py-6">
      <div className="flex shrink-0 flex-col items-center gap-3 text-center">
        <span
          className={`flex size-12 items-center justify-center rounded-full ${
            stopped ? "bg-warning-bg text-warning-fg" : "bg-success-bg text-success-fg"
          }`}
        >
          {stopped ? <CircleAlert className="size-6" /> : <CircleCheck className="size-6" />}
        </span>

        <h2 className="text-page-title">
          {stopped ? "Análisis detenido" : headline(totals?.processed ?? 0)}
        </h2>
        <p className="text-body">
          {stopped ? stoppedNote(totals?.processed ?? 0) : breakdown(run)}
        </p>

        <div className="mt-2 flex gap-2">
          <Button onClick={onOpenResults}>
            Ver los candidatos
            <ExternalLink className="size-4" />
          </Button>
          <Button variant="outline" onClick={onFinish}>
            Volver a las ofertas
          </Button>
        </div>
      </div>

      {failed.length > 0 ? (
        <section className="mt-6 flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
          <h3 className="text-card-heading">Movidos a la carpeta de fallidos</h3>

          <ul className="border-border minimal-scroll divide-y divide-[var(--border)] overflow-y-auto rounded-lg border">
            {failed.map((file) => (
              <li key={file.fileName} className="flex flex-col gap-0.5 px-4 py-2.5">
                <span className="truncate text-sm">{file.fileName}</span>
                <span className="text-caption">{file.reason ?? "Sin detalle"}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}

interface ConfirmStopProps {
  onConfirm: () => void;
  onDismiss: () => void;
  confirmLabel: string;
}

/**
 * La confirmación de detener, en un solo paso y diciendo lo incómodo.
 */
export function ConfirmStop({ onConfirm, onDismiss, confirmLabel }: ConfirmStopProps) {
  return (
    <div className="bg-warning-bg text-warning-fg flex shrink-0 items-center gap-4 px-6 py-3">
      <p className="text-sm">
        Los CV ya procesados no se deshacen, pero los que van a medias no se archivan. Revisa la carpeta
        antes de volver a lanzarlo.
      </p>

      <div className="ml-auto flex shrink-0 gap-2">
        <Button variant="ghost" size="sm" onClick={onDismiss}>
          Seguir
        </Button>
        <Button variant="destructive" size="sm" onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </div>
  );
}

function ActivityRow({ file }: { file: FileRow }) {
  // Todo el grupo está en el turno, así que lo que distingue por dónde va el agente es el nombre, no la
  // etiqueta. Una vez marcado no se apaga: la banda resaltada dice qué quedó atrás.
  const highlighted = file.named === true || file.status === "procesado";

  return (
    <li className="flex items-center gap-3 px-4 py-2.5">
      <span className={`flex-1 truncate text-sm ${name(file.status, highlighted)}`} title={file.fileName}>
        {file.fileName}
      </span>

      <div className="flex shrink-0 items-center gap-3">
        {file.status === "fallido" && file.reason ? (
          <span className="text-caption max-w-60 truncate" title={file.reason}>
            {file.reason}
          </span>
        ) : null}
        <Badge variant={tone(file.status)}>{file.status}</Badge>
      </div>
    </li>
  );
}

function name(status: FileStatus, highlighted: boolean): string {
  if (highlighted) return "text-foreground font-semibold";

  return status === "en cola" ? "text-ink-300" : "text-ink-500";
}

function tone(status: FileStatus): "outline" | "muted" | "info" | "success" | "danger" {
  switch (status) {
    case "en cola":
      return "outline";
    case "subiendo":
    case "subido":
      return "muted";
    case "procesando":
      return "info";
    case "procesado":
      return "success";
    case "fallido":
      return "danger";
  }
}

function headline(processed: number): string {
  if (processed === 0) return "No se ha procesado ningún CV";

  return processed === 1 ? "1 CV procesado" : `${processed} CV procesados`;
}

function stoppedNote(processed: number): string {
  const hechos =
    processed === 0
      ? "No se procesó ningún CV"
      : processed === 1
        ? "Quedó 1 CV procesado"
        : `Quedaron ${processed} CV procesados`;

  return `${hechos}. Revisa la carpeta antes de volver a lanzarlo.`;
}

function breakdown(run: RunState): string {
  const totals = run.totals;

  if (!totals) return "";

  const parts = [`${totals.toReview} fallidos`];

  if (totals.rejected > 0) parts.push(`${totals.rejected} excluidos por formato o tamaño`);
  if (totals.excludedByUser > 0) parts.push(`${totals.excludedByUser} dejados fuera`);
  if (totals.failedMoves > 0) parts.push(`${totals.failedMoves} que no se han podido mover`);

  return parts.join(" · ");
}
