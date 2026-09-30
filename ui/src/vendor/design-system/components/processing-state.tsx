import * as React from "react";
import { Loader2 } from "lucide-react";

import { cn } from "@repo/ui/lib/utils";
import { Progress } from "@repo/ui/components/ui/progress";

export interface ProcessingStateProps {
  /** Status lines, cycled in order (fallback before live `title` arrives). */
  messages: string[];
  reassurance?: string;
  /** Cadence between messages. Defaults to ~1800ms. */
  intervalMs?: number;
  /** Defaults to a generic spinner; callers can pass a themed illustration. */
  illustration?: React.ReactNode;
  /** Live status line — overrides the cycled `messages` when present. */
  title?: string;
  /** Live sub-line — overrides `reassurance` when present. */
  description?: string;
  /** 0–100 → determinate bar; null/undefined → indeterminate sweep. */
  progress?: number | null;
  className?: string;
}

const SWEEP_KEYFRAMES = `
@keyframes processing-state-sweep {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(400%); }
}`;

export function ProcessingState({
  messages,
  reassurance,
  intervalMs = 1800,
  illustration,
  title,
  description,
  progress,
  className,
}: ProcessingStateProps) {
  const [index, setIndex] = React.useState(0);

  React.useEffect(() => {
    setIndex(0);
    if (messages.length <= 1) return;
    const id = setInterval(() => {
      setIndex((i) => (i + 1) % messages.length);
    }, intervalMs);
    return () => clearInterval(id);
  }, [messages, intervalMs]);

  // Live agent copy wins; fall back to the cycled messages before the first event.
  const mainLine = title ?? messages[index];
  const subLine = description ?? reassurance;
  const determinate = typeof progress === "number";

  return (
    <div
      data-slot="processing-state"
      className={cn(
        "flex flex-col items-center justify-center gap-6 px-6 py-16 text-center",
        className,
      )}
    >
      <style>{SWEEP_KEYFRAMES}</style>
      <div className="text-star-500 animate-pulse">
        {illustration ?? <Loader2 className="size-12 animate-spin" />}
      </div>
      <p
        key={mainLine}
        className="text-ink-900 animate-in fade-in text-lg font-medium"
      >
        {mainLine}
      </p>
      {determinate ? (
        <Progress value={progress} className="w-60" />
      ) : (
        <div className="bg-star-100 relative h-1 w-60 overflow-hidden rounded-full">
          <span
            className="bg-star-500 absolute inset-y-0 left-0 w-2/5 rounded-full"
            style={{ animation: "processing-state-sweep 1.4s ease-in-out infinite" }}
          />
        </div>
      )}
      {subLine ? <p className="text-caption max-w-90">{subLine}</p> : null}
    </div>
  );
}
