/**
 * Playwright Global Setup with Claude Batch Server Management
 * 
 * Manages full E2E test environment:
 * 1. Checks if Claude Batch Server is running
 * 2. Starts Claude Batch Server if needed
 * 3. Waits for both web UI and API to be ready
 * 4. Stores server state for proper teardown
 */

import { chromium } from '@playwright/test'
import { setupServer } from './e2e/helpers/server-manager.js'

async function globalSetup(config) {
  console.log('🚀 Starting global setup for Claude Web UI E2E tests...')
  
  try {
    // STEP 1: Ensure Claude Batch Server is running
    console.log('🔧 Setting up Claude Batch Server...')
    const serverConfig = await setupServer()
    
    // Store server configuration for tests and teardown
    process.env.E2E_SERVER_BASE_URL = serverConfig.baseUrl
    process.env.E2E_SERVER_WAS_RUNNING = serverConfig.wasAlreadyRunning.toString()
    process.env.E2E_SERVER_STARTED_BY_TEST = serverConfig.startedByTest.toString()
    
    console.log('✅ Claude Batch Server is ready')
    console.log(`📊 Server URL: ${serverConfig.baseUrl}`)
    console.log(`📊 Was already running: ${serverConfig.wasAlreadyRunning}`)
    console.log(`📊 Started by test: ${serverConfig.startedByTest}`)
  
    // STEP 2: Ensure Web UI is ready
    const baseURL = config.use?.baseURL || 'http://localhost:5173'
    console.log(`🌐 Setting up Web UI at: ${baseURL}`)
    
    await waitForDevServer(baseURL)
    
    // STEP 3: Initialize browser for setup verification
    console.log('🔍 Verifying full E2E environment...')
    const browser = await chromium.launch()
    const context = await browser.newContext()
    const page = await context.newPage()
    
    try {
      // Test basic page load
      await page.goto(baseURL, { waitUntil: 'networkidle' })
      
      // Verify login form is present
      const loginForm = await page.locator('[data-testid="login-container"]').isVisible()
      if (loginForm) {
        console.log('✅ Web UI login form ready')
      }
      
      // Test API connectivity by attempting a health check from the UI
      await page.evaluate(async (apiUrl) => {
        try {
          const response = await fetch(`${apiUrl}/health`)
          if (!response.ok) {
            throw new Error(`API health check failed: ${response.status}`)
          }
          console.log('✅ API connectivity verified from browser')
        } catch (error) {
          console.warn('⚠️ API connectivity test failed:', error.message)
          // This might be OK if the health endpoint doesn't exist
        }
      }, serverConfig.baseUrl)
      
      // Store clean state for tests
      await page.context().storageState({ path: 'tests/fixtures/initial-state.json' })
      
    } finally {
      await browser.close()
    }
    
    console.log('✅ Complete E2E environment setup successful')
    console.log('🎯 Tests can now run against real Claude Batch Server integration')
    
  } catch (error) {
    console.error('❌ E2E test setup failed:', error.message)
    console.error('')
    console.error('This means E2E tests cannot run. Common issues:')
    console.error('• Claude Batch Server project not found at ../claude-batch-server/')
    console.error('• .NET SDK not installed or `dotnet` command not available')
    console.error('• Port 8080 already in use by another process')
    console.error('• Database or other server dependencies not configured')
    console.error('• Web UI dev server not starting on port 5173')
    console.error('')
    console.error('To run E2E tests, you need:')
    console.error('1. Claude Batch Server project in ../claude-batch-server/')
    console.error('2. .NET SDK installed with working `dotnet run` command')
    console.error('3. All server dependencies properly configured')
    console.error('4. Web UI dev server starting successfully')
    console.error('')
    
    // Exit with error to prevent tests from running against broken setup
    process.exit(1)
  }
}

/**
 * Wait for dev server to be ready with retry logic
 */
async function waitForDevServer(baseURL, maxRetries = 30, delay = 2000) {
  for (let i = 0; i < maxRetries; i++) {
    try {
      const response = await fetch(`${baseURL}/`)
      if (response.ok) {
        console.log(`✅ Dev server is ready (attempt ${i + 1}/${maxRetries})`)
        return
      }
      console.log(`⏳ Dev server not ready, status: ${response.status} (attempt ${i + 1}/${maxRetries})`)
    } catch (error) {
      console.log(`⏳ Waiting for dev server (attempt ${i + 1}/${maxRetries}): ${error.message}`)
    }
    
    if (i < maxRetries - 1) {
      await new Promise(resolve => setTimeout(resolve, delay))
    }
  }
  
  throw new Error(`❌ Dev server failed to start after ${maxRetries} attempts`)
}

/**
 * Setup authentication state for tests
 */
async function setupAuthenticationState(page, baseURL) {
  try {
    // Navigate to login page
    await page.goto(baseURL)
    
    // Check if we can access the login form
    const loginContainer = page.locator('[data-testid="login-container"]')
    await loginContainer.waitFor({ state: 'visible', timeout: 10000 })
    
    console.log('✅ Authentication system is accessible')
    
    // Store the clean initial state for tests
    await page.context().storageState({ path: 'tests/fixtures/initial-state.json' })
    
  } catch (error) {
    console.warn('⚠️ Could not setup authentication state:', error.message)
    // This is not critical for tests to run
  }
}

export default globalSetup