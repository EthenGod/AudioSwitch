import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  build: { emptyOutDir: false }, // Never batch-delete earlier output.
  test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], css: false },
})
