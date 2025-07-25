/**
 * Repository Management E2E Tests for Claude Web UI
 * Tests repository registration, validation, status monitoring, and CRUD operations
 */

import { test, expect } from '@playwright/test'
import { LoginPage, DashboardPage } from './helpers/page-objects.js'
import { auth, network } from './helpers/test-helpers.js'
import testData from '../fixtures/test-data.js'

test.describe('Repository Management', () => {
  let loginPage
  let dashboardPage

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page)
    dashboardPage = new DashboardPage(page)
    
    // Login before each test
    await auth.loginSuccessfully(page)
  })

  test.describe('Repository Registration', () => {
    test('should register new repository successfully', async ({ page }) => {
      // Mock successful repository registration
      await page.route('**/api/repositories', async route => {
        if (route.request().method() === 'POST') {
          const postData = await route.request().postDataJSON()
          await route.fulfill({
            status: 201,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'new-repo-123',
              name: postData.name,
              url: postData.url,
              description: postData.description,
              status: 'cloning',
              createdAt: new Date().toISOString()
            })
          })
        }
      })

      // Navigate to repositories page
      await page.click('[data-testid="repositories-nav"]')
      await expect(page).toHaveURL(/.*\/repositories/)

      // Click register new repository
      await page.click('[data-testid="register-repo-button"]')
      await expect(page.locator('[data-testid="repo-registration-form"]')).toBeVisible()

      // Fill registration form
      const repoData = testData.repositories.newRepo
      await page.fill('[data-testid="repo-name"]', repoData.name)
      await page.fill('[data-testid="repo-url"]', repoData.url)
      await page.fill('[data-testid="repo-description"]', repoData.description)

      // Submit registration
      await page.click('[data-testid="register-submit"]')

      // Verify success message and navigation
      await expect(page.locator('[data-testid="registration-success"]')).toContainText('successfully registered')
      
      // Should navigate to repository details or list
      await expect(page).toHaveURL(/.*\/repositories/)
      
      // Verify repository appears in list
      await expect(page.locator('[data-testid="repo-list"]')).toContainText(repoData.name)
      await expect(page.locator('[data-testid="repo-status-cloning"]')).toBeVisible()
    })

    test('should validate repository URL format', async ({ page }) => {
      await page.goto('/repositories')
      await page.click('[data-testid="register-repo-button"]')

      // Test invalid URLs
      const invalidUrls = [
        'not-a-url',
        'http://invalid',
        'ftp://example.com/repo.git',
        'https://example.com/not-git',
        'git@github.com:user/repo', // SSH format should be supported separately
      ]

      for (const invalidUrl of invalidUrls) {
        await page.fill('[data-testid="repo-url"]', invalidUrl)
        await page.fill('[data-testid="repo-name"]', 'test-repo')
        await page.click('[data-testid="register-submit"]')

        // Verify validation error
        await expect(page.locator('[data-testid="url-validation-error"]'))
          .toContainText(/invalid.*url|invalid.*format/i)
        
        // Clear field for next test
        await page.fill('[data-testid="repo-url"]', '')
      }

      // Test valid URLs
      const validUrls = [
        'https://github.com/user/repo.git',
        'https://gitlab.com/user/repo.git',
        'https://bitbucket.org/user/repo.git',
        'https://github.com/org/repo-name.git'
      ]

      for (const validUrl of validUrls) {
        await page.fill('[data-testid="repo-url"]', validUrl)
        
        // Validation error should disappear
        await expect(page.locator('[data-testid="url-validation-error"]')).toBeHidden()
      }
    })

    test('should validate required fields', async ({ page }) => {
      await page.goto('/repositories')
      await page.click('[data-testid="register-repo-button"]')

      // Try to submit empty form
      await page.click('[data-testid="register-submit"]')

      // Verify all required field errors
      await expect(page.locator('[data-testid="name-required-error"]')).toContainText('Name is required')
      await expect(page.locator('[data-testid="url-required-error"]')).toContainText('URL is required')

      // Fill name but not URL
      await page.fill('[data-testid="repo-name"]', 'test-repo')
      await page.click('[data-testid="register-submit"]')

      // Name error should be gone, URL error should remain
      await expect(page.locator('[data-testid="name-required-error"]')).toBeHidden()
      await expect(page.locator('[data-testid="url-required-error"]')).toContainText('URL is required')
    })

    test('should handle repository name conflicts', async ({ page }) => {
      // Mock name conflict error
      await page.route('**/api/repositories', async route => {
        if (route.request().method() === 'POST') {
          await route.fulfill({
            status: 409,
            contentType: 'application/json',
            body: JSON.stringify({ error: 'Repository name already exists' })
          })
        }
      })

      await page.goto('/repositories')
      await page.click('[data-testid="register-repo-button"]')

      await page.fill('[data-testid="repo-name"]', 'existing-repo')
      await page.fill('[data-testid="repo-url"]', 'https://github.com/user/repo.git')
      await page.click('[data-testid="register-submit"]')

      // Verify conflict error message
      await expect(page.locator('[data-testid="name-conflict-error"]')).toContainText('name already exists')
      
      // Form should remain editable
      await expect(page.locator('[data-testid="repo-name"]')).toBeEditable()
    })

    test('should show loading state during registration', async ({ page }) => {
      // Simulate slow registration
      await page.route('**/api/repositories', async route => {
        if (route.request().method() === 'POST') {
          await new Promise(resolve => setTimeout(resolve, 2000))
          await route.fulfill({
            status: 201,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'slow-repo-456',
              name: 'slow-repo',
              url: 'https://github.com/user/slow-repo.git',
              status: 'cloning'
            })
          })
        }
      })

      await page.goto('/repositories')
      await page.click('[data-testid="register-repo-button"]')

      await page.fill('[data-testid="repo-name"]', 'slow-repo')
      await page.fill('[data-testid="repo-url"]', 'https://github.com/user/slow-repo.git')

      // Submit and immediately check loading state
      await page.click('[data-testid="register-submit"]')
      await expect(page.locator('[data-testid="registration-loading"]')).toBeVisible()
      await expect(page.locator('[data-testid="register-submit"]')).toBeDisabled()

      // Eventually should succeed
      await expect(page.locator('[data-testid="registration-success"]')).toBeVisible({ timeout: 10000 })
    })
  })

  test.describe('Repository List and Status', () => {
    test('should display repository list with statuses', async ({ page }) => {
      // Mock repository list
      await page.route('**/api/repositories', async route => {
        if (route.request().method() === 'GET') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              repositories: testData.repositories.list
            })
          })
        }
      })

      await page.goto('/repositories')

      // Verify repository list loads
      await expect(page.locator('[data-testid="repo-list"]')).toBeVisible()
      
      // Check each repository appears with correct status
      for (const repo of testData.repositories.list) {
        const repoCard = page.locator(`[data-testid="repo-card-${repo.id}"]`)
        await expect(repoCard).toBeVisible()
        await expect(repoCard.locator('[data-testid="repo-name"]')).toContainText(repo.name)
        await expect(repoCard.locator('[data-testid="repo-url"]')).toContainText(repo.url)
        await expect(repoCard.locator('[data-testid="repo-status"]')).toContainText(repo.status)
        
        // Verify status-specific styling
        await expect(repoCard).toHaveClass(new RegExp(`status-${repo.status}`))
      }
    })

    test('should monitor repository status changes', async ({ page }) => {
      let currentStatus = 'cloning'
      const statusProgression = ['cloning', 'indexing', 'ready']
      let statusIndex = 0

      // Mock repository with changing status
      await page.route('**/api/repositories/status-change-repo', async route => {
        // Advance status on each request
        if (statusIndex < statusProgression.length - 1) {
          statusIndex++
          currentStatus = statusProgression[statusIndex]
        }

        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'status-change-repo',
            name: 'Status Change Test Repo',
            url: 'https://github.com/test/status-repo.git',
            status: currentStatus,
            lastUpdated: new Date().toISOString()
          })
        })
      })

      await page.goto('/repositories/status-change-repo')

      // Monitor status progression
      for (const expectedStatus of statusProgression) {
        await expect(page.locator('[data-testid="repo-status"]'))
          .toContainText(expectedStatus, { timeout: 15000 })
        
        // Verify status indicator changes
        const statusIndicator = page.locator('[data-testid="status-indicator"]')
        await expect(statusIndicator).toHaveClass(new RegExp(`status-${expectedStatus}`))
        
        if (expectedStatus !== 'ready') {
          await page.waitForTimeout(3000) // Wait for next status update
        }
      }

      // Verify final ready state
      await expect(page.locator('[data-testid="repo-ready-actions"]')).toBeVisible()
      await expect(page.locator('[data-testid="create-job-button"]')).toBeEnabled()
    })

    test('should handle repository status errors', async ({ page }) => {
      // Mock repository with error status
      await page.route('**/api/repositories/error-repo-789', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'error-repo-789',
            name: 'Error Test Repo',
            url: 'https://github.com/test/error-repo.git',
            status: 'error',
            error: 'Failed to clone repository: Authentication failed',
            lastUpdated: new Date().toISOString()
          })
        })
      })

      await page.goto('/repositories/error-repo-789')

      // Verify error status display
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('error')
      await expect(page.locator('[data-testid="repo-error-message"]')).toContainText('Authentication failed')
      
      // Verify error actions are available
      await expect(page.locator('[data-testid="retry-setup-button"]')).toBeVisible()
      await expect(page.locator('[data-testid="delete-repo-button"]')).toBeVisible()
    })

    test('should filter repositories by status', async ({ page }) => {
      await page.goto('/repositories')

      // Test status filter
      await page.selectOption('[data-testid="status-filter"]', 'ready')
      
      // Only ready repositories should be visible
      await expect(page.locator('[data-testid="repo-card"][data-status="ready"]')).toHaveCount(2)
      await expect(page.locator('[data-testid="repo-card"][data-status="cloning"]')).toHaveCount(0)

      // Test "all" filter
      await page.selectOption('[data-testid="status-filter"]', 'all')
      await expect(page.locator('[data-testid="repo-card"]')).toHaveCount(5) // Total repositories
    })

    test('should search repositories by name', async ({ page }) => {
      await page.goto('/repositories')

      // Search for specific repository
      await page.fill('[data-testid="repo-search"]', 'web-ui')
      
      // Wait for search results
      await page.waitForTimeout(500)
      
      // Only matching repositories should be visible
      await expect(page.locator('[data-testid="repo-card"]')).toHaveCount(1)
      await expect(page.locator('[data-testid="repo-name"]')).toContainText('web-ui')

      // Clear search
      await page.fill('[data-testid="repo-search"]', '')
      await page.waitForTimeout(500)
      
      // All repositories should be visible again
      await expect(page.locator('[data-testid="repo-card"]')).toHaveCount(5)
    })
  })

  test.describe('Repository Actions', () => {
    test('should delete repository with confirmation', async ({ page }) => {
      // Mock repository deletion
      await page.route('**/api/repositories/delete-test-repo', async route => {
        if (route.request().method() === 'DELETE') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ message: 'Repository deleted successfully' })
          })
        }
      })

      await page.goto('/repositories/delete-test-repo')

      // Click delete button
      await page.click('[data-testid="delete-repo-button"]')

      // Verify confirmation dialog
      await expect(page.locator('[data-testid="delete-confirmation"]')).toBeVisible()
      await expect(page.locator('[data-testid="delete-warning"]')).toContainText('This action cannot be undone')
      
      // Type repository name to confirm
      await page.fill('[data-testid="confirm-repo-name"]', 'delete-test-repo')
      await expect(page.locator('[data-testid="confirm-delete-button"]')).toBeEnabled()

      // Confirm deletion
      await page.click('[data-testid="confirm-delete-button"]')

      // Verify deletion success
      await expect(page.locator('[data-testid="deletion-success"]')).toContainText('deleted successfully')
      
      // Should redirect to repositories list
      await expect(page).toHaveURL(/.*\/repositories$/)
    })

    test('should retry failed repository setup', async ({ page }) => {
      // Mock retry request
      await page.route('**/api/repositories/retry-repo-456/retry', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'retry-repo-456',
            status: 'cloning',
            message: 'Repository setup restarted'
          })
        })
      })

      // Mock repository with error status
      await page.route('**/api/repositories/retry-repo-456', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'retry-repo-456',
            name: 'Retry Test Repo',
            status: 'error',
            error: 'Network timeout during clone'
          })
        })
      })

      await page.goto('/repositories/retry-repo-456')

      // Click retry button
      await page.click('[data-testid="retry-setup-button"]')

      // Verify retry confirmation
      await expect(page.locator('[data-testid="retry-success"]')).toContainText('setup restarted')
      
      // Status should change to cloning
      await expect(page.locator('[data-testid="repo-status"]')).toContainText('cloning')
    })

    test('should update repository settings', async ({ page }) => {
      // Mock repository update
      await page.route('**/api/repositories/update-repo-789', async route => {
        if (route.request().method() === 'PUT') {
          const updateData = await route.request().postDataJSON()
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'update-repo-789',
              name: 'Updated Repo Name',
              description: updateData.description,
              url: 'https://github.com/test/updated-repo.git',
              status: 'ready'
            })
          })
        }
      })

      await page.goto('/repositories/update-repo-789')

      // Click edit/settings button
      await page.click('[data-testid="edit-repo-button"]')
      await expect(page.locator('[data-testid="repo-edit-form"]')).toBeVisible()

      // Update description
      await page.fill('[data-testid="edit-description"]', 'Updated repository description')
      
      // Save changes
      await page.click('[data-testid="save-changes-button"]')

      // Verify success message
      await expect(page.locator('[data-testid="update-success"]')).toContainText('updated successfully')
      
      // Verify updated description is displayed
      await expect(page.locator('[data-testid="repo-description"]')).toContainText('Updated repository description')
    })

    test('should create job from repository', async ({ page }) => {
      // Mock job creation from repository
      await page.route('**/api/jobs', async route => {
        if (route.request().method() === 'POST') {
          await route.fulfill({
            status: 201,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'repo-job-123',
              title: 'Analysis job from repository',
              status: 'created',
              repository: 'ready-repo'
            })
          })
        }
      })

      await page.goto('/repositories/ready-repo-123')

      // Click create job button
      await page.click('[data-testid="create-job-button"]')

      // Should navigate to job creation with repository pre-selected
      await expect(page).toHaveURL(/.*\/jobs\/create.*/)
      
      const repoSelect = page.locator('[data-testid="repository-select"]')
      const selectedValue = await repoSelect.inputValue()
      expect(selectedValue).toBe('ready-repo-123')
    })
  })

  test.describe('Repository Details View', () => {
    test('should display comprehensive repository information', async ({ page }) => {
      // Mock detailed repository data
      await page.route('**/api/repositories/detailed-repo-999', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'detailed-repo-999',
            name: 'Detailed Test Repository',
            url: 'https://github.com/test/detailed-repo.git',
            description: 'A comprehensive test repository with detailed information',
            status: 'ready',
            createdAt: '2024-01-15T10:30:00Z',
            lastUpdated: new Date().toISOString(),
            statistics: {
              totalFiles: 156,
              totalSize: '2.4 MB',
              languages: [
                { name: 'JavaScript', percentage: 65.2 },
                { name: 'HTML', percentage: 20.1 },
                { name: 'CSS', percentage: 14.7 }
              ],
              lastCommit: {
                hash: 'abc123def456',
                message: 'Add comprehensive test suite',
                author: 'Test Author',
                date: '2024-01-20T14:22:00Z'
              }
            },
            jobs: [
              { id: 'job-1', title: 'Code review analysis', status: 'completed' },
              { id: 'job-2', title: 'Security audit', status: 'running' }
            ]
          })
        })
      })

      await page.goto('/repositories/detailed-repo-999')

      // Verify basic information
      await expect(page.locator('[data-testid="repo-name"]')).toContainText('Detailed Test Repository')
      await expect(page.locator('[data-testid="repo-description"]')).toContainText('comprehensive test repository')
      await expect(page.locator('[data-testid="repo-url"]')).toContainText('github.com/test/detailed-repo.git')

      // Verify statistics
      await expect(page.locator('[data-testid="total-files"]')).toContainText('156')
      await expect(page.locator('[data-testid="total-size"]')).toContainText('2.4 MB')

      // Verify language breakdown
      await expect(page.locator('[data-testid="language-javascript"]')).toContainText('65.2%')
      await expect(page.locator('[data-testid="language-html"]')).toContainText('20.1%')

      // Verify last commit information
      await expect(page.locator('[data-testid="last-commit-message"]')).toContainText('Add comprehensive test suite')
      await expect(page.locator('[data-testid="last-commit-author"]')).toContainText('Test Author')

      // Verify related jobs
      await expect(page.locator('[data-testid="related-jobs"]')).toContainText('Code review analysis')
      await expect(page.locator('[data-testid="related-jobs"]')).toContainText('Security audit')
    })

    test('should display repository clone and indexing progress', async ({ page }) => {
      // Mock repository in progress
      await page.route('**/api/repositories/progress-repo-888', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'progress-repo-888',
            name: 'Progress Test Repository',
            status: 'indexing',
            progress: {
              phase: 'indexing',
              currentStep: 'Analyzing code structure',
              percentage: 67,
              estimatedTimeRemaining: '2 minutes'
            }
          })
        })
      })

      await page.goto('/repositories/progress-repo-888')

      // Verify progress indicators
      await expect(page.locator('[data-testid="progress-phase"]')).toContainText('indexing')
      await expect(page.locator('[data-testid="current-step"]')).toContainText('Analyzing code structure')
      await expect(page.locator('[data-testid="progress-percentage"]')).toContainText('67%')
      await expect(page.locator('[data-testid="estimated-time"]')).toContainText('2 minutes')

      // Verify progress bar
      const progressBar = page.locator('[data-testid="progress-bar"]')
      await expect(progressBar).toBeVisible()
      const progressValue = await progressBar.getAttribute('value')
      expect(Number(progressValue)).toBe(67)
    })
  })

  test.describe('Error Handling', () => {
    test('should handle API errors gracefully', async ({ page }) => {
      // Mock API error
      await page.route('**/api/repositories', async route => {
        await route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Internal server error' })
        })
      })

      await page.goto('/repositories')

      // Verify error state
      await expect(page.locator('[data-testid="repositories-error"]')).toContainText('Failed to load repositories')
      await expect(page.locator('[data-testid="retry-load-button"]')).toBeVisible()

      // Test retry functionality
      await page.click('[data-testid="retry-load-button"]')
      await expect(page.locator('[data-testid="loading-repositories"]')).toBeVisible()
    })

    test('should handle network connectivity issues', async ({ page }) => {
      await page.goto('/repositories')

      // Simulate network disconnection
      await page.context().setOffline(true)

      // Try to register repository
      await page.click('[data-testid="register-repo-button"]')
      await page.fill('[data-testid="repo-name"]', 'offline-test')
      await page.fill('[data-testid="repo-url"]', 'https://github.com/test/offline.git')
      await page.click('[data-testid="register-submit"]')

      // Verify network error
      await expect(page.locator('[data-testid="network-error"]')).toContainText(/network|connection|offline/i)

      // Restore connectivity
      await page.context().setOffline(false)
    })
  })

  test.describe('Accessibility', () => {
    test('should be keyboard navigable', async ({ page }) => {
      await page.goto('/repositories')

      // Tab through main elements
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="register-repo-button"]')).toBeFocused()

      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="repo-search"]')).toBeFocused()

      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="status-filter"]')).toBeFocused()

      // Navigate to first repository card
      await page.keyboard.press('Tab')
      const firstRepoCard = page.locator('[data-testid="repo-card"]').first()
      await expect(firstRepoCard).toBeFocused()
    })

    test('should have proper ARIA labels and roles', async ({ page }) => {
      await page.goto('/repositories')

      // Check list accessibility
      await expect(page.locator('[data-testid="repo-list"]')).toHaveAttribute('role', 'list')
      
      // Check repository cards
      const repoCards = page.locator('[data-testid="repo-card"]')
      await expect(repoCards.first()).toHaveAttribute('role', 'listitem')
      await expect(repoCards.first()).toHaveAttribute('aria-label')

      // Check interactive elements
      await expect(page.locator('[data-testid="register-repo-button"]')).toHaveAttribute('aria-label')
      await expect(page.locator('[data-testid="repo-search"]')).toHaveAttribute('aria-label')
    })
  })

  test.describe('Mobile Responsiveness', () => {
    test('should adapt repository list for mobile', async ({ page }) => {
      // Set mobile viewport
      await page.setViewportSize({ width: 375, height: 667 })

      await page.goto('/repositories')

      // Verify mobile layout
      await expect(page.locator('[data-testid="repositories-mobile"]')).toBeVisible()
      
      // Repository cards should stack vertically
      const repoCards = page.locator('[data-testid="repo-card"]')
      const firstCardBox = await repoCards.first().boundingBox()
      const secondCardBox = await repoCards.nth(1).boundingBox()
      
      expect(firstCardBox.width).toBeLessThanOrEqual(375)
      expect(secondCardBox.y).toBeGreaterThan(firstCardBox.y + firstCardBox.height)

      // Test mobile registration form
      await page.tap('[data-testid="register-repo-button"]')
      await expect(page.locator('[data-testid="repo-registration-form"]')).toBeVisible()
      
      const formBox = await page.locator('[data-testid="repo-registration-form"]').boundingBox()
      expect(formBox.width).toBeLessThanOrEqual(375)
    })
  })
})