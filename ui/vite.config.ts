import path from "node:path";

import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Los componentes copiados se importan entre sí por el nombre del paquete original,
  // así que el alias es lo que permite conservarlos sin editar.
  resolve: {
    alias: { "@repo/ui": path.resolve(import.meta.dirname, "src/vendor/design-system") }
  },
  build: {
    // El ejecutable embebe cada archivo del bundle, así que menos archivos es menos recurso.
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 1200
  }
});
