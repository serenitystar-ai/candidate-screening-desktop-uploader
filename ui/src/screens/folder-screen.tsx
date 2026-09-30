import type { ComponentType, ReactNode } from "react";
import { Folder, FolderOpen, LoaderCircle, Undo2, X } from "lucide-react";

import { Button } from "@repo/ui/components/ui/button";

import type { Discovery } from "../bridge";

interface FolderScreenProps {
  folder: string | null;
  scanning: boolean;
  discovery: Discovery | null;
  excluded: ReadonlySet<string>;
  onChangeFolder: () => void;
  onExclude: (fileNames: string[]) => void;
  onInclude: (fileNames: string[]) => void;
  onRestore: (fileNames: string[]) => void;
  onContinue: () => void;
}

export function FolderScreen(props: FolderScreenProps) {
  const { folder, scanning, discovery, excluded, onChangeFolder, onContinue } = props;

  const included = discovery?.detected.filter((file) => !excluded.has(file.fileName)) ?? [];
  const ready = !scanning && included.length > 0;

  return (
    <div className="flex h-full flex-col">
      {folder ? (
        <div className="border-border flex shrink-0 items-center gap-3 border-b px-6 py-3">
          <Folder className="text-ink-300 size-4 shrink-0" />
          <span className="text-body truncate" title={folder}>
            {folder}
          </span>
          <Button variant="outline" size="sm" className="ml-auto shrink-0" onClick={onChangeFolder}>
            Cambiar carpeta
          </Button>
        </div>
      ) : null}

      <div className="flex flex-1 flex-col overflow-hidden px-6 py-6">
        <Body {...props} />
      </div>

      {ready ? (
        <div className="border-border flex shrink-0 justify-end border-t px-6 py-4">
          <Button onClick={onContinue}>Continuar</Button>
        </div>
      ) : null}
    </div>
  );
}

function Body({
  folder,
  scanning,
  discovery,
  excluded,
  onChangeFolder,
  onExclude,
  onInclude,
  onRestore
}: FolderScreenProps) {
  if (scanning) {
    return (
      <Centered>
        <LoaderCircle className="text-ink-300 size-6 animate-spin" />
        <p className="text-body">Revisando la carpeta</p>
      </Centered>
    );
  }

  if (!folder) {
    return (
      <Centered>
        <Emblem />
        <h2 className="text-page-title">Elige la carpeta con los CV</h2>
        <p className="text-body max-w-md">
          La carpeta donde están los currículums de esta oferta, sueltos y sin comprimir.
        </p>
        <Button className="mt-2" onClick={onChangeFolder}>
          Elegir carpeta
        </Button>
      </Centered>
    );
  }

  if (!discovery) return null;

  const failed = discovery.failed;

  if (discovery.detected.length === 0) {
    return (
      <Centered>
        <Emblem />
        <h2 className="text-page-title">Aquí no hay CV que procesar</h2>
        <p className="text-body max-w-md">
          {failed.length > 0
            ? "Los que fallaron siguen en su subcarpeta. Puedes devolverlos y volver a intentarlo."
            : "Puede que ya se hayan procesado y estén en procesados o en fallidos. Si no, elige otra carpeta."}
        </p>

        {/* Sin esta salida, una carpeta con todo procesado y algo fallido no tendría cómo reintentarlo. */}
        {failed.length > 0 ? (
          <Button className="mt-2" onClick={() => onRestore(names(failed))}>
            Devolver {failed.length} a la carpeta
          </Button>
        ) : null}
      </Centered>
    );
  }

  const included = discovery.detected.filter((file) => !excluded.has(file.fileName));
  const byHand = discovery.detected.filter((file) => excluded.has(file.fileName));

  return (
    <div className="grid flex-1 grid-cols-2 gap-6 overflow-hidden">
      <Column
        title="Incluidos"
        count={included.length}
        action={
          included.length > 0
            ? { label: "Excluir todos", onClick: () => onExclude(names(included)) }
            : undefined
        }
      >
        {included.map((file) => (
          <Row
            key={file.fileName}
            name={file.fileName}
            note={size(file.sizeBytes)}
            action={{ icon: X, label: "Dejar fuera", onClick: () => onExclude([file.fileName]) }}
          />
        ))}
      </Column>

      <div className={`grid min-h-0 gap-6 ${failed.length > 0 ? "grid-rows-2" : "grid-rows-1"}`}>
        <Column
          title="Excluidos"
          count={discovery.excluded.length + byHand.length}
          action={
            byHand.length > 0
              ? { label: "Incluir todos", onClick: () => onInclude(names(byHand)) }
              : undefined
          }
        >
          {byHand.map((file) => (
            <Row
              key={file.fileName}
              name={file.fileName}
              note="Exclusión manual"
              muted
              action={{
                icon: Undo2,
                label: "Volver a incluir",
                onClick: () => onInclude([file.fileName])
              }}
            />
          ))}

          {/* Lo que descartó el filtro no ofrece el gesto: no se vuelve procesable porque alguien insista. */}
          {discovery.excluded.map((file) => (
            <Row key={file.fileName} name={file.fileName} note={file.reason} muted />
          ))}
        </Column>

        {/* Sin fallidos el bloque no aporta nada: no hay nada que devolver y Excluidos se queda el sitio. */}
        {failed.length > 0 ? (
          <Column
            title="Fallidos"
            count={failed.length}
            action={{ label: "Incluir todos", onClick: () => onRestore(names(failed)) }}
          >
            {failed.map((file) => (
              <Row
                key={file.fileName}
                name={file.fileName}
                note={size(file.sizeBytes)}
                muted
                action={{
                  icon: Undo2,
                  label: "Devolver a la carpeta",
                  onClick: () => onRestore([file.fileName])
                }}
              />
            ))}
          </Column>
        ) : null}
      </div>
    </div>
  );
}

