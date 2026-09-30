import type { FormEvent } from "react";

import { Alert } from "@repo/ui/components/ui/alert";
import { Button } from "@repo/ui/components/ui/button";
import { Input } from "@repo/ui/components/ui/input";
import { Label } from "@repo/ui/components/ui/label";

interface LoginScreenProps {
  onSubmit: (email: string, password: string) => void;
  error: string | null;
  busy: boolean;
  onHelp: () => void;
}

export function LoginScreen({ onSubmit, error, busy, onHelp }: LoginScreenProps) {
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const form = new FormData(event.currentTarget);

    onSubmit(String(form.get("email")), String(form.get("password")));
  }

  return (
    <main className="flex h-full items-center justify-center px-6">
      <form className="flex w-80 flex-col gap-5" onSubmit={handleSubmit}>
        <h1 className="text-display">Candidate Screening Loader</h1>

        <div className="flex flex-col gap-2">
          <Label htmlFor="email">Correo electrónico</Label>
          <Input id="email" name="email" type="email" autoComplete="username" autoFocus required />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="password">Contraseña</Label>
          <Input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
          />
        </div>

        {error ? <Alert variant="destructive">{error}</Alert> : null}

        <Button type="submit" disabled={busy}>
          {busy ? "Entrando…" : "Entrar"}
        </Button>

        <p className="text-caption">
          Usa tus credenciales del AI Hub.{" "}
          <button
            type="button"
            onClick={onHelp}
            className="text-primary focus-visible:ring-ring/50 cursor-pointer rounded-sm underline underline-offset-4 outline-none focus-visible:ring-[3px]"
          >
            Cómo funciona
          </button>
        </p>
      </form>
    </main>
  );
}
