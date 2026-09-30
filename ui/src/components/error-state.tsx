import { TriangleAlert } from "lucide-react";

import { Button } from "@repo/ui/components/ui/button";

interface ErrorStateProps {
  title: string;
  detail: string;
  actionLabel: string;
  onAction: () => void;
}

/**
 * The error pattern: what happened, what to do, and a single way out. Fatal errors change the text,
 * not the shape.
 */
export function ErrorState({ title, detail, actionLabel, onAction }: ErrorStateProps) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-4 px-6 text-center">
      <span className="bg-danger-bg text-danger-fg flex size-12 items-center justify-center rounded-full">
        <TriangleAlert className="size-6" />
      </span>

      <h2 className="text-page-title">{title}</h2>
      <p className="text-body max-w-md">{detail}</p>

      <Button onClick={onAction} className="mt-2">
        {actionLabel}
      </Button>
    </div>
  );
}
