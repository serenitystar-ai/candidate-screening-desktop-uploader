import { Badge } from "@repo/ui/components/ui/badge";
import { Card } from "@repo/ui/components/ui/card";

import type { Opening } from "../bridge";

interface OpeningsScreenProps {
  openings: Opening[];
  onSelect: (opening: Opening) => void;
}

export function OpeningsScreen({ openings, onSelect }: OpeningsScreenProps) {
  if (openings.length === 0) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-2 px-6 text-center">
        <h2 className="text-page-title">No hay ofertas</h2>
        <p className="text-body max-w-md">
          Crea la oferta en Candidate Screening Studio y vuelve aquí para procesar sus CV.
        </p>
      </div>
    );
  }

  return (
    <div className="minimal-scroll h-full overflow-y-auto px-6 py-6">
      {/* La ventana no baja de 960 px de ancho, así que las tres columnas caben siempre. */}
      <div className="grid grid-cols-3 gap-4">
        {openings.map((opening) => (
          <Card
            key={opening.id}
            role="button"
            tabIndex={0}
            onClick={() => onSelect(opening)}
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") onSelect(opening);
            }}
            className="hover:border-primary focus-visible:border-primary cursor-pointer gap-3 py-5 transition-colors outline-none"
          >
            <div className="flex items-start justify-between gap-3 px-5">
              <h3 className="text-card-heading">{opening.title}</h3>
              <StatusBadge status={opening.status} />
            </div>

            <div className="flex flex-col gap-1 px-5">
              <p className="text-caption">{describe(opening)}</p>
              <p className="text-body">{countCandidates(opening.candidateCount)}</p>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const open = status.toLowerCase() === "open";

  return <Badge variant={open ? "success" : "muted"}>{open ? "Abierta" : "Cerrada"}</Badge>;
}

function describe(opening: Opening): string {
  return [opening.department, opening.location, opening.employmentType].filter(Boolean).join(" · ");
}

function countCandidates(count: number): string {
  if (count === 0) return "Sin candidatos";

  return count === 1 ? "1 candidato" : `${count} candidatos`;
}
