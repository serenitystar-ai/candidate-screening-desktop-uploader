import * as React from "react";

import { cn } from "@repo/ui/lib/utils";

export interface StickyActionBarProps {
  /** Left slot, e.g. "Back" / "Download report". */
  start?: React.ReactNode;
  /** Primary action(s) on the right. */
  end?: React.ReactNode;
  /** Inline hint/error beside the primary action (rendered as danger text). */
  hint?: React.ReactNode;
  /**
   * Neutral status line beside the primary action, for a button that is disabled by
   * something in progress rather than by something wrong — "we're preparing your
   * document". Distinct from `hint` because that slot is danger-coloured, and a wait
   * rendered in red reads as a failure.
   */
  note?: React.ReactNode;
  /** Inner content max-width (steps use 1000–1100px). */
  maxWidth?: string;
  className?: string;
}

export function StickyActionBar({
  start,
  end,
  hint,
  note,
  maxWidth = "1100px",
  className,
}: StickyActionBarProps) {
  return (
    <div
      data-slot="sticky-action-bar"
      className={cn(
        "border-border bg-card/95 sticky bottom-0 z-10 border-t backdrop-blur",
        className,
      )}
    >
      <div
        className="mx-auto flex w-full items-center gap-3 px-6 py-4"
        style={{ maxWidth }}
      >
        <div className="flex items-center gap-2">{start}</div>
        <div className="flex flex-1 items-center justify-end gap-3">
          {hint ? (
            <span className="text-danger-fg text-sm">{hint}</span>
          ) : null}
          {note ? <span className="text-caption">{note}</span> : null}
          {end}
        </div>
      </div>
    </div>
  );
}
