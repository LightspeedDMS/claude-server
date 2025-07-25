/**
 * E2E Test Helpers for Claude Web UI
 * Reusable helper functions for complex test scenarios
 */

import { expect } from '@playwright/test'
import testData from '../../fixtures/test-data.js'

/**
 * Authentication helpers
 */
export const auth = {
  /**
   * Login with valid credentials and verify success
   */
  async loginSuccessfully(page, credentials = testData.users.valid) {
    await page.goto('/')
    await page.fill('[data-testid="username"]', credentials.username)
    await page.fill('[data-testid="password"]', credentials.password)
    await page.click('[data-testid="login-button"]')
    
    // Wait for redirect to dashboard
    await page.waitForURL('**/dashboard', { timeout: 10000 })
    await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
    
    return credentials
  },

  /**
   * Attempt login with invalid credentials and verify failure
   */
  async loginWithError(page, credentials = testData.users.invalid) {
    await page.goto('/')
    await page.fill('[data-testid="username"]', credentials.username)
    await page.fill('[data-testid="password"]', credentials.password)
    await page.click('[data-testid="login-button"]')
    
    // Verify error message appears
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
    await expect(page.locator('[data-testid="error-message"]')).toContainText(credentials.expectedError)
  },

  /**
   * Logout and verify redirect to login page
   */
  async logout(page) {
    await page.click('[data-testid="user-menu"]')
    await page.click('[data-testid="logout-button"]')
    
    // Verify redirect to login page
    await page.waitForURL('**/login', { timeout: 5000 })
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
  },

  /**
   * Check if user is currently authenticated
   */
  async isAuthenticated(page) {
    try {
      await page.goto('/dashboard')
      await expect(page.locator('[data-testid="dashboard"]')).toBeVisible({ timeout: 3000 })
      return true
    } catch {
      return false
    }
  }
}

/**
 * Job management helpers
 */
