import { defineConfig } from "vite";

// Dev only: the UI runs on 127.0.0.1:5173 and proxies /api to the local FastAPI server.
// changeOrigin rewrites the Host header to 127.0.0.1:8765 so the backend's host check passes.
// Production use: `npm run build`, then the backend serves ./dist itself (single origin, no CORS).
export default defineConfig({
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": { target: "http://127.0.0.1:8765", changeOrigin: true },
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
  },
});
