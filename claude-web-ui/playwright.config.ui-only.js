import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for UI-Only E2E tests
 * No Claude Batch Server required - tests pure UI functionality
 */
export default defineConfig({
  testDir: './tests/e2e',
  
  /* Run tests in files in parallel */
  fullyParallel: true,
  
  /* Fail the build on CI if you accidentally left test.only in the source code */
  forbidOnly: !!process.env.CI,
  
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  
  /* Opt out of parallel tests on CI */
  workers: process.env.CI ? 1 : undefined,
  
  /* Reporter to use */
  reporter: [
    ['html'],
    ['json', { outputFile: 'test-results/ui-only-results.json' }],
    ['junit', { outputFile: 'test-results/ui-only-results.xml' }]
  ],
  
  /* Shared settings for all the projects below */
  use: {
    /* Base URL to use in actions like `await page.goto('/')` */
    baseURL: 'http://localhost:5173',
    
    /* Run browser in non-headless mode for debugging */
    headless: false,
    
    /* Slow down operations for debugging */
    slowMo: 1000,
    
    /* Collect trace when retrying the failed test */
    trace: 'on-first-retry',
    
    /* Take screenshot on failure */
    screenshot: 'only-on-failure',
    
    /* Record video for failed tests */
    video: 'retain-on-failure',
    
    /* Global test timeout */
    actionTimeout: 15000,
    
    /* Navigation timeout */
    navigationTimeout: 30000
  },
  
  /* Global test timeout */
  timeout: 60000,
  
  /* Expect timeout */
  expect: {
    timeout: 10000
  },

  /* Configure projects - CHROMIUM-BASED BROWSERS ONLY */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    }
  ],

  /* Run your local dev server before starting the tests */
  webServer: {
    command: 'npm run dev',
    port: 5173,
    reuseExistingServer: !process.env.CI,
    timeout: 60000 // 1 minute to start dev server
  },
  
  /* NO GLOBAL SETUP OR TEARDOWN - UI tests don't need server management */
  
  /* Output directory for test artifacts */
  outputDir: 'test-results/ui-only/',
  
  /* Test match patterns - only UI-only tests */
  testMatch: [
    'tests/e2e/ui-only.spec.js'
  ]
})