import { type FormEvent, type ReactNode, useState } from "react";
import { ChevronRight } from "lucide-react";

import { Alert } from "@repo/ui/components/ui/alert";
import { Button } from "@repo/ui/components/ui/button";
import { Input } from "@repo/ui/components/ui/input";
import { Label } from "@repo/ui/components/ui/label";

import type { Settings } from "../bridge";

const languages = ["Spanish", "English", "Portuguese", "French", "German", "Italian"];

interface SettingsScreenProps {
  settings: Settings;
  error: string | null;
  busy: boolean;
  onSave: (settings: Settings) => void;
}

export function SettingsScreen({ settings, error, busy, onSave }: SettingsScreenProps) {
  const [draft, setDraft] = useState(settings);

  const changed = Object.keys(settings).some(
    (key) => draft[key as keyof Settings] !== settings[key as keyof Settings]
  );

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    onSave(draft);
  }

  function set<K extends keyof Settings>(key: K, value: Settings[K]) {
    setDraft((previous) => ({ ...previous, [key]: value }));
  }

  return (
    <form className="flex h-full flex-col" onSubmit={handleSubmit}>
      <div className="minimal-scroll flex flex-1 flex-col gap-6 overflow-y-auto px-6 py-6">
        <Row columns={2}>
          <Field
            id="appCode"
            label="Código de la app"
            hint="Al entrar se busca el agente del AI Hub que sirve esta app, y se trabaja contra él."
          >
            <Input
              id="appCode"
              value={draft.appCode}
              onChange={(event) => set("appCode", event.target.value)}
              required
            />
          </Field>
        </Row>

        <Row columns={2}>
          <Field
            id="maxFileSizeMb"
            label="Tamaño máximo por archivo"
            hint="Los CV que lo superen quedan excluidos antes de enviar nada. Por encima de 25 MB el AI Hub los rechaza de todas formas."
          >
            <Measure unit="MB">
              <Input
                id="maxFileSizeMb"
                type="number"
                min={1}
                max={25}
                className="w-24"
                value={draft.maxFileSizeMb}
                onChange={(event) => set("maxFileSizeMb", Number(event.target.value))}
                required
              />
            </Measure>
          </Field>

          <Field
            id="responseLanguage"
            label="Idioma de las observaciones"
            hint="El idioma en el que el agente escribe la valoración de cada candidato."
          >
            <select
              id="responseLanguage"
              value={draft.responseLanguage}
              onChange={(event) => set("responseLanguage", event.target.value)}
              className="border-input focus-visible:border-ring focus-visible:ring-ring/50 h-9 w-48 rounded-md border bg-transparent px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px]"
            >
              {/* A saved value that isn't in the list is still valid for the agent. */}
              {(languages.includes(draft.responseLanguage)
                ? languages
                : [draft.responseLanguage, ...languages]
              ).map((language) => (
                <option key={language} value={language}>
                  {language}
                </option>
              ))}
            </select>
          </Field>
        </Row>

        <Advanced>
          <Row columns={3}>
            <Field
              id="auditLogRetentionDays"
              label="Historial"
              hint="Al lanzar un análisis se borran los registros más antiguos que este plazo."
            >
              <Measure unit="días">
                <Input
                  id="auditLogRetentionDays"
                  type="number"
                  min={30}
                  max={730}
                  className="w-24"
                  value={draft.auditLogRetentionDays}
                  onChange={(event) => set("auditLogRetentionDays", Number(event.target.value))}
                  required
                />
              </Measure>
            </Field>

            <Field id="batchSize" label="CV por lote" hint="Cantidad de CV por ejecución del agente.">
              <Measure unit="CV">
                <Input
                  id="batchSize"
                  type="number"
                  min={1}
                  max={20}
                  className="w-24"
                  value={draft.batchSize}
                  onChange={(event) => set("batchSize", Number(event.target.value))}
                  required
                />
              </Measure>
            </Field>

            <Field
              id="maxBatchMb"
              label="Peso máximo por lote"
              hint="El lote se cierra al alcanzar este peso, aunque no llegue al número de CV."
            >
              <Measure unit="MB">
                <Input
                  id="maxBatchMb"
                  type="number"
                  min={1}
                  max={50}
                  className="w-24"
                  value={draft.maxBatchMb}
                  onChange={(event) => set("maxBatchMb", Number(event.target.value))}
                  required
                />
              </Measure>
            </Field>
          </Row>
        </Advanced>

        {error ? <Alert variant="destructive">{error}</Alert> : null}
      </div>

      <div className="border-border flex shrink-0 justify-end border-t px-6 py-4">
        <Button type="submit" disabled={busy || !changed}>
          {busy ? "Guardando…" : "Guardar"}
        </Button>
      </div>
    </form>
  );
}

const columnsFor = { 1: "grid-cols-1", 2: "grid-cols-2", 3: "grid-cols-3" } as const;

/**
 * A row of fields. The subgrid aligns labels, descriptions and controls across columns even when one
 * description takes two lines and its neighbor one.
 */
function Row({ columns, children }: { columns: 1 | 2 | 3; children: ReactNode }) {
  return (
    <div className={`grid ${columnsFor[columns]} grid-rows-[auto_1fr_auto] gap-x-6 gap-y-2`}>
      {children}
    </div>
  );
}

function Field({
  id,
  label,
  hint,
  children
}: {
  id: string;
  label: string;
  hint: string;
  children: ReactNode;
}) {
  return (
    <div className="row-span-3 grid grid-rows-subgrid gap-2">
      <Label htmlFor={id}>{label}</Label>
      <p className="text-caption">{hint}</p>
      <div>{children}</div>
    </div>
  );
}

function Measure({ unit, children }: { unit: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      {children}
      <span className="text-body">{unit}</span>
    </div>
  );
}

/**
 * The engine's knobs, collapsed. They exist to avoid recompiling for a setting, not to be touched
 * daily.
 */
function Advanced({ children }: { children: ReactNode }) {
  return (
    <details className="border-border group rounded-lg border">
      <summary className="text-card-heading flex cursor-pointer list-none items-center gap-2 px-4 py-3">
        <ChevronRight className="size-4 transition-transform group-open:rotate-90" />
        Avanzadas
      </summary>

      <div className="border-border border-t px-4 py-4">{children}</div>
    </details>
  );
}
