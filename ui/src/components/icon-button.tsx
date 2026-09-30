import type { ComponentType } from "react";

import { Button } from "@repo/ui/components/ui/button";

interface IconButtonProps {
  icon: ComponentType<{ className?: string }>;
  label: string;
  onClick: () => void;
  spinning?: boolean;
}

/**
 * A header icon. The text travels in the tooltip and as the accessible name, because the icon alone
 * doesn't say what it does.
 */
export function IconButton({ icon: Icon, label, onClick, spinning }: IconButtonProps) {
  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={onClick}
      disabled={spinning}
      title={label}
      aria-label={label}
    >
      <Icon className={`size-4 ${spinning ? "animate-spin" : ""}`} />
    </Button>
  );
}
