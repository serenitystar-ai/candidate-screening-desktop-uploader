import { useEffect } from "react";
import { X } from "lucide-react";

import { Button } from "@repo/ui/components/ui/button";

const tree = `D:\\RRHH\\SharePoint Specialist\\
├── 12_Ingrid_Nystrom.pdf        pendiente
├── 13_Andres_Ocampo.pdf         pendiente
├── procesados\\
│   ├── 00_Mariano_Esquivel.pdf  procesado
│   └── 02_Priya_Ramanathan.pdf  procesado
└── fallidos\\
    └── 01_Lucia_Fernandez.pdf   no se pudo procesar`;

export function HelpPanel({ maxFileSizeMb, onClose }: { maxFileSizeMb: number; onClose: () => void }) {
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") onClose();
    }

    window.addEventListener("keydown", onKey);

    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <button
        type="button"
        aria-label="Cerrar la ayuda"
        className="absolute inset-0 cursor-default bg-black/40"
        onClick={onClose}
      />

      <aside className="bg-card border-border relative flex h-full w-[30rem] max-w-full flex-col border-l shadow-lg">
        <div className="border-border flex h-14 shrink-0 items-center justify-between border-b px-6">
          <h2 className="text-card-heading">Cómo funciona</h2>
          <Button variant="ghost" size="icon" onClick={onClose} aria-label="Cerrar">
            <X className="size-4" />
          </Button>
        </div>

        <div className="minimal-scroll flex flex-1 flex-col gap-6 overflow-y-auto px-6 py-6">
          <section className="flex flex-col gap-2">
            <h3 className="text-card-heading">Qué hace</h3>
            <p className="text-body">
              Envía los CV de una carpeta al agente de screening, uno detrás de otro, y deja cada
              candidato registrado en Candidate Screening Studio con su puntuación y sus observaciones.
            </p>
          </section>

          <section className="flex flex-col gap-2">
            <h3 className="text-card-heading">Qué pasa con los archivos</h3>
            <p className="text-body">
              La carpeta manda: lo que está suelto en ella está pendiente. Según cómo termine cada CV se
              mueve a una de dos subcarpetas, así que relanzar el análisis no repite lo ya hecho.
            </p>
            <pre className="bg-muted text-ink-500 minimal-scroll shrink-0 overflow-x-auto rounded-md p-4 text-xs leading-relaxed">
              {tree}
            </pre>
            <p className="text-caption">
              Los de <code>fallidos</code> se revisan a mano. Si el problema tiene arreglo, se devuelven a
              la carpeta y se vuelve a lanzar.
            </p>
          </section>

          <section className="flex flex-col gap-2">
            <h3 className="text-card-heading">Qué formatos admite</h3>
            <p className="text-body">
              PDF, Word, texto plano e imágenes de un CV escaneado, hasta {maxFileSizeMb} MB por archivo.
              Lo que no encaje aparece en la lista de excluidos antes de empezar, con el motivo.
            </p>
          </section>

          <section className="flex flex-col gap-2">
            <h3 className="text-card-heading">Dónde se ven los resultados</h3>
            <p className="text-body">
              En Candidate Screening Studio, dentro de la oferta. Esta aplicación registra a los
              candidatos; la puntuación y la comparación entre ellos se miran allí.
            </p>
          </section>
        </div>
      </aside>
    </div>
  );
}
