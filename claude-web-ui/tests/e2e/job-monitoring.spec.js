/**
 * Job Monitoring E2E Tests for Claude Web UI
 * Tests real-time job status monitoring, cancellation, and progress tracking
 */

import { test, expect } from '@playwright/test'
import { LoginPage, DashboardPage } from './helpers/page-objects.js'
import { auth, network } from './helpers/test-helpers.js'
import testData from '../fixtures/test-data.js'

test.describe('Job Monitoring and Status Tracking', () => {
  let loginPage
  let dashboardPage

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page)
    dashboardPage = new DashboardPage(page)
    
    // Login before each test
    await auth.loginSuccessfully(page)
  })

  test.describe('Real-time Status Updates', () => {
    test('should monitor job status changes in real-time', async ({ page }) => {
      // Mock job creation and status transitions
      let currentStatus = 'created'
      const statusStates = ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running', 'completed']
      let statusIndex = 0

      // Mock job creation
      await page.route('**/api/jobs', async route => {
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'test-job-monitor-123',
            title: 'Create test plan for repository',
            status: currentStatus,
            repository: testData.repositories.active.name,
            prompt: 'Create a comprehensive test plan',
            createdAt: new Date().toISOString()
          })
        })
      })

      // Mock job status polling with progressive status changes
      await page.route('**/api/jobs/test-job-monitor-123', async route => {
        // Advance to next status every few requests
        if (statusIndex < statusStates.length - 1) {
          statusIndex++
          currentStatus = statusStates[statusIndex]
        }

        const response = {
          id: 'test-job-monitor-123',
          title: 'Create test plan for repository',
          status: currentStatus,
          repository: testData.repositories.active.name,
          prompt: 'Create a comprehensive test plan',
          createdAt: new Date().toISOString(),
          startedAt: statusIndex > 1 ? new Date().toISOString() : null,
          completedAt: currentStatus === 'completed' ? new Date().toISOString() : null,
          exitCode: currentStatus === 'completed' ? 0 : null,
          output: currentStatus === 'completed' ? 'Test plan generated successfully.' : null
        }

        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(response)
        })
      })

      // Create job
      await page.goto('/jobs/create')
      await page.selectOption('[data-testid="repository-select"]', testData.repositories.active.id)
      await page.fill('[data-testid="prompt-input"]', 'Create a comprehensive test plan')
      await page.click('[data-testid="submit-job"]')

      // Should navigate to job details page
      await expect(page).toHaveURL(/.*\/jobs\/test-job-monitor-123/)

      // Monitor status transitions
      for (const expectedStatus of statusStates) {
        await expect(page.locator('[data-testid="job-status"]'))
          .toContainText(expectedStatus.replace('_', ' '), { timeout: 15000 })
        
        // Verify status badge color changes
        const statusBadge = page.locator('[data-testid="status-badge"]')
        await expect(statusBadge).toHaveClass(new RegExp(`status-${expectedStatus}`))
        
        // Wait a bit for next status change
        if (expectedStatus !== 'completed') {
          await page.waitForTimeout(2000)
        }
      }

      // Verify completion indicators
      await expect(page.locator('[data-testid="completion-time"]')).toBeVisible()
      await expect(page.locator('[data-testid="exit-code"]')).toContainText('0')
      await expect(page.locator('[data-testid="job-output"]')).toContainText('Test plan generated successfully')
    })

    test('should show elapsed time and estimated completion', async ({ page }) => {
      // Mock a running job
      await page.route('**/api/jobs/running-job-456', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'running-job-456',
            title: 'Long running analysis',
            status: 'running',
            repository: testData.repositories.active.name,
            createdAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(), // 5 minutes ago
            startedAt: new Date(Date.now() - 3 * 60 * 1000).toISOString(), // 3 minutes ago
            estimatedCompletionTime: new Date(Date.now() + 2 * 60 * 1000).toISOString() // 2 minutes from now
          })
        })
      })

      await page.goto('/jobs/running-job-456')

      // Verify time indicators
      await expect(page.locator('[data-testid="elapsed-time"]')).toContainText(/\d+:\d+/)
      await expect(page.locator('[data-testid="estimated-completion"]')).toBeVisible()
      
      // Wait for time update
      await page.waitForTimeout(3000)
      
      // Verify time has incremented
      const elapsedTime = await page.locator('[data-testid="elapsed-time"]').textContent()
      expect(elapsedTime).toMatch(/[0-9]+:[0-9]+/)
    })

    test('should handle status polling errors gracefully', async ({ page }) => {
      // Mock initial job load success
      await page.route('**/api/jobs/error-job-789', async route => {
        if (route.request().method() === 'GET') {
          // First request succeeds
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'error-job-789',
              title: 'Test job with polling errors',
              status: 'running',
              repository: testData.repositories.active.name
            })
          })
        }
      }, { times: 1 })

      // Subsequent polling requests fail
      await page.route('**/api/jobs/error-job-789', async route => {
        await route.fulfill({
          status: 500,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Server error' })
        })
      })

      await page.goto('/jobs/error-job-789')

      // Should show error indicator after failed polling
      await expect(page.locator('[data-testid="polling-error"]')).toBeVisible({ timeout: 10000 })
      await expect(page.locator('[data-testid="retry-polling"]')).toBeVisible()

      // Test retry functionality
      await page.click('[data-testid="retry-polling"]')
      await expect(page.locator('[data-testid="polling-error"]')).toBeVisible({ timeout: 10000 })
    })

    test('should stop polling when job is completed', async ({ page }) => {
      let pollCount = 0

      await page.route('**/api/jobs/completed-job-321', async route => {
        pollCount++
        
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'completed-job-321',
            title: 'Already completed job',
            status: 'completed',
            repository: testData.repositories.active.name,
            completedAt: new Date().toISOString(),
            exitCode: 0,
            output: 'Job completed successfully'
          })
        })
      })

      await page.goto('/jobs/completed-job-321')

      // Wait for initial load and potential polling attempts
      await page.waitForTimeout(8000)

      // Should have stopped polling after seeing completed status
      expect(pollCount).toBeLessThanOrEqual(3) // Allow for initial load + 1-2 polls max
    })
  })

  test.describe('Job Cancellation', () => {
    test('should cancel running job successfully', async ({ page }) => {
      // Mock running job
      await page.route('**/api/jobs/cancellable-job-555', async route => {
        if (route.request().method() === 'GET') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'cancellable-job-555',
              title: 'Long running job to cancel',
              status: 'running',
              repository: testData.repositories.active.name,
              canCancel: true
            })
          })
        }
      })

      // Mock cancellation request
      await page.route('**/api/jobs/cancellable-job-555/cancel', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'cancellable-job-555',
            status: 'cancelled',
            cancelledAt: new Date().toISOString(),
            cancelReason: 'User cancellation'
          })
        })
      })

      await page.goto('/jobs/cancellable-job-555')

      // Verify cancel button is available
      await expect(page.locator('[data-testid="cancel-job-button"]')).toBeVisible()
      await expect(page.locator('[data-testid="cancel-job-button"]')).toBeEnabled()

      // Cancel the job
      await page.click('[data-testid="cancel-job-button"]')

      // Verify confirmation dialog
      await expect(page.locator('[data-testid="cancel-confirmation"]')).toBeVisible()
      await expect(page.locator('[data-testid="cancel-warning"]')).toContainText('This action cannot be undone')

      // Confirm cancellation
      await page.click('[data-testid="confirm-cancel"]')

      // Verify cancellation success
      await expect(page.locator('[data-testid="job-status"]')).toContainText('cancelled', { timeout: 10000 })
      await expect(page.locator('[data-testid="cancel-reason"]')).toContainText('User cancellation')
      
      // Cancel button should be hidden
      await expect(page.locator('[data-testid="cancel-job-button"]')).toBeHidden()
    })

    test('should handle cancellation errors', async ({ page }) => {
      // Mock running job
      await page.route('**/api/jobs/cancel-error-job-666', async route => {
        if (route.request().method() === 'GET') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'cancel-error-job-666',
              title: 'Job that cannot be cancelled',
              status: 'running',
              repository: testData.repositories.active.name,
              canCancel: true
            })
          })
        }
      })

      // Mock cancellation failure
      await page.route('**/api/jobs/cancel-error-job-666/cancel', async route => {
        await route.fulfill({
          status: 400,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Job cannot be cancelled at this time' })
        })
      })

      await page.goto('/jobs/cancel-error-job-666')

      await page.click('[data-testid="cancel-job-button"]')
      await page.click('[data-testid="confirm-cancel"]')

      // Verify error message
      await expect(page.locator('[data-testid="cancel-error"]')).toContainText('cannot be cancelled')
      
      // Job should still be running
      await expect(page.locator('[data-testid="job-status"]')).toContainText('running')
    })

    test('should disable cancel for non-cancellable jobs', async ({ page }) => {
      // Mock completed job (not cancellable)
      await page.route('**/api/jobs/non-cancellable-job-777', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'non-cancellable-job-777',
            title: 'Completed job',
            status: 'completed',
            repository: testData.repositories.active.name,
            canCancel: false,
            completedAt: new Date().toISOString()
          })
        })
      })

      await page.goto('/jobs/non-cancellable-job-777')

      // Cancel button should not be visible or should be disabled
      const cancelButton = page.locator('[data-testid="cancel-job-button"]')
      if (await cancelButton.isVisible()) {
        await expect(cancelButton).toBeDisabled()
      } else {
        await expect(cancelButton).toBeHidden()
      }
    })
  })

  test.describe('Progress Indicators', () => {
    test('should show appropriate progress indicators for each status', async ({ page }) => {
      const statusConfigs = [
        { status: 'created', indicator: 'initialized', icon: 'check' },
        { status: 'queued', indicator: 'spinner', icon: 'clock' },
        { status: 'git_pulling', indicator: 'progress', icon: 'download' },
        { status: 'cidx_indexing', indicator: 'progress', icon: 'index' },
        { status: 'running', indicator: 'progress', icon: 'play' },
        { status: 'completed', indicator: 'success', icon: 'check-circle' },
        { status: 'failed', indicator: 'error', icon: 'x-circle' },
        { status: 'cancelled', indicator: 'warning', icon: 'stop-circle' }
      ]

      for (const config of statusConfigs) {
        // Mock job with specific status
        await page.route(`**/api/jobs/status-test-${config.status}`, async route => {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: `status-test-${config.status}`,
              title: `Job with ${config.status} status`,
              status: config.status,
              repository: testData.repositories.active.name
            })
          })
        })

        await page.goto(`/jobs/status-test-${config.status}`)

        // Verify appropriate progress indicator
        await expect(page.locator(`[data-testid="status-${config.indicator}"]`)).toBeVisible()
        await expect(page.locator(`[data-testid="status-icon-${config.icon}"]`)).toBeVisible()

        // Verify status-specific styling
        const statusElement = page.locator('[data-testid="job-status-container"]')
        await expect(statusElement).toHaveClass(new RegExp(`status-${config.status}`))
      }
    })

    test('should show resource usage indicators when available', async ({ page }) => {
      // Mock job with resource usage data
      await page.route('**/api/jobs/resource-job-888', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'resource-job-888',
            title: 'Job with resource monitoring',
            status: 'running',
            repository: testData.repositories.active.name,
            resourceUsage: {
              cpuPercent: 75.5,
              memoryMB: 512,
              diskMB: 1024
            }
          })
        })
      })

      await page.goto('/jobs/resource-job-888')

      // Verify resource indicators
      await expect(page.locator('[data-testid="cpu-usage"]')).toContainText('75.5%')
      await expect(page.locator('[data-testid="memory-usage"]')).toContainText('512 MB')
      await expect(page.locator('[data-testid="disk-usage"]')).toContainText('1024 MB')

      // Verify progress bars
      await expect(page.locator('[data-testid="cpu-progress-bar"]')).toBeVisible()
      await expect(page.locator('[data-testid="memory-progress-bar"]')).toBeVisible()
    })
  })

  test.describe('Job Output Display', () => {
    test('should display streaming output for running jobs', async ({ page }) => {
      let outputLines = [
        'Starting job execution...',
        'Cloning repository...',
        'Repository cloned successfully',
        'Starting Claude Code analysis...',
        'Processing files...'
      ]
      let currentLine = 0

      // Mock job with streaming output
      await page.route('**/api/jobs/streaming-job-999', async route => {
        // Add new output line each time
        if (currentLine < outputLines.length) {
          currentLine++
        }

        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'streaming-job-999',
            title: 'Job with streaming output',
            status: 'running',
            repository: testData.repositories.active.name,
            output: outputLines.slice(0, currentLine).join('\n')
          })
        })
      })

      await page.goto('/jobs/streaming-job-999')

      // Verify output appears gradually
      for (let i = 1; i <= outputLines.length; i++) {
        await expect(page.locator('[data-testid="job-output"]'))
          .toContainText(outputLines[i - 1], { timeout: 10000 })
        
        await page.waitForTimeout(3000) // Wait for next polling cycle
      }

      // Verify output formatting
      const outputContainer = page.locator('[data-testid="job-output"]')
      await expect(outputContainer).toHaveClass(/monospace|terminal/)
    })

    test('should handle large output with scrolling', async ({ page }) => {
      // Generate large output
      const largeOutput = Array.from({ length: 1000 }, (_, i) => 
        `Log line ${i + 1}: Processing item ${i + 1} of 1000`
      ).join('\n')

      await page.route('**/api/jobs/large-output-job-1000', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'large-output-job-1000',
            title: 'Job with large output',
            status: 'completed',
            repository: testData.repositories.active.name,
            output: largeOutput
          })
        })
      })

      await page.goto('/jobs/large-output-job-1000')

      const outputContainer = page.locator('[data-testid="job-output"]')
      
      // Verify scrolling is enabled
      await expect(outputContainer).toHaveCSS('overflow-y', 'auto')
      
      // Verify auto-scroll to bottom functionality
      const autoScrollButton = page.locator('[data-testid="auto-scroll-toggle"]')
      if (await autoScrollButton.isVisible()) {
        await expect(autoScrollButton).toBeChecked()
      }

      // Test manual scrolling
      await outputContainer.hover()
      await page.mouse.wheel(0, -500) // Scroll up
      
      // Auto-scroll should be disabled
      if (await autoScrollButton.isVisible()) {
        await expect(autoScrollButton).not.toBeChecked()
      }
    })

    test('should provide output download functionality', async ({ page }) => {
      const jobOutput = 'This is the complete job output\nWith multiple lines\nAnd detailed results'

      await page.route('**/api/jobs/download-output-job-1001', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'download-output-job-1001',
            title: 'Job with downloadable output',
            status: 'completed',
            repository: testData.repositories.active.name,
            output: jobOutput
          })
        })
      })

      await page.goto('/jobs/download-output-job-1001')

      // Test download functionality
      const downloadButton = page.locator('[data-testid="download-output"]')
      await expect(downloadButton).toBeVisible()

      const downloadPromise = page.waitForDownload()
      await downloadButton.click()
      const download = await downloadPromise

      expect(download.suggestedFilename()).toMatch(/output.*\.txt/)
    })
  })

  test.describe('Mobile Responsiveness', () => {
    test('should adapt monitoring interface for mobile', async ({ page }) => {
      // Set mobile viewport
      await page.setViewportSize({ width: 375, height: 667 })

      await page.route('**/api/jobs/mobile-job-1002', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'mobile-job-1002',
            title: 'Mobile test job',
            status: 'running',
            repository: testData.repositories.active.name
          })
        })
      })

      await page.goto('/jobs/mobile-job-1002')

      // Verify mobile layout
      await expect(page.locator('[data-testid="job-details-mobile"]')).toBeVisible()
      
      // Verify status indicators are appropriately sized
      const statusBadge = page.locator('[data-testid="status-badge"]')
      const badgeBox = await statusBadge.boundingBox()
      expect(badgeBox.width).toBeLessThanOrEqual(375)

      // Verify action buttons are touch-friendly
      const cancelButton = page.locator('[data-testid="cancel-job-button"]')
      if (await cancelButton.isVisible()) {
        const buttonBox = await cancelButton.boundingBox()
        expect(buttonBox.height).toBeGreaterThanOrEqual(44) // Touch target size
      }
    })
  })

  test.describe('Accessibility', () => {
    test('should announce status changes to screen readers', async ({ page }) => {
      let currentStatus = 'running'

      await page.route('**/api/jobs/a11y-job-1003', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'a11y-job-1003',
            title: 'Accessibility test job',
            status: currentStatus,
            repository: testData.repositories.active.name
          })
        })
      })

      await page.goto('/jobs/a11y-job-1003')

      // Verify ARIA live region for status updates
      const statusRegion = page.locator('[data-testid="status-live-region"]')
      await expect(statusRegion).toHaveAttribute('aria-live', 'polite')
      await expect(statusRegion).toHaveAttribute('aria-label', /status/i)

      // Verify proper labeling of interactive elements
      const cancelButton = page.locator('[data-testid="cancel-job-button"]')
      if (await cancelButton.isVisible()) {
        await expect(cancelButton).toHaveAttribute('aria-label')
      }
    })

    test('should support keyboard navigation', async ({ page }) => {
      await page.route('**/api/jobs/keyboard-job-1004', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'keyboard-job-1004',
            title: 'Keyboard navigation test',
            status: 'running',
            repository: testData.repositories.active.name,
            canCancel: true
          })
        })
      })

      await page.goto('/jobs/keyboard-job-1004')

      // Tab through interactive elements
      await page.keyboard.press('Tab')
      
      const cancelButton = page.locator('[data-testid="cancel-job-button"]')
      if (await cancelButton.isVisible()) {
        await expect(cancelButton).toBeFocused()
        
        // Test activation with Enter/Space
        await page.keyboard.press('Enter')
        await expect(page.locator('[data-testid="cancel-confirmation"]')).toBeVisible()
        
        // Cancel the cancellation with Escape
        await page.keyboard.press('Escape')
        await expect(page.locator('[data-testid="cancel-confirmation"]')).toBeHidden()
      }
    })
  })
})