interface BulkAction {
  label: string;
  onClick: () => void;
}

function Column({
  title,
  count,
  action,
  children
}: {
  title: string;
  count: number;
  action?: BulkAction;
  children: ReactNode;
}) {
  return (
    <section className="flex min-h-0 flex-col gap-3">
      <div className="flex items-baseline gap-2">
        <h2 className="text-card-heading">{title}</h2>
        <span className="text-caption">{count}</span>

        {action ? (
          <Button variant="ghost" size="sm" className="ml-auto" onClick={action.onClick}>
            {action.label}
          </Button>
        ) : null}
      </div>

      {count === 0 ? (
        <p className="text-caption">Ninguno.</p>
      ) : (
        <ul className="border-border minimal-scroll flex-1 divide-y divide-[var(--border)] overflow-y-auto rounded-lg border">
          {children}
        </ul>
      )}
    </section>
  );
}

interface RowAction {
  icon: ComponentType<{ className?: string }>;
  label: string;
  onClick: () => void;
}

function Row({
  name,
  note,
  muted,
  action
}: {
  name: string;
  note: string;
  muted?: boolean;
  action?: RowAction;
}) {
  return (
    <li className="flex items-center gap-3 px-4 py-2.5">
      {action ? (
        <button
          type="button"
          onClick={action.onClick}
          title={action.label}
          aria-label={`${action.label}: ${name}`}
          className="text-ink-300 hover:text-foreground focus-visible:ring-ring/50 shrink-0 cursor-pointer rounded-sm transition-colors outline-none focus-visible:ring-[3px]"
        >
          <action.icon className="size-4" />
        </button>
      ) : (
        <span className="size-4 shrink-0" />
      )}

      <span
        className={`flex-1 truncate text-sm ${muted ? "text-ink-300" : "text-foreground"}`}
        title={name}
      >
        {name}
      </span>

      <span className="text-caption shrink-0">{note}</span>
    </li>
  );
}

function Emblem() {
  return (
    <span className="bg-muted text-ink-300 flex size-12 items-center justify-center rounded-full">
      <FolderOpen className="size-6" />
    </span>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-3 text-center">{children}</div>
  );
}

function names(files: { fileName: string }[]): string[] {
  return files.map((file) => file.fileName);
}

function size(bytes: number): string {
  const kb = bytes / 1024;

  return kb < 1024 ? `${Math.round(kb)} KB` : `${(kb / 1024).toFixed(1)} MB`;
}
