import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  // loadEnv rather than process.env: it reads .env files the way the rest of Vite
  // does, and it keeps this config free of a Node type dependency.
  const env = loadEnv(mode, process.cwd(), '');

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: { '@': new URL('./src', import.meta.url).pathname },
    },
    server: {
      port: 5173,
      // Fail rather than silently moving to 5174: the API's CORS policy names this
      // exact origin, so a shifted port produces confusing CORS errors instead of
      // a clear "port already in use".
      strictPort: true,
      proxy: {
        // The API is reached through a same-origin path in development, so the
        // browser issues no preflight and the app exercises the same relative
        // URLs it will use behind nginx in production.
        '/api': {
          target: env.VITE_API_URL || 'http://localhost:5199',
          changeOrigin: true,
        },
      },
    },
    build: {
      // Sourcemaps are kept in production builds. An ERP bug report arrives as a
      // stack trace from a user's browser, and without them it names minified
      // symbols and is worthless.
      sourcemap: true,
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      // Only the suites, not the pages beside them. Without this the default
      // include pattern reaches into `dist` after a build and tries to run the
      // bundle.
      include: ['src/**/*.test.{ts,tsx}'],
      css: false,
    },
  };
});
