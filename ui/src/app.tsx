import { useCallback, useEffect, useState } from "react";
import { CircleQuestionMark, LoaderCircle, LogOut, RefreshCw, RotateCcwClock, Settings } from "lucide-react";

import type { Discovery, Opening, PastRun, Settings as AppSettings } from "./bridge";
import { BridgeError } from "./bridge";
import * as bridge from "./bridge";
import { AppHeader, type Crumb } from "./components/app-header";
import { ErrorState } from "./components/error-state";
import { HelpPanel } from "./components/help-panel";
import { IconButton } from "./components/icon-button";
import { FolderScreen } from "./screens/folder-screen";
import { HistoryDetail, HistoryScreen } from "./screens/history-screen";
import { LoginScreen } from "./screens/login-screen";
import { OpeningsScreen } from "./screens/openings-screen";
import { SettingsScreen } from "./screens/settings-screen";
import { ConfirmStop, RunProgress, RunSummary } from "./screens/run-screen";
import { initialRun, processed, type RunState, seed, settled } from "./state/run-state";
import { reduce } from "./state/run-state";

type Screen = "login" | "openings" | "folder" | "run" | "history" | "historyDetail" | "settings";

/** Lo que dice la ayuda mientras nadie ha abierto los ajustes; coincide con el default del motor. */
const DefaultFileSizeMb = 20;

interface Exit {
  label: string;
  leave: () => void;
}

