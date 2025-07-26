/**
 * Shared test utilities for E2E and unit tests
 */

import { expect } from '@playwright/test'
import { vi } from 'vitest'

/**
 * Mock localStorage for unit tests
 */
export function mockLocalStorage() {
  const store = new Map()
  
  const localStorage = {
    getItem: vi.fn((key) => store.get(key) || null),
    setItem: vi.fn((key, value) => store.set(key, value)),
    removeItem: vi.fn((key) => store.delete(key)),
    clear: vi.fn(() => store.clear()),
    length: 0,
    key: vi.fn()
  }
  
  // Mock global localStorage
  Object.defineProperty(window, 'localStorage', {
    value: localStorage,
    writable: true
  })
  
  return localStorage
}

/**
 * Mock fetch for unit tests
 */
export function mockFetch() {
  const fetch = vi.fn()
  
  // Mock global fetch
  global.fetch = fetch
  
  return fetch
}

/**
 * Create test session data
 */
export function createTestSession(overrides = {}) {
  const expires = new Date(Date.now() + 3600000).toISOString() // 1 hour from now
  
  // Create a proper JWT-like token structure with base64url encoding
  function base64urlEncode(str) {
    return btoa(str)
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=/g, '')
  }
  
  const header = base64urlEncode(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const payload = base64urlEncode(JSON.stringify({ 
    username: 'jsbattig', 
    exp: Math.floor((Date.now() + 3600000) / 1000), // 1 hour from now in seconds
    iat: Math.floor(Date.now() / 1000) 
  }))
  const signature = 'mock-signature'
  const token = `${header}.${payload}.${signature}`
  
  return {
    token,
    user: JSON.stringify({ username: 'jsbattig', id: 1 }),
    expires,
    ...overrides
  }
}

/**
 * Helper function for user login in E2E tests
 */
export async function loginAsUser(page, username = 'jsbattig', password = 'test123') {
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
      user: { username: 'jsbattig' }
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
      user: 'jsbattig',
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
    username: 'jsbattig',
    password: 'test123',
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