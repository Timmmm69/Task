import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const desktopBuild = process.env.TASK_DESKTOP_BUILD === "1";

export default defineConfig({
  // Electron loads the bundle from file://. Its asset URLs must therefore be
  // relative, while the Sites build keeps the existing root-relative URLs.
  base: desktopBuild ? "./" : "/",
  build: {
    outDir: desktopBuild ? "../stage_5_6_windows_client/runtime/client" : "dist/client",
    emptyOutDir: desktopBuild,
  },
  optimizeDeps: {
    include: ["react", "react-dom/client"],
  },
  server: {
    host: "0.0.0.0",
    allowedHosts: ["terminal.local"],
    warmup: {
      clientFiles: ["./src/main.jsx"],
    },
  },
  plugins: [react()],
});
