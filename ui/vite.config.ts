import path from "node:path";

import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // The design system components import each other by package name (`@repo/ui/...`),
  // so the alias is what lets them stay unedited.
  resolve: {
    alias: { "@repo/ui": path.resolve(import.meta.dirname, "src/vendor/design-system") }
  },
  build: {
    // The executable embeds every bundle file, so fewer files means fewer resources.
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 1200
  }
});
