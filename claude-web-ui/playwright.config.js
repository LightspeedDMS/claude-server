import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for Claude Web UI E2E tests
 * @see https://playwright.dev/docs/test-configuration
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
  
  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: [
    ['html'],
    ['json', { outputFile: 'test-results/results.json' }],
    ['junit', { outputFile: 'test-results/results.xml' }]
  ],
  
  /* Shared settings for all the projects below */
  use: {
    /* Base URL to use in actions like `await page.goto('/')` */
    baseURL: 'http://localhost:5173',
    
    /* Collect trace when retrying the failed test */
    trace: 'on-first-retry',
    
    /* Take screenshot on failure */
    screenshot: 'only-on-failure',
    
    /* Record video for failed tests */
    video: 'retain-on-failure',
    
    /* Global test timeout */
    actionTimeout: 15000,
    
    /* Navigation timeout */
    navigationTimeout: 60000
  },
  
  /* Global test timeout - generous for Claude Code jobs */
  timeout: 600000, // 10 minutes for complex Claude Code jobs
  
  /* Expect timeout - increased for Claude Code jobs */
  expect: {
    timeout: 30000
  },

  /* Configure projects - CHROMIUM-BASED BROWSERS ONLY per requirements */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },

    {
      name: 'Google Chrome',
      use: { ...devices['Desktop Chrome'], channel: 'chrome' },
    },

    {
      name: 'Microsoft Edge',
      use: { ...devices['Desktop Edge'], channel: 'msedge' },
    },

    /* Mobile Chromium-based testing */
    {
      name: 'Mobile Chrome',
      use: { ...devices['Pixel 5'] },
    },
  ],

  /* Run your local dev server before starting the tests */
  webServer: {
    command: 'npm run dev',
    port: 5173,
    reuseExistingServer: !process.env.CI,
    timeout: 60000 // 1 minute to start dev server
  },
  
  /* Global setup and teardown */
  globalSetup: './tests/global-setup.js',
  globalTeardown: './tests/global-teardown.js',
  
  /* Output directory for test artifacts */
  outputDir: 'test-results/',
  
  /* Test match patterns */
  testMatch: [
    'tests/e2e/**/*.spec.js',
    'tests/e2e/**/*.test.js'
  ],
  
  /* Test ignore patterns */
  testIgnore: [
    'tests/e2e/**/fixtures/**',
    'tests/e2e/**/helpers/**'
  ]
})