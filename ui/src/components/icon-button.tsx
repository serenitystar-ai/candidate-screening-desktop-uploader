import type { ComponentType } from "react";

import { Button } from "@repo/ui/components/ui/button";

interface IconButtonProps {
  icon: ComponentType<{ className?: string }>;
  label: string;
  onClick: () => void;
  spinning?: boolean;
}

/**
 * Un icono de la cabecera. El texto viaja en el tooltip y como nombre accesible, porque el icono solo
 * no dice qué hace.
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
