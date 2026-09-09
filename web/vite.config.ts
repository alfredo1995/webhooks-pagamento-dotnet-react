/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Em desenvolvimento o Vite faz proxy para a API, entao o front usa sempre
// caminhos relativos — o mesmo comportamento do nginx em producao. Assim nao
// existe URL de API espalhada pelo codigo nem diferenca de origem entre os
// ambientes.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.API_URL ?? 'http://localhost:5090', changeOrigin: true },
      '/health': { target: process.env.API_URL ?? 'http://localhost:5090', changeOrigin: true },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },

  test: {
    environment: 'jsdom',
    setupFiles: ['./src/testes/configuracao.ts'],
    // Sem globais: cada teste importa o que usa. Custa duas linhas de import e
    // evita que `expect` e `vi` aparecam do nada para quem le o arquivo.
    globals: false,
    restoreMocks: true,
    css: false,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/main.tsx', 'src/testes/**', 'src/**/*.test.{ts,tsx}'],
    },
  },
})
