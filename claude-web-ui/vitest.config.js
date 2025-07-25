import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // Test environment
    environment: 'jsdom',
    
    // Global test settings
    globals: true,
    
    // Setup files
    setupFiles: ['./tests/unit/setup.js'],
    
    // Test file patterns
    include: [
      'tests/unit/**/*.{test,spec}.{js,ts}',
      'src/**/__tests__/**/*.{test,spec}.{js,ts}'
    ],
    
    // Exclude patterns
    exclude: [
      'node_modules',
      'dist',
      'tests/e2e',
      'tests/fixtures'
    ],
    
    // Coverage configuration
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/',
        'tests/',
        'vite.config.js',
        'vitest.config.js',
        'playwright.config.js'
      ],
      thresholds: {
        global: {
          branches: 80,
          functions: 80,
          lines: 80,
          statements: 80
        }
      }
    },
    
    // Test timeout
    testTimeout: 10000,
    
    // Hook timeout
    hookTimeout: 10000,
    
    // Reporter configuration
    reporter: ['verbose', 'json'],
    
    // Output files
    outputFile: {
      json: './test-results/unit-results.json'
    },
    
    // Mock configuration
    mockReset: true,
    restoreMocks: true,
    
    // Watch options
    watch: false,
    
    // UI options for interactive testing
    ui: false,
    open: false
  },
  
  // Define global constants for tests
  define: {
    __TEST__: true,
    __APP_VERSION__: JSON.stringify('0.1.0-test')
  },
  
  // Resolve configuration
  resolve: {
    alias: {
      '@': new URL('./src', import.meta.url).pathname,
      '@tests': new URL('./tests', import.meta.url).pathname
    }
  }
})