export const jobs = {
  /**
   * Create a new job with specified options
   */
  async createJob(page, options = {}) {
    const {
      repository = 'frontend-project',
      prompt = 'Test job prompt',
      files = [],
      timeout = 300,
      gitAware = true,
      cidxAware = true
    } = options

    // Navigate to job creation
    await page.click('[data-testid="create-job-button"]')
    await expect(page.locator('[data-testid="job-creation-form"]')).toBeVisible()

    // Fill form
    await page.selectOption('[data-testid="repository-select"]', repository)
    await page.fill('[data-testid="prompt-input"]', prompt)

    // Upload files if provided
    if (files.length > 0) {
      const filePaths = files.map(f => `tests/fixtures/test-files/${f}`)
      await page.setInputFiles('[data-testid="file-upload"]', filePaths)
      
      // Wait for files to be uploaded
      for (const fileName of files) {
        await expect(page.locator(`[data-testid="file-item"][data-filename="${fileName}"]`)).toBeVisible()
      }
    }

    // Configure advanced options if needed
    if (timeout !== 300 || !gitAware || !cidxAware) {
      await page.click('[data-testid="advanced-options"]')
      
      if (timeout !== 300) {
        await page.fill('[data-testid="timeout-input"]', timeout.toString())
      }
      
      const gitCheckbox = page.locator('[data-testid="git-aware"]')
      if ((await gitCheckbox.isChecked()) !== gitAware) {
        await gitCheckbox.click()
      }
      
      const cidxCheckbox = page.locator('[data-testid="cidx-aware"]')
      if ((await cidxCheckbox.isChecked()) !== cidxAware) {
        await cidxCheckbox.click()
      }
    }

    // Submit job
    await page.click('[data-testid="submit-job"]')
    await page.waitForLoadState('networkidle')

    // Extract job ID from URL or response
    await page.waitForURL('**/jobs/**', { timeout: 10000 })
    const url = page.url()
    const match = url.match(/\/jobs\/([^\/]+)/)
    const jobId = match ? match[1] : null

    return jobId
  },

  /**
   * Wait for job to reach a specific status
   */
  async waitForJobStatus(page, jobId, targetStatus, timeout = 300000) {
    // Navigate to job details if not already there
    if (!page.url().includes(`/jobs/${jobId}`)) {
      await page.goto(`/jobs/${jobId}`)
    }

    const statusBadge = page.locator('[data-testid="status-badge"]')
    
    if (targetStatus === 'completed' || targetStatus === 'failed' || targetStatus === 'cancelled') {
      // Wait for any terminal status
      await expect(statusBadge).toHaveText(/^(completed|failed|cancelled)$/, { timeout })
    } else {
      // Wait for specific status
      await expect(statusBadge).toHaveText(targetStatus, { timeout })
    }

    const finalStatus = await statusBadge.textContent()
    return finalStatus.toLowerCase()
  },

  /**
   * Monitor job progress with real-time updates
   */
  async monitorJobProgress(page, jobId, onStatusChange = null) {
    await page.goto(`/jobs/${jobId}`)
    
    const statusBadge = page.locator('[data-testid="status-badge"]')
    const progressBar = page.locator('[data-testid="progress-bar"]')
    
    let currentStatus = ''
    const statusHistory = []
    
    // Monitor status changes
    while (true) {
      await page.waitForTimeout(1000) // Check every second
      
      const newStatus = await statusBadge.textContent()
      const progress = await progressBar.getAttribute('value') || '0'
      
      if (newStatus !== currentStatus) {
        currentStatus = newStatus
        const statusUpdate = {
          status: newStatus,
          progress: parseInt(progress),
          timestamp: new Date().toISOString()
        }
        
        statusHistory.push(statusUpdate)
        
        if (onStatusChange) {
          await onStatusChange(statusUpdate)
        }
        
        // Break on terminal status
        if (['completed', 'failed', 'cancelled'].includes(newStatus.toLowerCase())) {
          break
        }
      }
    }
    
    return {
      finalStatus: currentStatus.toLowerCase(),
      statusHistory
    }
  },

  /**
   * Cancel a running job
   */
  async cancelJob(page, jobId) {
    await page.goto(`/jobs/${jobId}`)
    await page.click('[data-testid="cancel-job"]')
    
    // Wait for cancellation to complete
    await expect(page.locator('[data-testid="status-badge"]')).toHaveText('cancelled', { timeout: 30000 })
  },

  /**
   * Delete a job
   */
  async deleteJob(page, jobId) {
    await page.goto(`/jobs/${jobId}`)
    await page.click('[data-testid="delete-job"]')
    
    // Handle confirmation dialog if present
    const confirmButton = page.locator('[data-testid="confirm-delete"]')
    if (await confirmButton.isVisible({ timeout: 1000 })) {
      await confirmButton.click()
    }
    
    // Verify redirect away from job details
    await page.waitForURL('**/jobs', { timeout: 5000 })
  },

  /**
   * Get job output content
   */
  async getJobOutput(page, jobId) {
    await page.goto(`/jobs/${jobId}`)
    await expect(page.locator('[data-testid="job-output"]')).toBeVisible()
    
    const output = await page.locator('[data-testid="job-output"]').textContent()
    return output
  },

  /**
   * Get list of generated files from job
   */
  async getGeneratedFiles(page, jobId) {
    await page.goto(`/jobs/${jobId}`)
    
    const fileItems = await page.locator('[data-testid="file-item"]').all()
    const files = []
    
    for (const item of fileItems) {
      const name = await item.getAttribute('data-filename')
      const size = await item.locator('[data-testid="file-size"]').textContent()
      const type = await item.getAttribute('data-filetype') || 'unknown'
      
      files.push({ name, size, type })
    }
    
    return files
  }
}

/**
 * Repository management helpers
 */
