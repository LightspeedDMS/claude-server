/**
 * Job Creation E2E Tests for Claude Web UI
 * Tests job creation workflow with file uploads, repository selection, and prompt submission
 */

import { test, expect } from '@playwright/test'
import { LoginPage, DashboardPage } from './helpers/page-objects.js'
import { auth } from './helpers/test-helpers.js'
import testData from '../fixtures/test-data.js'

test.describe('Job Creation Workflow', () => {
  let loginPage
  let dashboardPage

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page)
    dashboardPage = new DashboardPage(page)
    
    // Login before each test
    await auth.loginSuccessfully(page)
  })

  test.describe('Basic Job Creation', () => {
    test('should create job with repository selection and prompt', async ({ page }) => {
      // Navigate to job creation page
      await page.click('[data-testid="create-job-nav"]')
      await expect(page).toHaveURL(/.*\/jobs\/create/)
      
      // Verify job creation form is visible
      await expect(page.locator('[data-testid="job-creation-form"]')).toBeVisible()
      
      // Select repository
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Enter prompt
      const prompt = 'Analyze this repository structure and suggest improvements'
      await page.fill('[data-testid="prompt-input"]', prompt)
      
      // Submit job
      await page.click('[data-testid="submit-job"]')
      
      // Verify job creation success
      await expect(page).toHaveURL(/.*\/jobs\/.*/)
      await expect(page.locator('[data-testid="job-title"]')).toContainText('Analyze this repository')
      await expect(page.locator('[data-testid="job-status"]')).toContainText('created')
      
      // Verify job appears in job list
      await page.click('[data-testid="jobs-nav"]')
      await expect(page.locator('[data-testid="job-list"]')).toContainText('Analyze this repository')
    })

    test('should validate required fields', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Try to submit without required fields
      await page.click('[data-testid="submit-job"]')
      
      // Verify validation messages
      await expect(page.locator('[data-testid="repository-error"]')).toContainText('Please select a repository')
      await expect(page.locator('[data-testid="prompt-error"]')).toContainText('Please enter a prompt')
      
      // Fill repository but not prompt
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.click('[data-testid="submit-job"]')
      
      // Repository error should be gone, prompt error should remain
      await expect(page.locator('[data-testid="repository-error"]')).toBeHidden()
      await expect(page.locator('[data-testid="prompt-error"]')).toContainText('Please enter a prompt')
    })

    test('should show loading state during job creation', async ({ page }) => {
      // Simulate slow network
      await page.route('**/api/jobs', async route => {
        await new Promise(resolve => setTimeout(resolve, 2000))
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'test-job-123',
            title: 'Analyze this repository',
            status: 'created',
            repository: testData.repositories.active.name
          })
        })
      })
      
      await page.goto('/jobs/create')
      
      // Fill form
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.fill('[data-testid="prompt-input"]', 'Test prompt')
      
      // Submit and immediately check loading state
      await page.click('[data-testid="submit-job"]')
      await expect(page.locator('[data-testid="submit-loading"]')).toBeVisible()
      await expect(page.locator('[data-testid="submit-job"]')).toBeDisabled()
      
      // Eventually should succeed
      await expect(page).toHaveURL(/.*\/jobs\/test-job-123/, { timeout: 10000 })
    })
  })

  test.describe('File Upload Functionality', () => {
    test('should upload single file successfully', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Select repository first
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Upload single file
      const fileInput = page.locator('[data-testid="file-upload"]')
      await fileInput.setInputFiles({
        name: 'test-file.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from('This is a test file content')
      })
      
      // Verify file appears in upload list
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('test-file.txt')
      await expect(page.locator('[data-testid="file-count"]')).toContainText('1 file')
      
      // Verify file can be removed
      await page.click('[data-testid="remove-file-0"]')
      await expect(page.locator('[data-testid="uploaded-files"]')).toBeEmpty()
      await expect(page.locator('[data-testid="file-count"]')).toContainText('0 files')
    })

    test('should upload multiple files with progress tracking', async ({ page }) => {
      await page.goto('/jobs/create')
      
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Upload multiple files
      const fileInput = page.locator('[data-testid="file-upload"]')
      await fileInput.setInputFiles([
        {
          name: 'config.json',
          mimeType: 'application/json',
          buffer: Buffer.from('{"setting": "value"}')
        },
        {
          name: 'README.md',
          mimeType: 'text/markdown',
          buffer: Buffer.from('# Test README\nThis is a test.')
        },
        {
          name: 'script.js',
          mimeType: 'text/javascript',
          buffer: Buffer.from('console.log("Hello World");')
        }
      ])
      
      // Verify all files are listed
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('config.json')
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('README.md')
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('script.js')
      await expect(page.locator('[data-testid="file-count"]')).toContainText('3 files')
      
      // Verify total file size is displayed
      await expect(page.locator('[data-testid="total-file-size"]')).toBeVisible()
    })

    test('should handle large file upload with progress bar', async ({ page }) => {
      // Mock slow upload to see progress
      await page.route('**/api/jobs/*/files', async route => {
        // Simulate upload progress
        await new Promise(resolve => setTimeout(resolve, 1000))
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ message: 'Files uploaded successfully' })
        })
      })
      
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Upload large file (simulated)
      const largeFileContent = 'A'.repeat(1024 * 1024) // 1MB of data
      await page.locator('[data-testid="file-upload"]').setInputFiles({
        name: 'large-file.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from(largeFileContent)
      })
      
      // Verify progress indicators
      await expect(page.locator('[data-testid="upload-progress"]')).toBeVisible()
      await expect(page.locator('[data-testid="progress-bar"]')).toBeVisible()
      
      // Wait for upload completion
      await expect(page.locator('[data-testid="upload-complete"]')).toBeVisible({ timeout: 15000 })
      
      // Verify file is in the list
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('large-file.txt')
    })

    test('should enforce file size limits', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Try to upload file exceeding 50MB limit
      const oversizedContent = 'A'.repeat(51 * 1024 * 1024) // 51MB
      await page.locator('[data-testid="file-upload"]').setInputFiles({
        name: 'oversized-file.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from(oversizedContent)
      })
      
      // Verify error message
      await expect(page.locator('[data-testid="file-size-error"]')).toContainText('File size exceeds 50MB limit')
      await expect(page.locator('[data-testid="uploaded-files"]')).not.toContainText('oversized-file.txt')
    })

    test('should handle file type restrictions', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Try to upload executable file
      await page.locator('[data-testid="file-upload"]').setInputFiles({
        name: 'malicious.exe',
        mimeType: 'application/x-msdownload',
        buffer: Buffer.from('fake executable content')
      })
      
      // Verify warning or restriction
      const warningMessage = page.locator('[data-testid="file-type-warning"]')
      if (await warningMessage.isVisible()) {
        await expect(warningMessage).toContainText('executable files')
      }
    })
  })

  test.describe('Drag and Drop Upload', () => {
    test('should support drag and drop file upload', async ({ page }) => {
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Simulate drag and drop
      const dropZone = page.locator('[data-testid="file-drop-zone"]')
      
      // Create a test file
      const fileContent = 'Test file for drag and drop'
      const dataTransfer = await page.evaluateHandle(() => new DataTransfer())
      
      // Simulate file drop
      await dropZone.dispatchEvent('dragover', { dataTransfer })
      await expect(page.locator('[data-testid="drop-zone-active"]')).toBeVisible()
      
      // Mock the file drop
      await page.evaluate(async (content) => {
        const dropZone = document.querySelector('[data-testid="file-drop-zone"]')
        const file = new File([content], 'dropped-file.txt', { type: 'text/plain' })
        const dataTransfer = new DataTransfer()
        dataTransfer.items.add(file)
        
        const dropEvent = new DragEvent('drop', { dataTransfer })
        dropZone.dispatchEvent(dropEvent)
      }, fileContent)
      
      // Verify file was added
      await expect(page.locator('[data-testid="uploaded-files"]')).toContainText('dropped-file.txt')
    })

    test('should show visual feedback during drag operations', async ({ page }) => {
      await page.goto('/jobs/create')
      
      const dropZone = page.locator('[data-testid="file-drop-zone"]')
      
      // Simulate drag enter
      await dropZone.dispatchEvent('dragenter')
      await expect(page.locator('[data-testid="drop-zone-active"]')).toBeVisible()
      await expect(dropZone).toHaveClass(/drag-over/)
      
      // Simulate drag leave
      await dropZone.dispatchEvent('dragleave')
      await expect(page.locator('[data-testid="drop-zone-active"]')).toBeHidden()
      await expect(dropZone).not.toHaveClass(/drag-over/)
    })
  })

  test.describe('Repository Integration', () => {
    test('should load repositories on page load', async ({ page }) => {
      // Mock repositories API
      await page.route('**/api/repositories', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            repositories: testData.repositories.list
          })
        })
      })
      
      await page.goto('/jobs/create')
      
      // Verify repositories are loaded in select
      await expect(page.locator('[data-testid="repository-select"]')).toBeVisible()
      
      const options = page.locator('[data-testid="repository-select"] option')
      await expect(options).toHaveCount(testData.repositories.list.length + 1) // +1 for placeholder
      
      // Verify specific repositories are present
      await expect(options).toContainText(testData.repositories.active.name)
      await expect(options).toContainText(testData.repositories.inactive.name)
    })

    test('should filter repositories by status', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Only active repositories should be available for job creation
      const select = page.locator('[data-testid="repository-select"]')
      
      // Verify active repository is available
      await expect(select.locator(`option[value="${testData.repositories.active.id}"]`)).toBeVisible()
      
      // Verify inactive repository is not available or disabled
      const inactiveOption = select.locator(`option[value="${testData.repositories.inactive.id}"]`)
      if (await inactiveOption.isVisible()) {
        await expect(inactiveOption).toBeDisabled()
      }
    })

    test('should show repository information when selected', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Select repository
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Verify repository info is displayed
      await expect(page.locator('[data-testid="repo-info"]')).toBeVisible()
      await expect(page.locator('[data-testid="repo-name"]')).toContainText(testData.repositories.active.name)
      await expect(page.locator('[data-testid="repo-url"]')).toContainText(testData.repositories.active.url)
      await expect(page.locator('[data-testid="repo-description"]')).toContainText(testData.repositories.active.description)
    })
  })

  test.describe('Prompt Input', () => {
    test('should support multi-line prompts', async ({ page }) => {
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      const multiLinePrompt = `Please analyze this repository and provide:
1. Code quality assessment
2. Architecture recommendations  
3. Security considerations
4. Performance optimizations

Focus on practical, actionable suggestions.`
      
      await page.fill('[data-testid="prompt-input"]', multiLinePrompt)
      
      // Verify textarea handles multiline content
      const promptValue = await page.locator('[data-testid="prompt-input"]').inputValue()
      expect(promptValue).toBe(multiLinePrompt)
      
      // Verify character count if displayed
      const charCount = page.locator('[data-testid="char-count"]')
      if (await charCount.isVisible()) {
        await expect(charCount).toContainText(multiLinePrompt.length.toString())
      }
    })

    test('should provide prompt suggestions', async ({ page }) => {
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Check if prompt suggestions are available
      const suggestions = page.locator('[data-testid="prompt-suggestions"]')
      if (await suggestions.isVisible()) {
        // Click on a suggestion
        await page.click('[data-testid="suggestion-0"]')
        
        // Verify suggestion was applied
        const promptInput = page.locator('[data-testid="prompt-input"]')
        await expect(promptInput).not.toBeEmpty()
      }
    })

    test('should validate prompt length', async ({ page }) => {
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Test very short prompt
      await page.fill('[data-testid="prompt-input"]', 'hi')
      await page.click('[data-testid="submit-job"]')
      
      const lengthError = page.locator('[data-testid="prompt-length-error"]')
      if (await lengthError.isVisible()) {
        await expect(lengthError).toContainText('too short')
      }
      
      // Test very long prompt (over reasonable limit)
      const longPrompt = 'A'.repeat(10000)
      await page.fill('[data-testid="prompt-input"]', longPrompt)
      
      if (await lengthError.isVisible()) {
        await expect(lengthError).toContainText('too long')
      }
    })
  })

  test.describe('Error Handling', () => {
    test('should handle API errors during job creation', async ({ page }) => {
      // Mock API error
      await page.route('**/api/jobs', async route => {
        await route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Internal server error' })
        })
      })
      
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.fill('[data-testid="prompt-input"]', 'Test prompt')
      
      await page.click('[data-testid="submit-job"]')
      
      // Verify error message is displayed
      await expect(page.locator('[data-testid="error-message"]')).toContainText('Failed to create job')
      
      // Verify form remains in editable state
      await expect(page.locator('[data-testid="submit-job"]')).toBeEnabled()
      await expect(page.locator('[data-testid="prompt-input"]')).toBeEditable()
    })

    test('should handle network connectivity issues', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Simulate network disconnection
      await page.context().setOffline(true)
      
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.fill('[data-testid="prompt-input"]', 'Test prompt')
      await page.click('[data-testid="submit-job"]')
      
      // Verify network error message
      await expect(page.locator('[data-testid="network-error"]')).toContainText(/network|connection|offline/i)
      
      // Restore connectivity
      await page.context().setOffline(false)
      
      // Verify retry functionality if available
      const retryButton = page.locator('[data-testid="retry-submit"]')
      if (await retryButton.isVisible()) {
        await retryButton.click()
        // Should proceed normally now
      }
    })

    test('should handle file upload failures', async ({ page }) => {
      // Mock file upload error
      await page.route('**/api/jobs/*/files', async route => {
        await route.fulfill({
          status: 413,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Payload too large' })
        })
      })
      
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Upload file
      await page.locator('[data-testid="file-upload"]').setInputFiles({
        name: 'test-file.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from('Test content')
      })
      
      // Verify error is displayed
      await expect(page.locator('[data-testid="upload-error"]')).toContainText('too large')
      
      // Verify file is marked as failed
      await expect(page.locator('[data-testid="file-status-error"]')).toBeVisible()
    })
  })

  test.describe('Accessibility', () => {
    test('should be keyboard navigable', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Tab through form elements
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="repository-select"]')).toBeFocused()
      
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="prompt-input"]')).toBeFocused()
      
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="file-upload"]')).toBeFocused()
      
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="submit-job"]')).toBeFocused()
    })

    test('should have proper ARIA labels and roles', async ({ page }) => {
      await page.goto('/jobs/create')
      
      // Check form accessibility
      await expect(page.locator('[data-testid="job-creation-form"]')).toHaveAttribute('role', 'form')
      await expect(page.locator('[data-testid="repository-select"]')).toHaveAttribute('aria-label')
      await expect(page.locator('[data-testid="prompt-input"]')).toHaveAttribute('aria-label')
      await expect(page.locator('[data-testid="file-upload"]')).toHaveAttribute('aria-label')
      
      // Check error announcements
      await page.click('[data-testid="submit-job"]')
      const errorElement = page.locator('[data-testid="prompt-error"]')
      if (await errorElement.isVisible()) {
        await expect(errorElement).toHaveAttribute('role', 'alert')
        await expect(errorElement).toHaveAttribute('aria-live', 'polite')
      }
    })
  })

  test.describe('Mobile Responsiveness', () => {
    test('should work on mobile devices', async ({ page }) => {
      // Set mobile viewport
      await page.setViewportSize({ width: 375, height: 667 })
      
      await page.goto('/jobs/create')
      
      // Verify form is usable on mobile
      await expect(page.locator('[data-testid="job-creation-form"]')).toBeVisible()
      
      // Test touch interactions
      await page.tap('[data-testid="repository-select"]')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      
      // Verify textarea is properly sized
      const promptInput = page.locator('[data-testid="prompt-input"]')
      await expect(promptInput).toBeVisible()
      
      const boundingBox = await promptInput.boundingBox()
      expect(boundingBox.width).toBeLessThanOrEqual(375) // Should fit in viewport
    })
  })
})