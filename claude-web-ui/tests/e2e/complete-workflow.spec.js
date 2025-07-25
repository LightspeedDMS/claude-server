/**
 * Complete End-to-End Workflow Tests for Claude Web UI
 * Tests full user journeys from authentication through job completion
 */

import { test, expect } from '@playwright/test'
import { LoginPage, DashboardPage } from './helpers/page-objects.js'
import { auth, network } from './helpers/test-helpers.js'
import testData from '../fixtures/test-data.js'

test.describe('Complete Claude Web UI Workflows', () => {
  let loginPage
  let dashboardPage

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page)
    dashboardPage = new DashboardPage(page)
  })

  test.describe('New User Complete Journey', () => {
    test('full user journey: login → register repo → create job → monitor → browse files', async ({ page }) => {
      // Mock all necessary API endpoints for complete workflow
      await setupCompleteWorkflowMocks(page)

      // 1. AUTHENTICATION PHASE
      await page.goto('/')
      
      // Verify we're on login page
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
      
      // Login with valid credentials
      await page.fill('[data-testid="username"]', testData.users.valid.username)
      await page.fill('[data-testid="password"]', testData.users.valid.password)
      await page.click('[data-testid="login-button"]')
      
      // Verify successful login and dashboard access
      await expect(page).toHaveURL(/.*\/dashboard/)
      await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
      await expect(page.locator('[data-testid="user-menu"]')).toContainText(testData.users.valid.username)

      // 2. REPOSITORY REGISTRATION PHASE
      await page.click('[data-testid="repositories-nav"]')
      await expect(page).toHaveURL(/.*\/repositories/)
      
      // Start repository registration
      await page.click('[data-testid="register-repo-button"]')
      await expect(page.locator('[data-testid="repo-registration-form"]')).toBeVisible()
      
      // Fill repository details
      const repoData = testData.workflows.completeJourney.repository
      await page.fill('[data-testid="repo-name"]', repoData.name)
      await page.fill('[data-testid="repo-url"]', repoData.url)
      await page.fill('[data-testid="repo-description"]', repoData.description)
      
      // Submit repository registration
      await page.click('[data-testid="register-submit"]')
      await expect(page.locator('[data-testid="registration-success"]')).toContainText('successfully registered')
      
      // Monitor repository setup progress
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('cloning')
      
      // Wait for repository to become ready (with status transitions)
      const statusProgression = ['cloning', 'indexing', 'ready']
      for (const expectedStatus of statusProgression) {
        await expect(page.locator('[data-testid="repo-status"]'))
          .toContainText(expectedStatus, { timeout: 30000 })
        
        if (expectedStatus !== 'ready') {
          await page.waitForTimeout(3000) // Simulate setup time
        }
      }
      
      // Verify repository is ready for use
      await expect(page.locator('[data-testid="create-job-button"]')).toBeEnabled()

      // 3. JOB CREATION PHASE
      await page.click('[data-testid="create-job-button"]')
      await expect(page).toHaveURL(/.*\/jobs\/create/)
      
      // Verify repository is pre-selected
      const repoSelect = page.locator('[data-testid="repository-select"]')
      const selectedValue = await repoSelect.inputValue()
      expect(selectedValue).toBe(repoData.id)
      
      // Upload test files
      const testFiles = testData.workflows.completeJourney.files
      await page.setInputFiles('[data-testid="file-upload"]', testFiles.map(file => ({
        name: file.name,
        mimeType: file.mimeType,
        buffer: Buffer.from(file.content)
      })))
      
      // Verify file upload success
      for (const file of testFiles) {
        await expect(page.locator('[data-testid="uploaded-files"]')).toContainText(file.name)
      }
      await expect(page.locator('[data-testid="file-count"]')).toContainText(`${testFiles.length} files`)
      
      // Enter comprehensive prompt
      const prompt = testData.workflows.completeJourney.prompt
      await page.fill('[data-testid="prompt-input"]', prompt)
      
      // Submit job creation
      await page.click('[data-testid="submit-job"]')
      
      // Verify job creation and navigation to job details
      await expect(page).toHaveURL(/.*\/jobs\/e2e-test-job-999/)
      await expect(page.locator('[data-testid="job-title"]')).toContainText('Analyze repository structure')
      await expect(page.locator('[data-testid="job-status"]')).toContainText('created')

      // 4. JOB MONITORING PHASE
      // Monitor job status transitions in real-time
      const jobStatusProgression = ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running', 'completed']
      for (const expectedStatus of jobStatusProgression) {
        await expect(page.locator('[data-testid="job-status"]'))
          .toContainText(expectedStatus.replace('_', ' '), { timeout: 45000 })
        
        // Verify status-specific indicators
        await expect(page.locator('[data-testid="status-badge"]'))
          .toHaveClass(new RegExp(`status-${expectedStatus}`))
        
        // Check for appropriate progress indicators
        if (['git_pulling', 'cidx_indexing', 'running'].includes(expectedStatus)) {
          await expect(page.locator('[data-testid="progress-indicator"]')).toBeVisible()
        }
        
        if (expectedStatus !== 'completed') {
          await page.waitForTimeout(4000) // Simulate processing time
        }
      }
      
      // Verify job completion details
      await expect(page.locator('[data-testid="completion-time"]')).toBeVisible()
      await expect(page.locator('[data-testid="exit-code"]')).toContainText('0')
      await expect(page.locator('[data-testid="job-output"]')).not.toBeEmpty()
      
      // Verify uploaded files are reflected in job output
      for (const file of testFiles) {
        await expect(page.locator('[data-testid="job-output"]')).toContainText(file.name)
      }

      // 5. FILE BROWSING PHASE
      await page.click('[data-testid="files-tab"]')
      await expect(page.locator('[data-testid="file-browser"]')).toBeVisible()
      
      // Verify workspace files are available
      await expect(page.locator('[data-testid="file-tree"]')).toBeVisible()
      await expect(page.locator('[data-testid="file-list"]')).toBeVisible()
      
      // Browse uploaded files
      for (const file of testFiles) {
        const fileItem = page.locator(`[data-testid="file-item"][data-filename="${file.name}"]`)
        await expect(fileItem).toBeVisible()
        
        // Click to preview file
        await fileItem.click()
        await page.waitForTimeout(1000)
        
        // Verify file preview
        const preview = page.locator('[data-testid="file-preview"]')
        await expect(preview).toBeVisible()
        if (file.mimeType.startsWith('text/')) {
          await expect(preview).toContainText(file.content.substring(0, 50))
        }
      }
      
      // Test file download
      const downloadPromise = page.waitForDownload()
      await page.click(`[data-testid="file-item"][data-filename="${testFiles[0].name}"] [data-testid="download-file"]`)
      const download = await downloadPromise
      expect(download.suggestedFilename()).toBe(testFiles[0].name)
      
      // Browse repository structure
      const directoryItem = page.locator('[data-testid="file-item"][data-type="directory"]').first()
      if (await directoryItem.isVisible()) {
        await directoryItem.dblclick()
        await page.waitForTimeout(1000)
        
        // Verify navigation
        await expect(page.locator('[data-testid="breadcrumb-nav"]')).toContainText('/')
      }

      // 6. WORKFLOW COMPLETION VERIFICATION
      // Navigate back to dashboard to verify everything is tracked
      await page.click('[data-testid="dashboard-nav"]')
      await expect(page).toHaveURL(/.*\/dashboard/)
      
      // Verify recent activity shows our actions
      await expect(page.locator('[data-testid="recent-jobs"]')).toContainText('Analyze repository structure')
      await expect(page.locator('[data-testid="recent-repositories"]')).toContainText(repoData.name)
      
      // Verify statistics are updated
      const statsCards = page.locator('[data-testid="stats-card"]')
      await expect(statsCards.locator('[data-testid="total-repositories"]')).toContainText('1')
      await expect(statsCards.locator('[data-testid="completed-jobs"]')).toContainText('1')
      
      // 7. LOGOUT TO COMPLETE JOURNEY
      await page.click('[data-testid="user-menu"]')
      await page.click('[data-testid="logout-button"]')
      
      // Verify successful logout
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
      
      // Verify session is cleared
      const tokenInStorage = await page.evaluate(() => localStorage.getItem('token'))
      expect(tokenInStorage).toBeNull()
    })

    test('should handle workflow interruptions gracefully', async ({ page }) => {
      await setupCompleteWorkflowMocks(page)
      
      // Start workflow - login and register repository
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      await page.click('[data-testid="repositories-nav"]')
      await page.click('[data-testid="register-repo-button"]')
      
      const repoData = testData.workflows.interruption.repository
      await page.fill('[data-testid="repo-name"]', repoData.name)
      await page.fill('[data-testid="repo-url"]', repoData.url)
      await page.click('[data-testid="register-submit"]')
      
      // Wait for repository setup to start
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('cloning')
      
      // Simulate browser refresh during setup
      await page.reload()
      
      // Verify state is preserved and setup continues
      await expect(page.locator('[data-testid="repo-status"]')).toContainText(/cloning|indexing/)
      
      // Continue monitoring until ready
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('ready', { timeout: 30000 })
      
      // Create job and then simulate network disconnection
      await page.click('[data-testid="create-job-button"]')
      await page.fill('[data-testid="prompt-input"]', 'Test prompt for interruption')
      
      // Disconnect network before job submission
      await page.context().setOffline(true)
      await page.click('[data-testid="submit-job"]')
      
      // Verify network error handling
      await expect(page.locator('[data-testid="network-error"]')).toContainText(/network|connection/)
      
      // Restore network and retry
      await page.context().setOffline(false)
      await page.click('[data-testid="retry-submit"]')
      
      // Verify job creation proceeds normally
      await expect(page).toHaveURL(/.*\/jobs\/.*/)
      await expect(page.locator('[data-testid="job-status"]')).toContainText('created')
    })
  })

  test.describe('Power User Workflows', () => {
    test('multi-repository parallel job execution workflow', async ({ page }) => {
      await setupParallelWorkflowMocks(page)
      
      // Login as power user
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      // Register multiple repositories in parallel
      const repositories = testData.workflows.parallel.repositories
      
      for (const repo of repositories) {
        // Open new tab for each repository registration
        const newTab = await page.context().newPage()
        await newTab.goto('/repositories')
        await newTab.click('[data-testid="register-repo-button"]')
        
        await newTab.fill('[data-testid="repo-name"]', repo.name)
        await newTab.fill('[data-testid="repo-url"]', repo.url)
        await newTab.click('[data-testid="register-submit"]')
        
        await newTab.close()
      }
      
      // Return to main page and verify all repositories are being processed
      await page.goto('/repositories')
      
      for (const repo of repositories) {
        await expect(page.locator(`[data-testid="repo-card-${repo.id}"]`)).toBeVisible()
      }
      
      // Wait for all repositories to become ready
      for (const repo of repositories) {
        await expect(page.locator(`[data-testid="repo-card-${repo.id}"] [data-testid="repo-status"]`))
          .toContainText('ready', { timeout: 60000 })
      }
      
      // Create jobs for all repositories
      const jobPrompts = testData.workflows.parallel.jobPrompts
      
      for (let i = 0; i < repositories.length; i++) {
        const repo = repositories[i]
        const prompt = jobPrompts[i]
        
        // Create job for this repository
        await page.goto('/jobs/create')
        await page.selectOption('[data-testid="repository-select"]', repo.id)
        await page.fill('[data-testid="prompt-input"]', prompt)
        await page.click('[data-testid="submit-job"]')
        
        // Verify job creation
        await expect(page.locator('[data-testid="job-status"]')).toContainText('created')
      }
      
      // Monitor all jobs from dashboard
      await page.goto('/dashboard')
      
      // Verify all jobs are listed in recent jobs
      for (const prompt of jobPrompts) {
        const jobTitle = prompt.substring(0, 30) // First part of prompt becomes title
        await expect(page.locator('[data-testid="recent-jobs"]')).toContainText(jobTitle)
      }
      
      // Navigate to jobs list and verify parallel execution
      await page.click('[data-testid="jobs-nav"]')
      
      // At least some jobs should be running simultaneously
      const runningJobs = page.locator('[data-testid="job-item"][data-status="running"]')
      const runningCount = await runningJobs.count()
      expect(runningCount).toBeGreaterThan(0)
      
      // Wait for all jobs to complete
      for (const prompt of jobPrompts) {
        const jobTitle = prompt.substring(0, 30)
        await expect(page.locator(`[data-testid="job-item"]:has-text("${jobTitle}") [data-testid="job-status"]`))
          .toContainText('completed', { timeout: 120000 })
      }
    })

    test('advanced file management and analysis workflow', async ({ page }) => {
      await setupAdvancedWorkflowMocks(page)
      
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      // Use existing ready repository
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Upload complex file structure
      const complexFiles = testData.workflows.advanced.complexFiles
      await page.setInputFiles('[data-testid="file-upload"]', complexFiles.map(file => ({
        name: file.name,
        mimeType: file.mimeType,
        buffer: Buffer.from(file.content)
      })))
      
      // Verify file organization in upload preview
      await expect(page.locator('[data-testid="file-tree-preview"]')).toBeVisible()
      
      // Submit advanced analysis job
      const advancedPrompt = testData.workflows.advanced.analysisPrompt
      await page.fill('[data-testid="prompt-input"]', advancedPrompt)
      await page.click('[data-testid="submit-job"]')
      
      // Monitor job with detailed progress
      await expect(page.locator('[data-testid="job-status"]')).toContainText('running', { timeout: 30000 })
      
      // Verify detailed progress indicators
      await expect(page.locator('[data-testid="processing-step"]')).toBeVisible()
      await expect(page.locator('[data-testid="files-processed"]')).toBeVisible()
      
      // Wait for completion with streaming output
      let previousOutputLength = 0
      const maxWaitTime = 120000 // 2 minutes
      const startTime = Date.now()
      
      while (Date.now() - startTime < maxWaitTime) {
        const outputElement = page.locator('[data-testid="job-output"]')
        const currentOutput = await outputElement.textContent()
        const currentLength = currentOutput ? currentOutput.length : 0
        
        if (currentLength > previousOutputLength) {
          previousOutputLength = currentLength
          // Output is growing, job is progressing
        }
        
        const status = await page.locator('[data-testid="job-status"]').textContent()
        if (status?.includes('completed')) {
          break
        }
        
        await page.waitForTimeout(2000)
      }
      
      // Verify comprehensive job completion
      await expect(page.locator('[data-testid="job-status"]')).toContainText('completed')
      await expect(page.locator('[data-testid="job-output"]')).toContainText('Analysis complete')
      
      // Explore generated workspace files
      await page.click('[data-testid="files-tab"]')
      
      // Verify complex file structure is preserved
      for (const file of complexFiles) {
        await expect(page.locator(`[data-testid="file-item"][data-filename="${file.name}"]`)).toBeVisible()
      }
      
      // Test advanced file operations
      await page.click('[data-testid="select-all-files"]')
      await expect(page.locator('[data-testid="selected-count"]')).toContainText(`${complexFiles.length} selected`)
      
      // Download multiple files as archive
      const downloadPromise = page.waitForDownload()
      await page.click('[data-testid="download-selected-archive"]')
      const download = await downloadPromise
      expect(download.suggestedFilename()).toMatch(/\.zip$/)
    })
  })

  test.describe('Error Recovery Workflows', () => {
    test('should recover from repository setup failures', async ({ page }) => {
      await setupErrorRecoveryMocks(page)
      
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      // Attempt to register repository that will fail
      await page.click('[data-testid="repositories-nav"]')
      await page.click('[data-testid="register-repo-button"]')
      
      const failingRepo = testData.workflows.errorRecovery.failingRepository
      await page.fill('[data-testid="repo-name"]', failingRepo.name)
      await page.fill('[data-testid="repo-url"]', failingRepo.url)
      await page.click('[data-testid="register-submit"]')
      
      // Wait for error state
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('error', { timeout: 30000 })
      await expect(page.locator('[data-testid="repo-error-message"]')).toContainText('Authentication failed')
      
      // Attempt to retry with corrected credentials
      await page.click('[data-testid="retry-setup-button"]')
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('cloning')
      
      // This time it should succeed
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('ready', { timeout: 30000 })
      
      // Verify repository is now usable
      await page.click('[data-testid="create-job-button"]')
      await expect(page).toHaveURL(/.*\/jobs\/create/)
      const repoSelect = page.locator('[data-testid="repository-select"]')
      const selectedValue = await repoSelect.inputValue()
      expect(selectedValue).toBe(failingRepo.id)
    })

    test('should handle job failures with retry and debugging', async ({ page }) => {
      await setupJobFailureMocks(page)
      
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      // Create job that will fail
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.fill('[data-testid="prompt-input"]', 'This job will fail for testing')
      await page.click('[data-testid="submit-job"]')
      
      // Monitor job until failure
      await expect(page.locator('[data-testid="job-status"]')).toContainText('running', { timeout: 30000 })
      await expect(page.locator('[data-testid="job-status"]')).toContainText('failed', { timeout: 60000 })
      
      // Verify failure details
      await expect(page.locator('[data-testid="exit-code"]')).toContainText('1')
      await expect(page.locator('[data-testid="job-output"]')).toContainText('Error:')
      await expect(page.locator('[data-testid="error-details"]')).toBeVisible()
      
      // Test retry functionality
      await page.click('[data-testid="retry-job-button"]')
      await expect(page.locator('[data-testid="retry-confirmation"]')).toBeVisible()
      await page.click('[data-testid="confirm-retry"]')
      
      // Verify new job is created
      await expect(page).toHaveURL(/.*\/jobs\/retry-.*/)
      await expect(page.locator('[data-testid="job-status"]')).toContainText('created')
      
      // This retry should succeed
      await expect(page.locator('[data-testid="job-status"]')).toContainText('completed', { timeout: 60000 })
    })
  })

  test.describe('Cross-Device Workflows', () => {
    test('should maintain session across browser refresh and tabs', async ({ page, context }) => {
      await setupCrossDeviceWorkflowMocks(page)
      
      // Start workflow in first tab
      await page.goto('/')
      await auth.loginSuccessfully(page)
      
      await page.click('[data-testid="repositories-nav"]')
      await page.click('[data-testid="register-repo-button"]')
      
      const repoData = testData.workflows.crossDevice.repository
      await page.fill('[data-testid="repo-name"]', repoData.name)
      await page.fill('[data-testid="repo-url"]', repoData.url)
      await page.click('[data-testid="register-submit"]')
      
      // Open second tab and verify session persistence
      const secondTab = await context.newPage()
      await secondTab.goto('/dashboard')
      
      // Should be automatically logged in
      await expect(secondTab.locator('[data-testid="dashboard"]')).toBeVisible()
      await expect(secondTab.locator('[data-testid="user-menu"]')).toContainText(testData.users.valid.username)
      
      // Verify repository appears in both tabs
      await secondTab.goto('/repositories')
      await expect(secondTab.locator(`[data-testid="repo-card-${repoData.id}"]`)).toBeVisible()
      
      // Create job in second tab
      await secondTab.goto('/jobs/create')
      await secondTab.selectOption('[data-testid="repository-select"]', repoData.id)
      await secondTab.fill('[data-testid="prompt-input"]', 'Cross-tab job creation test')
      await secondTab.click('[data-testid="submit-job"]')
      
      // Verify job appears in first tab after refresh
      await page.reload()
      await page.goto('/jobs')
      await expect(page.locator('[data-testid="job-list"]')).toContainText('Cross-tab job creation test')
      
      // Test browser refresh during job execution
      const jobUrl = page.url()
      await page.reload()
      
      // Job monitoring should resume
      await expect(page.locator('[data-testid="job-status"]')).toBeVisible()
      
      await secondTab.close()
    })
  })
})