export const repositories = {
  /**
   * Register a new repository
   */
  async registerRepository(page, options = {}) {
    const {
      name = `test-repo-${Date.now()}`,
      url = 'https://github.com/test/sample-repo.git',
      description = 'Test repository for E2E testing'
    } = options

    // Navigate to repositories page
    await page.click('[data-testid="repositories-nav"]')
    await expect(page.locator('[data-testid="repository-list"]')).toBeVisible()

    // Start registration
    await page.click('[data-testid="register-repo-button"]')
    await expect(page.locator('[data-testid="register-form"]')).toBeVisible()

    // Fill registration form
    await page.fill('[data-testid="repo-name-input"]', name)
    await page.fill('[data-testid="repo-url-input"]', url)
    await page.fill('[data-testid="repo-description-input"]', description)

    // Submit registration
    await page.click('[data-testid="submit-register"]')
    await page.waitForLoadState('networkidle')

    return { name, url, description }
  },

  /**
   * Wait for repository to be ready
   */
  async waitForRepositoryReady(page, repoName, timeout = 120000) {
    const repoItem = `[data-testid="repository-item"][data-repo-name="${repoName}"]`
    const statusElement = `${repoItem} [data-testid="repo-status"]`
    
    await expect(page.locator(statusElement)).toHaveText('ready', { timeout })
  },

  /**
   * Unregister a repository
   */
  async unregisterRepository(page, repoName) {
    await page.click('[data-testid="repositories-nav"]')
    
    const repoItem = `[data-testid="repository-item"][data-repo-name="${repoName}"]`
    const unregisterButton = `${repoItem} [data-testid="unregister-repo"]`
    
    await page.click(unregisterButton)
    
    // Handle confirmation if present
    const confirmButton = page.locator('[data-testid="confirm-unregister"]')
    if (await confirmButton.isVisible({ timeout: 1000 })) {
      await confirmButton.click()
    }
    
    await page.waitForLoadState('networkidle')
  },

  /**
   * Get list of all repositories
   */
  async getRepositories(page) {
    await page.click('[data-testid="repositories-nav"]')
    await expect(page.locator('[data-testid="repository-list"]')).toBeVisible()
    
    const repoItems = await page.locator('[data-testid="repository-item"]').all()
    const repositories = []
    
    for (const item of repoItems) {
      const name = await item.locator('[data-testid="repo-name"]').textContent()
      const status = await item.locator('[data-testid="repo-status"]').textContent()
      const url = await item.locator('[data-testid="repo-url"]').textContent()
      
      repositories.push({ name, status, url })
    }
    
    return repositories
  }
}

/**
 * File management helpers
 */
export const files = {
  /**
   * Upload files to job creation form
   */
  async uploadFiles(page, fileNames) {
    const filePaths = fileNames.map(name => `tests/fixtures/test-files/${name}`)
    await page.setInputFiles('[data-testid="file-upload"]', filePaths)
    
    // Wait for all files to be uploaded
    for (const fileName of fileNames) {
      await expect(page.locator(`[data-testid="file-item"][data-filename="${fileName}"]`)).toBeVisible()
    }
  },

  /**
   * Simulate drag and drop file upload
   */
  async dragAndDropFiles(page, fileNames) {
    const uploadArea = page.locator('[data-testid="upload-area"]')
    
    for (const fileName of fileNames) {
      const filePath = `tests/fixtures/test-files/${fileName}`
      
      // Simulate drag and drop
      await uploadArea.setInputFiles(filePath, { noWaitAfter: true })
    }
    
    await page.waitForLoadState('networkidle')
    
    // Verify files are uploaded
    for (const fileName of fileNames) {
      await expect(page.locator(`[data-testid="file-item"][data-filename="${fileName}"]`)).toBeVisible()
    }
  },

  /**
   * Download a file from job results
   */
  async downloadFile(page, jobId, fileName) {
    await page.goto(`/jobs/${jobId}`)
    
    const fileItem = `[data-testid="file-item"][data-filename="${fileName}"]`
    const downloadButton = `${fileItem} [data-testid="download-file"]`
    
    // Start download
    const downloadPromise = page.waitForEvent('download')
    await page.click(downloadButton)
    const download = await downloadPromise
    
    return {
      suggestedFilename: download.suggestedFilename(),
      path: await download.path()
    }
  },

  /**
   * View file content in browser
   */
  async viewFile(page, jobId, fileName) {
    await page.goto(`/jobs/${jobId}`)
    
    const fileItem = `[data-testid="file-item"][data-filename="${fileName}"]`
    const viewButton = `${fileItem} [data-testid="view-file"]`
    
    await page.click(viewButton)
    
    // Wait for file viewer to open
    await expect(page.locator('[data-testid="file-viewer"]')).toBeVisible()
    
    const content = await page.locator('[data-testid="file-content"]').textContent()
    return content
  }
}

