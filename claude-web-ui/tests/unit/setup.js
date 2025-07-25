/**
 * Vitest setup file
 * Runs before each test file
 */

import { beforeEach, afterEach, vi } from 'vitest'

// Mock localStorage
const localStorageMock = {
  getItem: vi.fn(),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn(),
}
global.localStorage = localStorageMock

// Mock fetch
global.fetch = vi.fn()

// Reset mocks before each test
beforeEach(() => {
  vi.clearAllMocks()
  localStorageMock.getItem.mockClear()
  localStorageMock.setItem.mockClear()
  localStorageMock.removeItem.mockClear()
  localStorageMock.clear.mockClear()
})

// Cleanup after each test
afterEach(() => {
  vi.restoreAllMocks()
})

// Global test utilities
global.createMockResponse = (data, options = {}) => {
  const { status = 200, headers = {} } = options
  return Promise.resolve({
    ok: status >= 200 && status < 300,
    status,
    headers: new Map(Object.entries(headers)),
    json: () => Promise.resolve(data),
    text: () => Promise.resolve(JSON.stringify(data)),
  })
}

global.createMockError = (message, status = 500) => {
  const error = new Error(message)
  error.status = status
  return Promise.reject(error)
}