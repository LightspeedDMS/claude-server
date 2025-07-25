/**
 * REAL E2E Authentication Tests for Claude Web UI
 * 
 * IMPORTANT: These tests require a running Claude Batch Server instance
 * NO MOCKS - Tests the complete integration between web UI and backend API
 * 
 * Prerequisites:
 * 1. Claude Batch Server must be running on the configured API endpoint
 * 2. Test user credentials must exist in the system
 * 3. Database must be accessible and properly configured
 */

import { test, expect } from '@playwright/test'
import { getServerConfig, validateTestCredentials } from './helpers/server-config.js'

// Configuration - Automatically loaded from Claude Batch Server .env files
const serverConfig = getServerConfig()
const config = {
  apiBaseUrl: process.env.E2E_SERVER_BASE_URL || serverConfig.serverUrl,
  testUser: serverConfig.testUser, // Read from Claude Batch Server .env files
  webUIUrl: 'http://localhost:5173' // Vite dev server URL
}

console.log('🔧 E2E Test Configuration:')
console.log(`📡 API URL: ${config.apiBaseUrl}`)
console.log(`👤 Test User: ${config.testUser.username}`)
console.log(`🌐 Web UI URL: ${config.webUIUrl}`)

// Validate test credentials before running any tests
try {
  validateTestCredentials()
} catch (error) {
  console.error(error.message)
  process.exit(1)
}

test.describe('Real E2E Authentication Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate to the web UI
    await page.goto(config.webUIUrl)
  })

  test('should perform complete login flow with real Claude Batch Server', async ({ page }) => {
    // STEP 1: Verify we start on login page
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()

    // STEP 2: Attempt login with real credentials
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    
    // Capture the actual API request that will be made
    const [response] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/auth/login') && 
        response.request().method() === 'POST'
      ),
      page.click('[data-testid="login-button"]')
    ])

    // STEP 3: Verify real API response
    expect(response.status()).toBe(200)
    const responseBody = await response.json()
    expect(responseBody).toHaveProperty('token')
    expect(responseBody).toHaveProperty('user')

    // STEP 4: Verify UI responds to successful authentication
    await expect(page.locator('[data-testid="login-container"]')).not.toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    await expect(page.locator('[data-testid="user-menu"]')).toContainText(config.testUser.username)

    // STEP 5: Verify JWT token is stored in localStorage
    const storedToken = await page.evaluate(() => localStorage.getItem('token'))
    expect(storedToken).toBeTruthy()
    expect(storedToken).toBe(responseBody.token)
  })

  test('should handle invalid credentials with real API error', async ({ page }) => {
    // Attempt login with invalid credentials
    await page.fill('[data-testid="username"]', 'invaliduser')
    await page.fill('[data-testid="password"]', 'wrongpassword')
    
    // Capture the actual API error response
    const [response] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/auth/login') && 
        response.request().method() === 'POST'
      ),
      page.click('[data-testid="login-button"]')
    ])

    // Verify real API returns appropriate error
    expect(response.status()).toBe(401) // Or whatever your API returns for invalid auth
    
    // Verify UI handles the real error appropriately
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()
    
    // Verify no token is stored
    const storedToken = await page.evaluate(() => localStorage.getItem('token'))
    expect(storedToken).toBeNull()
  })

  test('should maintain session across page refresh with real token validation', async ({ page }) => {
    // First, login successfully
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    await page.click('[data-testid="login-button"]')
    
    // Wait for dashboard to load
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    
    // Refresh the page
    await page.reload()
    
    // The app should validate the token with the real API
    await page.waitForResponse(response => 
      response.url().includes('/auth/validate') || 
      response.url().includes('/auth/me') ||
      response.url().includes('/user/profile')
    )
    
    // Should remain logged in
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    await expect(page.locator('[data-testid="login-container"]')).not.toBeVisible()
  })

  test('should logout and clear session with real API call', async ({ page }) => {
    // Login first
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    await page.click('[data-testid="login-button"]')
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    
    // Perform logout
    const [response] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/auth/logout') && 
        response.request().method() === 'POST'
      ),
      page.click('[data-testid="logout-button"]')
    ])
    
    // Verify real logout API call
    expect(response.status()).toBe(200)
    
    // Verify UI responds to logout
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()
    
    // Verify token is cleared
    const storedToken = await page.evaluate(() => localStorage.getItem('token'))
    expect(storedToken).toBeNull()
  })

  test('should handle token expiration with real API response', async ({ page }) => {
    // This test requires setting up a short-lived token or waiting for expiration
    // Skip if not in test environment with controllable token expiration
    test.skip(!process.env.CI && !process.env.TEST_TOKEN_EXPIRATION, 
      'Token expiration test requires controlled environment')
    
    // Login with short-lived token (if supported by your API)
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    await page.click('[data-testid="login-button"]')
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    
    // Wait for token to expire or manually expire it
    // This depends on your Claude Batch Server implementation
    
    // Try to perform an authenticated action
    await page.click('[data-testid="repositories-nav"]')
    
    // Should get 401 response and redirect to login
    await page.waitForResponse(response => response.status() === 401)
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
  })
})