/**
 * Network and error handling helpers
 */
export const network = {
  /**
   * Simulate network error
   */
  async simulateNetworkError(page, urlPattern) {
    await page.route(urlPattern, route => {
      route.abort('failed')
    })
  },

  /**
   * Simulate slow network
   */
  async simulateSlowNetwork(page, urlPattern, delay = 5000) {
    await page.route(urlPattern, async route => {
      await page.waitForTimeout(delay)
      await route.continue()
    })
  },

  /**
   * Mock API response
   */
  async mockApiResponse(page, urlPattern, response) {
    await page.route(urlPattern, route => {
      route.fulfill({
        status: response.status || 200,
        contentType: 'application/json',
        body: JSON.stringify(response.body || response)
      })
    })
  },

  /**
   * Wait for specific network request
   */
  async waitForRequest(page, urlPattern, timeout = 30000) {
    return await page.waitForRequest(urlPattern, { timeout })
  },

  /**
   * Wait for specific network response
   */
  async waitForResponse(page, urlPattern, timeout = 30000) {
    return await page.waitForResponse(urlPattern, { timeout })
  }
}

/**
 * Performance monitoring helpers
 */
export const performance = {
  /**
   * Measure page load time
   */
  async measurePageLoad(page, url) {
    const startTime = Date.now()
    await page.goto(url)
    await page.waitForLoadState('networkidle')
    const endTime = Date.now()
    
    return endTime - startTime
  },

  /**
   * Measure operation time
   */
  async measureOperation(operation) {
    const startTime = Date.now()
    await operation()
    const endTime = Date.now()
    
    return endTime - startTime
  },

  /**
   * Check response times
   */
  async checkResponseTimes(page, operations) {
    const results = {}
    
    for (const [name, operation] of Object.entries(operations)) {
      const duration = await this.measureOperation(operation)
      results[name] = duration
    }
    
    return results
  }
}

/**
 * Utility helpers
 */
export const utils = {
  /**
   * Generate unique test data
   */
  generateUniqueData(prefix = 'test') {
    const timestamp = Date.now()
    const random = Math.random().toString(36).substr(2, 5)
    
    return {
      id: `${prefix}-${timestamp}-${random}`,
      name: `${prefix}-name-${timestamp}`,
      timestamp,
      random
    }
  },

  /**
   * Wait for element with custom conditions
   */
  async waitForElementWithCondition(page, selector, condition, timeout = 10000) {
    const element = page.locator(selector)
    
    await expect(async () => {
      const isVisible = await element.isVisible()
      if (!isVisible) {
        throw new Error('Element not visible')
      }
      
      const conditionMet = await condition(element)
      if (!conditionMet) {
        throw new Error('Condition not met')
      }
    }).toPass({ timeout })
    
    return element
  },

  /**
   * Take screenshot with custom name
   */
  async takeScreenshot(page, name) {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
    const filename = `${name}-${timestamp}.png`
    
    await page.screenshot({ 
      path: `test-results/screenshots/${filename}`,
      fullPage: true 
    })
    
    return filename
  },

  /**
   * Verify element accessibility
   */
  async checkAccessibility(page, selector) {
    const element = page.locator(selector)
    
    // Basic accessibility checks
    const hasAriaLabel = await element.getAttribute('aria-label')
    const hasAltText = await element.getAttribute('alt')
    const hasRole = await element.getAttribute('role')
    const isVisible = await element.isVisible()
    const isFocusable = await element.isEnabled()
    
    return {
      hasAriaLabel: !!hasAriaLabel,
      hasAltText: !!hasAltText,
      hasRole: !!hasRole,
      isVisible,
      isFocusable
    }
  }
}

export default {
  auth,
  jobs,
  repositories,
  files,
  network,
  performance,
  utils
}