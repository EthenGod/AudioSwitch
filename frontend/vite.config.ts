import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  build: {
    emptyOutDir: false, // Never batch-delete earlier output.
    outDir: mode === 'desktop' ? 'dist-desktop' : 'dist',
    // Stable desktop asset names avoid embedding historical hashed builds; no cleanup needed.
    ...(mode === 'desktop' ? { rollupOptions: { output: { entryFileNames: 'assets/app.js', chunkFileNames: 'assets/[name].js', assetFileNames: 'assets/[name][extname]' } } } : {}),
  },
  test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], css: false },
}))
