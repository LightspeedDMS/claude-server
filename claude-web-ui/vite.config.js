import { defineConfig } from 'vite'

export default defineConfig({
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5185',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, '')
      },
      '/auth': {
        target: 'http://localhost:5185',
        changeOrigin: true
      },
      '/jobs': {
        target: 'http://localhost:5185',
        changeOrigin: true
      },
      '/repositories': {
        target: 'http://localhost:5185',
        changeOrigin: true
      },
      '/health': {
        target: 'http://localhost:5185',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets'
  }
})