// Helper function to setup mocks for complete workflow
async function setupCompleteWorkflowMocks(page) {
  // Authentication mock
  await page.route('**/api/auth/login', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        token: 'mock-jwt-token',
        user: testData.users.valid,
        expiresIn: 3600
      })
    })
  })

  // Repository registration mock with status progression
  let repoStatus = 'cloning'
  let statusCallCount = 0
  
  await page.route('**/api/repositories', async route => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'e2e-test-repo-999',
          name: testData.workflows.completeJourney.repository.name,
          url: testData.workflows.completeJourney.repository.url,
          status: 'cloning'
        })
      })
    } else {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          repositories: [
            {
              id: 'e2e-test-repo-999',
              name: testData.workflows.completeJourney.repository.name,
              status: repoStatus
            }
          ]
        })
      })
    }
  })

  // Repository status progression mock
  await page.route('**/api/repositories/e2e-test-repo-999', async route => {
    statusCallCount++
    if (statusCallCount > 3) repoStatus = 'indexing'
    if (statusCallCount > 6) repoStatus = 'ready'
    
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: 'e2e-test-repo-999',
        name: testData.workflows.completeJourney.repository.name,
        status: repoStatus
      })
    })
  })

  // Job creation and monitoring mocks
  let jobStatus = 'created'
  let jobCallCount = 0
  
  await page.route('**/api/jobs', async route => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'e2e-test-job-999',
          title: 'Analyze repository structure and suggest improvements',
          status: 'created',
          repository: 'e2e-test-repo-999'
        })
      })
    }
  })

  await page.route('**/api/jobs/e2e-test-job-999', async route => {
    jobCallCount++
    const statusProgression = ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running', 'completed']
    const progressIndex = Math.min(Math.floor(jobCallCount / 2), statusProgression.length - 1)
    jobStatus = statusProgression[progressIndex]
    
    const response = {
      id: 'e2e-test-job-999',
      title: 'Analyze repository structure and suggest improvements',
      status: jobStatus,
      repository: 'e2e-test-repo-999',
      output: jobStatus === 'completed' ? 
        'Repository analysis complete. Found JavaScript, HTML, and CSS files. Suggested improvements include...' : 
        `Job is ${jobStatus}...`
    }
    
    if (jobStatus === 'completed') {
      response.completedAt = new Date().toISOString()
      response.exitCode = 0
    }
    
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(response)
    })
  })

  // File browsing mock
  await page.route('**/api/jobs/e2e-test-job-999/files', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        files: testData.workflows.completeJourney.files.map(file => ({
          name: file.name,
          type: 'file',
          size: file.content.length,
          path: `/${file.name}`
        }))
      })
    })
  })

  // Dashboard statistics mock
  await page.route('**/api/dashboard/stats', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        totalRepositories: 1,
        totalJobs: 1,
        completedJobs: jobStatus === 'completed' ? 1 : 0,
        runningJobs: jobStatus === 'running' ? 1 : 0
      })
    })
  })
}

// Helper functions for other workflow mocks
async function setupParallelWorkflowMocks(page) {
  // Implementation for parallel workflow mocks
  // Similar structure to setupCompleteWorkflowMocks but for multiple repositories/jobs
}

async function setupAdvancedWorkflowMocks(page) {
  // Implementation for advanced workflow mocks
  // Includes complex file structures and detailed progress monitoring
}

async function setupErrorRecoveryMocks(page) {
  // Implementation for error recovery workflow mocks
  // Includes failing operations that can be retried
}

async function setupJobFailureMocks(page) {
  // Implementation for job failure workflow mocks
  // Includes jobs that fail and can be retried
}

async function setupCrossDeviceWorkflowMocks(page) {
  // Implementation for cross-device workflow mocks
  // Includes session persistence and multi-tab functionality
}