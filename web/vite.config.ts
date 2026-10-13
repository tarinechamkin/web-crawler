import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development, /api is forwarded to the API so the browser sees one origin (no CORS needed).
// In Docker, nginx does the same forwarding (see nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
});
