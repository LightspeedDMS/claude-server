import { defineConfig } from 'vite'

export default defineConfig(({ mode }) => {
  // In production, nginx handles the API routing
  // In development, proxy directly to the API server
  const apiTarget = process.env.VITE_API_URL || 'http://localhost:5000'
  
  return {
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/api/, '')
        },
        '/auth': {
          target: apiTarget,
          changeOrigin: true
        }
      }
    },
    build: {
      outDir: 'dist',
      assetsDir: 'assets'
    }
  }
})