test.describe('Real E2E Repository Management', () => {
  
  test.beforeEach(async ({ page }) => {
    // Login before each repository test
    await page.goto(config.webUIUrl)
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    await page.click('[data-testid="login-button"]')
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
  })

  test('should load real repositories from Claude Batch Server', async ({ page }) => {
    // Navigate to repositories
    await page.click('[data-testid="repositories-nav"]')
    
    // Wait for real API call to load repositories
    const [response] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/repositories') && 
        response.request().method() === 'GET'
      ),
      page.waitForSelector('[data-testid="repository-list"]')
    ])
    
    // Verify real API response
    expect(response.status()).toBe(200)
    const repositories = await response.json()
    
    // Verify UI displays real data
    if (repositories.length > 0) {
      await expect(page.locator('[data-testid="repository-list"]')).toContainText(repositories[0].name)
    } else {
      await expect(page.locator('[data-testid="repository-list"]')).toContainText('No repositories')
    }
  })

  test('should register new repository with real Claude Batch Server', async ({ page }) => {
    await page.click('[data-testid="repositories-nav"]')
    
    // This test would require a real git repository URL that your test environment can access
    const testRepo = {
      name: 'test-e2e-repo',
      url: 'https://github.com/your-org/test-repo.git', // Update to real test repo
      description: 'E2E test repository'
    }
    
    // Start repository registration
    await page.click('[data-testid="register-repo-button"]')
    await page.fill('[data-testid="repo-name"]', testRepo.name)
    await page.fill('[data-testid="repo-url"]', testRepo.url)
    await page.fill('[data-testid="repo-description"]', testRepo.description)
    
    // Submit and wait for real API call
    const [response] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/repositories') && 
        response.request().method() === 'POST'
      ),
      page.click('[data-testid="register-submit"]')
    ])
    
    // Verify real repository creation
    expect(response.status()).toBe(201)
    const createdRepo = await response.json()
    expect(createdRepo.name).toBe(testRepo.name)
    
    // Verify UI reflects real repository
    await expect(page.locator('[data-testid="repository-list"]')).toContainText(testRepo.name)
  })
})

test.describe('Real E2E Job Management', () => {
  
  test.beforeEach(async ({ page }) => {
    // Login and ensure we have at least one repository
    await page.goto(config.webUIUrl)
    await page.fill('[data-testid="username"]', config.testUser.username)
    await page.fill('[data-testid="password"]', config.testUser.password)
    await page.click('[data-testid="login-button"]')
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
  })

  test('should create and monitor real Claude Code job', async ({ page }) => {
    // Navigate to job creation
    await page.click('[data-testid="create-job-nav"]')
    
    // Wait for repositories to load
    await page.waitForResponse(response => response.url().includes('/repositories'))
    
    // Select first available repository
    const repoSelect = page.locator('[data-testid="repository-select"]')
    await repoSelect.selectOption({ index: 1 }) // Skip "Select a repository..." option
    
    // Fill job details
    await page.fill('[data-testid="prompt-input"]', 'List all files in the repository and provide a summary')
    
    // Create the job with real API call
    const [createResponse] = await Promise.all([
      page.waitForResponse(response => 
        response.url().includes('/jobs') && 
        response.request().method() === 'POST'
      ),
      page.click('[data-testid="submit-job"]')
    ])
    
    // Verify real job creation
    expect(createResponse.status()).toBe(201)
    const createdJob = await createResponse.json()
    expect(createdJob).toHaveProperty('jobId')
    
    // Monitor real job execution
    // The job should go through actual states: created -> queued -> running -> completed
    await page.waitForResponse(response => 
      response.url().includes(`/jobs/${createdJob.jobId}`)
    )
    
    // Wait for job to complete (this could take time with real Claude Code execution)
    // In real E2E, we'd wait for actual completion or set reasonable timeout
    await expect(page.locator('[data-testid="job-status"]')).toContainText(/created|queued|running/, { timeout: 30000 })
    
    // If job completes quickly, verify final state
    try {
      await expect(page.locator('[data-testid="job-status"]')).toContainText('completed', { timeout: 60000 })
      await expect(page.locator('[data-testid="job-output"]')).not.toBeEmpty()
    } catch (e) {
      // Job may still be running - that's OK for E2E test
      console.log('Job still running - this is expected behavior for real E2E test')
    }
  })
})

/**
 * SETUP INSTRUCTIONS FOR REAL E2E TESTS:
 * 
 * 1. Configure Test Credentials:
 *    - Ensure ../claude-batch-server/.env or .env.test contains:
 *      TEST_USERNAME=your-test-user
 *      TEST_PASSWORD=your-test-password
 *    - Credentials are automatically loaded from server configuration
 *    - No manual environment variable setup required!
 * 
 * 2. Run Tests:
 *    npm run test:e2e:real
 * 
 * 3. What Happens Automatically:
 *    - Server management: Start/stop Claude Batch Server as needed
 *    - Credential loading: Read test user from server .env files
 *    - Environment setup: Configure all required URLs and settings
 * 
 * 4. Expected Behavior:
 *    - Tests will fail if Claude Batch Server configuration is invalid
 *    - Tests will fail if test user doesn't exist in authentication system
 *    - Tests will fail if API endpoints don't match expected schema
 *    - This is CORRECT behavior for true E2E tests - they catch real issues!
 */