export function App() {
  const [screen, setScreen] = useState<Screen>("login");
  const [loginError, setLoginError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [openings, setOpenings] = useState<Opening[]>([]);
  const [opening, setOpening] = useState<Opening | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [fatal, setFatal] = useState<{ message: string; code: string | null } | null>(null);

  const [folder, setFolder] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [discovery, setDiscovery] = useState<Discovery | null>(null);
  const [excluded, setExcluded] = useState<ReadonlySet<string>>(new Set());

  const [run, setRun] = useState<RunState>(initialRun);
  const [analyzing, setAnalyzing] = useState(false);
  const [exit, setExit] = useState<Exit | null>(null);

  const [runs, setRuns] = useState<PastRun[]>([]);
  const [pastRun, setPastRun] = useState<PastRun | null>(null);

  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [settingsError, setSettingsError] = useState<string | null>(null);

  const [helpOpen, setHelpOpen] = useState(false);

  useEffect(() => {
    const stopRun = bridge.on("run", (progress) => {
      setRun((previous) => reduce(previous, progress));

      if (progress.type === "runCompleted" || progress.type === "runFailed") setAnalyzing(false);
      if (progress.type === "runCancelled") setAnalyzing(false);
    });

    const stopClose = bridge.on("closeRequested", () => {
      setExit({ label: "Salir", leave: () => void bridge.closeWindow() });
    });

    return () => {
      stopRun();
      stopClose();
    };
  }, []);

  const goToOpenings = useCallback(async () => {
    setBusy(true);
    setFatal(null);

    try {
      setOpenings(await bridge.listOpenings());
    } catch (failure) {
      setFatal(describe(failure));
    } finally {
      // Se sale del acceso incluso si falló, porque el patrón de error se dibuja bajo la cabecera y ahí
      // están las dos salidas: los ajustes y cerrar sesión.
      setScreen("openings");
      setBusy(false);
    }
  }, []);

  /** Relee las ofertas sin vaciar la pantalla: el conteo de candidatos cambia por fuera de esta ventana. */
  async function refreshOpenings() {
    setRefreshing(true);

    try {
      setOpenings(await bridge.listOpenings());
    } catch (failure) {
      setFatal(describe(failure));
    } finally {
      setRefreshing(false);
    }
  }

  async function logout() {
    try {
      await bridge.logout();
    } catch {
      // Los tokens ya no sirven de todas formas: la pantalla vuelve al acceso igual.
    }

    setOpenings([]);
    setOpening(null);
    setFolder(null);
    setDiscovery(null);
    setRun(initialRun);
    setRuns([]);
    setPastRun(null);
    setExcluded(new Set());
    setFatal(null);
    setLoginError(null);
    setBusy(false);
    setScreen("login");
  }

  async function handleLogin(email: string, password: string) {
    setBusy(true);
    setLoginError(null);

    try {
      await bridge.login(email, password);
      await goToOpenings();
    } catch (failure) {
      const failed = describe(failure);

      // Que ningún agente sirva la app no es un problema de credenciales: tiene su propia pantalla, y la
      // salida está en los ajustes.
      if (failed.code === "agentNotFound") {
        setFatal(failed);
        setScreen("openings");
      } else {
        setLoginError(failed.message);
      }

      setBusy(false);
    }
  }

  async function scan(target: string) {
    setFolder(target);
    setDiscovery(null);
    setExcluded(new Set());
    setScanning(true);

    try {
      setDiscovery(await bridge.discover(target));
    } catch (failure) {
      setFolder(null);
      setFatal(describe(failure));
    } finally {
      setScanning(false);
    }
  }

  async function selectOpening(chosen: Opening) {
    setOpening(chosen);
    setFolder(null);
    setDiscovery(null);
    setScreen("folder");

    const remembered = await bridge.rememberedFolder(chosen.id);

    if (remembered) await scan(remembered);
  }

  async function changeFolder() {
    if (!opening) return;

    const chosen = await bridge.pickFolder(opening.id);

    if (chosen) await scan(chosen);
  }

  async function startRun() {
    if (!opening || !folder || !discovery) return;

    // Se siembra sólo con lo que va a correr: los dejados fuera no tienen por qué aparecer «en cola».
    setRun(seed(discovery.detected.filter((file) => !excluded.has(file.fileName))));
    setAnalyzing(true);
    setScreen("run");

    try {
      await bridge.startRun(opening.id, folder, [...excluded]);
    } catch (failure) {
      setAnalyzing(false);
      setFatal(describe(failure));
    }
  }

  /** Devolver un fallido lo mueve en disco, así que la carpeta se relee y manda lo que diga. */
  async function restoreFailed(fileNames: string[]) {
    if (!folder) return;

    setScanning(true);

    try {
      setDiscovery(await bridge.restoreFailed(folder, fileNames));
    } catch (failure) {
      setFatal(describe(failure));
    } finally {
      setScanning(false);
    }
  }

  async function openSettings() {
    setBusy(true);
    setSettingsError(null);

    try {
      setSettings(await bridge.readSettings());
      setScreen("settings");
    } catch (failure) {
      setFatal(describe(failure));
    } finally {
      setBusy(false);
    }
  }

  async function saveSettings(next: AppSettings) {
    setBusy(true);
    setSettingsError(null);

    try {
      await bridge.saveSettings(next);
      setSettings(next);
      await goToOpenings();
    } catch (failure) {
      setSettingsError(describe(failure).message);
    } finally {
      setBusy(false);
    }
  }

  async function openHistory() {
    setBusy(true);

    try {
      setRuns(await bridge.listRuns());
      setScreen("history");
    } catch (failure) {
      setFatal(describe(failure));
    } finally {
      setBusy(false);
    }
  }

  /** Nada que salga de un análisis en marcha lo hace sin pasar por la confirmación. */
  function leave(label: string, leaveNow: () => void) {
    if (analyzing) setExit({ label, leave: leaveNow });
    else leaveNow();
  }

  function backToOpenings() {
    setOpening(null);
    setFolder(null);
    setDiscovery(null);
    setRun(initialRun);
    setScreen("openings");
  }

  function confirmExit() {
    const leaving = exit;

    setExit(null);
    void bridge.cancelRun();
    leaving?.leave();
  }

  if (screen === "login") {
    return (
      <>
        <LoginScreen
          onSubmit={handleLogin}
          error={loginError}
          busy={busy}
          onHelp={() => setHelpOpen(true)}
        />
        {helpOpen ? <HelpPanel maxFileSizeMb={sizeLimit()} onClose={() => setHelpOpen(false)} /> : null}
      </>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <AppHeader crumbs={crumbs()} onHome={() => leave("Salir del análisis", backToOpenings)}>
        {screen === "openings" && !fatal ? (
          <IconButton
            icon={RefreshCw}
            label="Actualizar"
            onClick={() => void refreshOpenings()}
            spinning={refreshing}
          />
        ) : null}

        {/* Durante el análisis se oculta: llegar al historial pasaría por la confirmación, y cambiar de
            pantalla no vale perder la corrida. */}
        {screen !== "history" && screen !== "historyDetail" && !analyzing ? (
          <IconButton icon={RotateCcwClock} label="Historial" onClick={() => void openHistory()} />
        ) : null}

        {screen !== "settings" && !analyzing ? (
          <IconButton icon={Settings} label="Ajustes" onClick={() => void openSettings()} />
        ) : null}

        <IconButton
          icon={CircleQuestionMark}
          label="Cómo funciona"
          onClick={() => setHelpOpen(true)}
        />

        <IconButton
          icon={LogOut}
          label="Cerrar sesión"
          onClick={() => leave("Cerrar sesión", () => void logout())}
        />
      </AppHeader>

      {exit ? (
        <ConfirmStop
          confirmLabel={exit.label}
          onConfirm={confirmExit}
          onDismiss={() => setExit(null)}
        />
      ) : null}

      <main className="min-h-0 flex-1">{body()}</main>

      {helpOpen ? <HelpPanel maxFileSizeMb={sizeLimit()} onClose={() => setHelpOpen(false)} /> : null}
    </div>
  );

  function crumbs(): Crumb[] {
    switch (screen) {
      case "folder":
      case "run":
        return opening ? [{ label: opening.title }] : [];
      case "history":
        return [{ label: "Historial" }];
      case "settings":
        return [{ label: "Ajustes" }];
      case "historyDetail":
        return [
          { label: "Historial", onClick: () => setScreen("history") },
          { label: "Detalle" }
        ];
      default:
        return [];
    }
  }

  function body() {
    if (fatal?.code === "agentNotFound") {
      return (
        <ErrorState
          title="No se ha encontrado el agente"
          detail={`${fatal.message} Comprueba el código de la app en los ajustes, o pídeselo a quien administre el AI Hub.`}
          actionLabel="Ir a los ajustes"
          onAction={() => {
            setFatal(null);
            void openSettings();
          }}
        />
      );
    }

    if (fatal) {
      return (
        <ErrorState
          title="No se ha podido continuar"
          detail={fatal.message}
          actionLabel="Volver a las ofertas"
          onAction={() => {
            setFatal(null);
            void goToOpenings();
          }}
        />
      );
    }

    switch (screen) {
      case "openings":
        return busy ? <Loading /> : <OpeningsScreen openings={openings} onSelect={selectOpening} />;

      case "folder":
        return (
          <FolderScreen
            folder={folder}
            scanning={scanning}
            discovery={discovery}
            excluded={excluded}
            onChangeFolder={() => void changeFolder()}
            onExclude={(fileNames) => setExcluded((previous) => added(previous, fileNames))}
            onInclude={(fileNames) => setExcluded((previous) => removed(previous, fileNames))}
            onRestore={(fileNames) => void restoreFailed(fileNames)}
            onContinue={() => void startRun()}
          />
        );

      case "run":
        return runBody();

      case "history":
        return (
          <HistoryScreen
            runs={runs}
            onOpen={(chosen) => {
              setPastRun(chosen);
              setScreen("historyDetail");
            }}
          />
        );

      case "historyDetail":
        return pastRun ? <HistoryDetail run={pastRun} /> : null;

      case "settings":
        return settings ? (
          <SettingsScreen
            settings={settings}
            error={settingsError}
            busy={busy}
            onSave={(next) => void saveSettings(next)}
          />
        ) : null;

      default:
        return null;
    }
  }

  /** La ayuda dice el tope vigente, no una cifra escrita a mano que envejece con el primer ajuste. */
  function sizeLimit(): number {
    return settings?.maxFileSizeMb ?? DefaultFileSizeMb;
  }

  function runBody() {
    if (run.failure) {
      return (
        <ErrorState
          title="El análisis se ha interrumpido"
          detail={run.failure.message}
          actionLabel="Volver a las ofertas"
          onAction={backToOpenings}
        />
      );
    }

    if (run.cancelled || run.totals) {
      return (
        <RunSummary
          run={run.totals ? run : withPartialTotals(run)}
          stopped={run.cancelled}
          onOpenResults={() => void bridge.openResults()}
          onFinish={backToOpenings}
        />
      );
    }

    // Con la confirmación abierta el botón sobra: la decisión ya está en la franja.
    return (
      <RunProgress
        run={run}
        onStop={exit ? null : () => setExit({ label: "Detener", leave: () => {} })}
      />
    );
  }
}

/** Un análisis detenido no deja totales, así que se cuentan los CV que sí llegaron a un desenlace. */
function withPartialTotals(run: RunState): RunState {
  const done = processed(run);

  return {
    ...run,
    totals: {
      processed: done,
      toReview: settled(run) - done,
      rejected: 0,
      excludedByUser: 0,
      failedMoves: 0
    }
  };
}

function added(names: ReadonlySet<string>, fileNames: string[]): ReadonlySet<string> {
  const next = new Set(names);

  for (const fileName of fileNames) next.add(fileName);

  return next;
}

function removed(names: ReadonlySet<string>, fileNames: string[]): ReadonlySet<string> {
  const next = new Set(names);

  for (const fileName of fileNames) next.delete(fileName);

  return next;
}

function Loading() {
  return (
    <div className="flex h-full items-center justify-center">
      <LoaderCircle className="text-ink-300 size-6 animate-spin" />
    </div>
  );
}

function describe(failure: unknown): { message: string; code: string | null } {
  return failure instanceof BridgeError
    ? { message: failure.message, code: failure.code }
    : { message: "Ha ocurrido un fallo inesperado.", code: null };
}
