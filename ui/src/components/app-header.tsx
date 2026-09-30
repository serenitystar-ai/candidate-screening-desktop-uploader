import type { ReactNode } from "react";
import { ChevronRight } from "lucide-react";

export interface Crumb {
  label: string;
  onClick?: () => void;
}

interface AppHeaderProps {
  crumbs: Crumb[];
  onHome: () => void;
  children?: ReactNode;
}

export function AppHeader({ crumbs, onHome, children }: AppHeaderProps) {
  return (
    <header className="border-border flex h-14 shrink-0 items-center justify-between gap-4 border-b px-6">
      <nav className="flex min-w-0 items-center gap-1 text-sm">
        <Step label="Candidate Screening Loader" onClick={onHome} />

        {crumbs.map((crumb, index) => (
          <span key={crumb.label} className="flex min-w-0 items-center gap-1">
            <ChevronRight className="text-ink-300 size-4 shrink-0" />
            <Step
              label={crumb.label}
              onClick={index === crumbs.length - 1 ? undefined : crumb.onClick}
            />
          </span>
        ))}
      </nav>

      <div className="flex shrink-0 items-center gap-1">{children}</div>
    </header>
  );
}

function Step({ label, onClick }: Crumb) {
  if (!onClick) {
    return <span className="text-ink-300 truncate">{label}</span>;
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className="text-ink-500 hover:text-foreground focus-visible:ring-ring/50 cursor-pointer truncate rounded-sm transition-colors outline-none focus-visible:ring-[3px]"
    >
      {label}
    </button>
  );
}
