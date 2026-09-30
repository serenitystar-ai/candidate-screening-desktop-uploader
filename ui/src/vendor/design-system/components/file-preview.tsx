import * as React from "react";
import { FileText, Upload } from "lucide-react";

import { cn } from "@repo/ui/lib/utils";
import { Button } from "@repo/ui/components/ui/button";

export interface FilePreviewProps {
  fileName: string;
  /** e.g. "37.4 KB · Ready to analyze" */
  meta?: string;
  onReplace?: () => void;
  /** e.g. "Choose another" */
  replaceLabel?: string;
  icon?: React.ReactNode;
  className?: string;
}

export function FilePreview({
  fileName,
  meta,
  onReplace,
  replaceLabel,
  icon,
  className,
}: FilePreviewProps) {
  return (
    <div
      data-slot="file-preview"
      className={cn(
        "border-border flex items-center gap-4 rounded-lg border p-4",
        className,
      )}
    >
      <span className="bg-star-50 flex size-11 shrink-0 items-center justify-center rounded-lg">
        {icon ?? <FileText className="text-star-500 size-6" />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-ink-900 truncate font-medium">{fileName}</p>
        {meta ? <p className="text-caption">{meta}</p> : null}
      </div>
      {onReplace ? (
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onReplace}
          className="shrink-0"
        >
          <Upload />
          {replaceLabel}
        </Button>
      ) : null}
    </div>
  );
}
