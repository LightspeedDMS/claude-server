/**
 * Basic integration test for repository components
 * Tests component instantiation and basic functionality
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RepositoryListComponent } from '../../src/components/repository-list.js'
import { RepositoryRegisterComponent } from '../../src/components/repository-register.js'
import { RepositoryDetailsComponent } from '../../src/components/repository-details.js'

// Mock API client
vi.mock('../../src/services/api.js', () => ({
  default: {
    getRepositories: vi.fn().mockResolvedValue([]),
    getRepository: vi.fn().mockResolvedValue({}),
    createRepository: vi.fn().mockResolvedValue({}),
    deleteRepository: vi.fn().mockResolvedValue({})
  }
}))

describe('Repository Components', () => {
  let container
  
  beforeEach(() => {
    container = document.createElement('div')
    document.body.appendChild(container)
    vi.clearAllMocks()
  })

  afterEach(() => {
    document.body.removeChild(container)
  })

  describe('RepositoryListComponent', () => {
    it('should render repository list container', () => {
      const component = new RepositoryListComponent(container)
      
      expect(container.querySelector('.repository-list-container')).toBeTruthy()
      expect(container.querySelector('.repository-list-title')).toBeTruthy()
      expect(container.querySelector('[data-testid="register-repository-button"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="refresh-repositories"]')).toBeTruthy()
    })

    it('should render filters and controls', () => {
      const component = new RepositoryListComponent(container)
      
      expect(container.querySelector('[data-testid="status-filter"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="search-repositories"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="sort-by"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="grid-view"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="list-view"]')).toBeTruthy()
    })

    it('should handle register repository callback', () => {
      const mockCallback = vi.fn()
      const component = new RepositoryListComponent(container, {
        onRegisterRepository: mockCallback
      })
      
      const registerBtn = container.querySelector('[data-testid="register-repository-button"]')
      registerBtn.click()
      
      expect(mockCallback).toHaveBeenCalled()
    })

    it('should initialize with correct default values', () => {
      const component = new RepositoryListComponent(container)
      
      expect(component.repositories).toEqual([])
      expect(component.currentFilter).toBe('all')
      expect(component.viewMode).toBe('grid')
      expect(component.sortBy).toBe('name')
      expect(component.sortOrder).toBe('asc')
    })

    it('should destroy component cleanly', () => {
      const component = new RepositoryListComponent(container)
      
      expect(() => component.destroy()).not.toThrow()
    })
  })

  describe('RepositoryRegisterComponent', () => {
    it('should render registration form', () => {
      const component = new RepositoryRegisterComponent(container)
      
      expect(container.querySelector('[data-testid="repository-register-form"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="repository-name"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="git-url"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="description"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="submit-button"]')).toBeTruthy()
    })

    it('should have submit button disabled by default', () => {
      const component = new RepositoryRegisterComponent(container)
      
      const submitBtn = container.querySelector('[data-testid="submit-button"]')
      expect(submitBtn.disabled).toBe(true)
    })

    it('should handle back button callback', () => {
      const mockCallback = vi.fn()
      const component = new RepositoryRegisterComponent(container, {
        onCancel: mockCallback
      })
      
      const backBtn = container.querySelector('[data-testid="back-button"]')
      backBtn.click()
      
      expect(mockCallback).toHaveBeenCalled()
    })

    it('should validate repository name correctly', () => {
      const component = new RepositoryRegisterComponent(container)
      
      // Test valid name
      const nameInput = container.querySelector('[data-testid="repository-name"]')
      nameInput.value = 'valid-repo-name'
      nameInput.dispatchEvent(new Event('input'))
      
      expect(component.validateName()).toBe(true)
      expect(nameInput.classList.contains('error')).toBe(false)
    })

    it('should validate git URL correctly', () => {
      const component = new RepositoryRegisterComponent(container)
      
      // Test valid HTTPS git URL
      const gitUrlInput = container.querySelector('[data-testid="git-url"]')
      gitUrlInput.value = 'https://github.com/user/repo.git'
      gitUrlInput.dispatchEvent(new Event('input'))
      
      expect(component.validateGitUrl()).toBe(true)
      expect(gitUrlInput.classList.contains('error')).toBe(false)
    })
  })

  describe('RepositoryDetailsComponent', () => {
    it('should render repository details container', () => {
      const component = new RepositoryDetailsComponent(container, 'test-repo')
      
      expect(container.querySelector('.repository-details-container')).toBeTruthy()
      expect(container.querySelector('[data-testid="repository-title"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="back-button"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="refresh-repository"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="create-job"]')).toBeTruthy()
    })

    it('should display repository name', () => {
      const component = new RepositoryDetailsComponent(container, 'my-test-repo')
      
      const titleElement = container.querySelector('[data-testid="repository-title"]')
      expect(titleElement.textContent.trim()).toBe('my-test-repo')
    })

    it('should handle back button callback', () => {
      const mockCallback = vi.fn()
      const component = new RepositoryDetailsComponent(container, 'test-repo', {
        onBack: mockCallback
      })
      
      const backBtn = container.querySelector('[data-testid="back-button"]')
      backBtn.click()
      
      expect(mockCallback).toHaveBeenCalled()
    })

    it('should handle create job callback', () => {
      const mockCallback = vi.fn()
      const component = new RepositoryDetailsComponent(container, 'test-repo', {
        onCreateJob: mockCallback
      })
      
      const createJobBtn = container.querySelector('[data-testid="create-job"]')
      createJobBtn.click()
      
      expect(mockCallback).toHaveBeenCalledWith('test-repo')
    })

    it('should format repository status correctly', () => {
      const component = new RepositoryDetailsComponent(container, 'test-repo')
      
      expect(component.formatStatus('ready')).toBe('Ready')
      expect(component.formatStatus('cloning')).toBe('Cloning')
      expect(component.formatStatus('failed')).toBe('Failed')
      expect(component.formatStatus('unknown')).toBe('Unknown')
    })

    it('should format file sizes correctly', () => {
      const component = new RepositoryDetailsComponent(container, 'test-repo')
      
      expect(component.formatSize(0)).toBe('0 B')
      expect(component.formatSize(1024)).toBe('1 KB')
      expect(component.formatSize(1024 * 1024)).toBe('1 MB')
      expect(component.formatSize(1024 * 1024 * 1024)).toBe('1 GB')
    })

    it('should destroy component cleanly', () => {
      const component = new RepositoryDetailsComponent(container, 'test-repo')
      
      expect(() => component.destroy()).not.toThrow()
    })
  })
})