/**
 * Shared test utilities for E2E and unit tests
 */

import { expect } from '@playwright/test'

/**
 * Helper function for user login in E2E tests
 */
export async function loginAsUser(page, username = 'testuser', password = 'testpass') {
  await page.goto('/')
  await page.fill('[data-testid="username"]', username)
  await page.fill('[data-testid="password"]', password)
  await page.click('[data-testid="login-button"]')
  await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
  return { username, password }
}

/**
 * Helper function for creating and starting a job in E2E tests
 */
export async function createAndStartJob(page, options = {}) {
  const {
    repository = 'test-repo',
    prompt = 'Test prompt for job creation',
    files = []
  } = options

  await page.click('[data-testid="create-job-button"]')
  await page.selectOption('[data-testid="repository-select"]', repository)
  
  if (files.length > 0) {
    const filePaths = files.map(f => `tests/fixtures/test-files/${f}`)
    await page.setInputFiles('[data-testid="file-upload"]', filePaths)
    await expect(page.locator('[data-testid="upload-complete"]')).toBeVisible({ timeout: 30000 })
  }
  
  await page.fill('[data-testid="prompt-input"]', prompt)
  await page.click('[data-testid="submit-job"]')
  
  const jobId = await page.locator('[data-testid="job-id"]').textContent()
  return jobId
}

/**
 * Helper function to wait for job completion in E2E tests
 */
export async function waitForJobCompletion(page, jobId, timeout = 300000) {
  await expect(page.locator('[data-testid="job-status"]'))
    .toContainText('completed', { timeout })
  return true
}

/**
 * Helper function to create a completed job for testing
 */
export async function createCompletedJob(page) {
  const jobId = await createAndStartJob(page, {
    repository: 'test-repo',
    prompt: 'Simple test that completes quickly',
    files: ['sample.txt']
  })
  
  await waitForJobCompletion(page, jobId, 60000) // 1 minute timeout for simple job
  return jobId
}

/**
 * Helper function to register a test repository
 */
export async function registerTestRepository(page, options = {}) {
  const {
    name = 'e2e-test-repo',
    url = 'https://github.com/test/sample-repo.git',
    description = 'E2E test repository'
  } = options

  await page.click('[data-testid="repositories-nav"]')
  await page.click('[data-testid="register-repo-button"]')
  await page.fill('[data-testid="repo-name"]', name)
  await page.fill('[data-testid="repo-url"]', url)
  await page.fill('[data-testid="repo-description"]', description)
  await page.click('[data-testid="register-submit"]')
  
  // Wait for repository to be ready
  await expect(page.locator('[data-testid="repo-status"]'))
    .toContainText('ready', { timeout: 120000 })
  
  return { name, url, description }
}

/**
 * Mock API responses for unit tests
 */
export const mockApiResponses = {
  login: {
    success: {
      token: 'mock-jwt-token',
      user: { username: 'testuser' }
    },
    failure: {
      error: 'Invalid credentials'
    }
  },
  
  jobs: {
    list: [
      {
        jobId: 'job-1',
        title: 'Test Job 1',
        status: 'completed',
        repository: 'test-repo',
        createdAt: '2024-01-01T10:00:00Z'
      },
      {
        jobId: 'job-2',
        title: 'Test Job 2',
        status: 'running',
        repository: 'test-repo',
        createdAt: '2024-01-01T11:00:00Z'
      }
    ],
    
    single: {
      jobId: 'job-1',
      title: 'Test Job 1',
      status: 'completed',
      repository: 'test-repo',
      output: 'Mock job output',
      exitCode: 0,
      createdAt: '2024-01-01T10:00:00Z',
      completedAt: '2024-01-01T10:05:00Z'
    },
    
    create: {
      jobId: 'new-job-id',
      status: 'created',
      title: 'New Test Job',
      user: 'testuser',
      cowPath: '/tmp/cow-workspace'
    }
  },
  
  repositories: {
    list: [
      {
        name: 'test-repo',
        gitUrl: 'https://github.com/test/repo.git',
        status: 'ready',
        lastPull: '2024-01-01T10:00:00Z',
        description: 'Test repository'
      }
    ],
    
    register: {
      name: 'new-repo',
      status: 'cloning',
      message: 'Repository registration started'
    }
  }
}

/**
 * Common test assertions
 */
export const assertions = {
  async expectJobStatusBadge(page, status) {
    const badge = page.locator('[data-testid="status-badge"]')
    await expect(badge).toHaveClass(new RegExp(`status-${status}`))
    await expect(badge).toContainText(status.replace('_', ' '))
  },
  
  async expectLoadingState(page, container = '[data-testid="loading-state"]') {
    await expect(page.locator(container)).toBeVisible()
    await expect(page.locator(`${container} .spinner`)).toBeVisible()
  },
  
  async expectErrorMessage(page, message) {
    const errorEl = page.locator('[data-testid="error-message"]')
    await expect(errorEl).toBeVisible()
    await expect(errorEl).toContainText(message)
  },
  
  async expectSuccessMessage(page, message) {
    const successEl = page.locator('[data-testid="success-message"]')
    await expect(successEl).toBeVisible()
    await expect(successEl).toContainText(message)
  }
}

/**
 * Test data generators
 */
export const generators = {
  user: (overrides = {}) => ({
    username: 'testuser',
    password: 'testpass',
    ...overrides
  }),
  
  job: (overrides = {}) => ({
    jobId: `job-${Date.now()}`,
    title: 'Test Job',
    status: 'created',
    repository: 'test-repo',
    prompt: 'Test prompt',
    createdAt: new Date().toISOString(),
    ...overrides
  }),
  
  repository: (overrides = {}) => ({
    name: 'test-repo',
    gitUrl: 'https://github.com/test/repo.git',
    status: 'ready',
    description: 'Test repository',
    ...overrides
  })
}

/**
 * Wait utilities
 */
export const waitFor = {
  async elementToBeVisible(page, selector, timeout = 10000) {
    await expect(page.locator(selector)).toBeVisible({ timeout })
  },
  
  async elementToContainText(page, selector, text, timeout = 10000) {
    await expect(page.locator(selector)).toContainText(text, { timeout })
  },
  
  async networkIdle(page, timeout = 5000) {
    await page.waitForLoadState('networkidle', { timeout })
  }
}