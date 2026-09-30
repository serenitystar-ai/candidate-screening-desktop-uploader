import { ChevronRight } from "lucide-react";

import { Badge } from "@repo/ui/components/ui/badge";

import type { PastRun } from "../bridge";

const when = new Intl.DateTimeFormat("es-ES", {
  day: "2-digit",
  month: "short",
  hour: "2-digit",
  minute: "2-digit"
});

export function HistoryScreen({
  runs,
  onOpen
}: {
  runs: PastRun[];
  onOpen: (run: PastRun) => void;
}) {
  if (runs.length === 0) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-2 px-6 text-center">
        <h2 className="text-page-title">Todavía no hay análisis</h2>
        <p className="text-body max-w-md">Aquí quedará el resultado de cada uno cuando lances el primero.</p>
      </div>
    );
  }

  return (
    <div className="minimal-scroll h-full overflow-y-auto px-6 py-6">
      <ul className="border-border divide-y divide-[var(--border)] overflow-hidden rounded-lg border">
        {runs.map((run) => (
          <li key={run.id}>
            <button
              type="button"
              onClick={() => onOpen(run)}
              className="hover:bg-muted flex w-full cursor-pointer items-center gap-4 px-4 py-3 text-left transition-colors"
            >
              <span className="text-caption w-28 shrink-0">{when.format(new Date(run.startedAt))}</span>
              <span className="truncate text-sm">{run.openingTitle}</span>
              <span className="ml-auto flex shrink-0 items-center gap-3">
                <Outcome run={run} />
                <ChevronRight className="text-ink-300 size-4" />
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function HistoryDetail({ run }: { run: PastRun }) {
  return (
    <div className="flex h-full flex-col overflow-hidden px-6 py-6">
      <div className="flex shrink-0 flex-col gap-1">
        <h2 className="text-page-title">{run.openingTitle}</h2>
        <p className="text-caption">{when.format(new Date(run.startedAt))}</p>
        <p className="text-body mt-2">
          {run.completed
            ? `${run.processed} procesados · ${run.toReview} fallidos · ${run.rejected} excluidos`
            : "El análisis no llegó a terminar."}
        </p>
      </div>

      {run.failures.length > 0 ? (
        <section className="mt-6 flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
          <h3 className="text-card-heading">CV fallidos</h3>

          <ul className="border-border minimal-scroll divide-y divide-[var(--border)] overflow-y-auto rounded-lg border">
            {run.failures.map((failure) => (
              <li key={failure.fileName} className="flex flex-col gap-0.5 px-4 py-2.5">
                <span className="truncate text-sm">{failure.fileName}</span>
                <span className="text-caption">{failure.reason || "Sin detalle"}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : (
        <p className="text-body mt-6">Se procesaron todos los CV de la carpeta.</p>
      )}
    </div>
  );
}

function Outcome({ run }: { run: PastRun }) {
  if (!run.completed) return <Badge variant="warning">Sin terminar</Badge>;

  if (run.toReview === 0) return <Badge variant="success">{run.processed} procesados</Badge>;

  return (
    <Badge variant="muted">
      {run.processed} de {run.processed + run.toReview}
    </Badge>
  );
}
