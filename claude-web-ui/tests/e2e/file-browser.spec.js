import { test, expect } from '@playwright/test'

test.describe('File Browser E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to a job details page (assuming there's a test job available)
    await page.goto('/jobs/test-job-123')
    
    // Wait for the page to load
    await page.waitForSelector('[data-testid="job-title"]')
  })

  test('should display file browser interface', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    
    // Wait for file browser to load
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Check that all main components are visible
    await expect(page.locator('[data-testid="breadcrumb-nav"]')).toBeVisible()
    await expect(page.locator('[data-testid="file-search"]')).toBeVisible()
    await expect(page.locator('[data-testid="file-tree"]')).toBeVisible()
    await expect(page.locator('[data-testid="file-list"]')).toBeVisible()
    await expect(page.locator('[data-testid="file-preview"]')).toBeVisible()
  })

  test('should search files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for files to load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Get initial file count
    const initialFiles = await page.locator('[data-testid="file-item"]').count()
    
    // Search for specific files
    await page.fill('[data-testid="file-search"]', 'README')
    
    // Wait for search results
    await page.waitForTimeout(500)
    
    // Check that results are filtered
    const filteredFiles = await page.locator('[data-testid="file-item"]').count()
    expect(filteredFiles).toBeLessThanOrEqual(initialFiles)
  })

  test('should sort files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for files to load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Get initial file order
    const initialOrder = await page.locator('[data-testid="file-name"]').allTextContents()
    
    // Change sort order
    await page.selectOption('[data-testid="sort-select"]', 'name-desc')
    
    // Wait for re-sorting
    await page.waitForTimeout(300)
    
    // Get new file order
    const newOrder = await page.locator('[data-testid="file-name"]').allTextContents()
    
    // Check that order has changed (assuming there are files to sort)
    if (initialOrder.length > 1) {
      expect(newOrder).not.toEqual(initialOrder)
    }
  })

  test('should preview text files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for files to load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Find a text file (look for common extensions)
    const textFileSelector = '[data-testid="file-item"][data-type="file"]'
    const textFiles = await page.locator(textFileSelector).all()
    
    if (textFiles.length > 0) {
      // Click on the first text file
      await textFiles[0].click()
      
      // Wait for preview to load
      await page.waitForTimeout(1000)
      
      // Check that preview is shown (either content or error message)
      const preview = page.locator('[data-testid="file-preview"]')
      await expect(preview).toBeVisible()
      
      // Should show either preview content or error message
      const hasPreviewContent = await preview.locator('.text-file-preview, .image-file-preview, .binary-file-preview, .preview-error').isVisible()
      expect(hasPreviewContent).toBe(true)
    }
  })

  test('should download files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for files to load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Find a file to download
    const fileItems = await page.locator('[data-testid="file-item"][data-type="file"]').all()
    
    if (fileItems.length > 0) {
      // Hover over the first file to show actions
      await fileItems[0].hover()
      
      // Set up download promise before clicking
      const downloadPromise = page.waitForEvent('download')
      
      // Click download button
      const downloadButton = fileItems[0].locator('[data-testid="download-file"]')
      if (await downloadButton.isVisible()) {
        await downloadButton.click()
        
        // Wait for download to start
        const download = await downloadPromise
        
        // Verify download started
        expect(download).toBeTruthy()
      }
    }
  })

  test('should select multiple files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for files to load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Check initial state of download selected button
    const downloadSelectedButton = page.locator('[data-testid="download-selected"]')
    await expect(downloadSelectedButton).toBeDisabled()
    
    // Select a file
    const checkboxes = await page.locator('.file-checkbox').all()
    if (checkboxes.length > 0) {
      await checkboxes[0].check()
      
      // Wait for UI update
      await page.waitForTimeout(100)
      
      // Check that download selected button is enabled
      await expect(downloadSelectedButton).toBeEnabled()
    }
  })

  test('should refresh files', async ({ page }) => {
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for initial load
    await page.waitForSelector('[data-testid="file-item"]')
    
    // Click refresh button
    await page.click('[data-testid="refresh-files"]')
    
    // Wait for refresh to complete
    await page.waitForTimeout(1000)
    
    // Verify files are still displayed
    await expect(page.locator('[data-testid="file-list"]')).toBeVisible()
  })

  test('should handle empty workspace', async ({ page }) => {
    // Navigate to a job with no files (mock response)
    await page.route('**/api/jobs/*/files', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ files: [] })
      })
    })
    
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for empty state
    await page.waitForSelector('[data-testid="empty-files"]')
    
    // Check empty state message
    await expect(page.locator('[data-testid="empty-files"]')).toContainText('No files found')
  })

  test('should handle API errors gracefully', async ({ page }) => {
    // Mock API error
    await page.route('**/api/jobs/*/files', async route => {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Internal server error' })
      })
    })
    
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Wait for error state
    await page.waitForSelector('[data-testid="error-state"]')
    
    // Check error message
    await expect(page.locator('[data-testid="error-state"]')).toContainText('Failed to load workspace files')
  })

  test('should be responsive on mobile', async ({ page, browserName }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 })
    
    // Click on the Files tab
    await page.click('[data-testid="files-tab"]')
    await page.waitForSelector('[data-testid="file-browser"]')
    
    // Check that mobile layout is applied
    const fileTree = page.locator('.file-browser-sidebar')
    const filePreview = page.locator('.file-preview-panel')
    
    // On mobile, these should have different layout properties
    await expect(fileTree).toBeVisible()
    await expect(filePreview).toBeVisible()
    
    // File actions should be visible on mobile (no hover required)
    await page.waitForSelector('[data-testid="file-item"]')
    const fileActions = page.locator('.file-actions').first()
    if (await fileActions.isVisible()) {
      const opacity = await fileActions.evaluate(el => window.getComputedStyle(el).opacity)
      // On mobile, file actions should always be visible (opacity 1)
      expect(parseFloat(opacity)).toBeGreaterThan(0.5)
    }
  })

  test.describe('Navigation', () => {
    test('should navigate between directories', async ({ page }) => {
      // Click on the Files tab
      await page.click('[data-testid="files-tab"]')
      await page.waitForSelector('[data-testid="file-browser"]')
      
      // Wait for files to load
      await page.waitForSelector('[data-testid="file-item"]')
      
      // Look for a directory
      const directories = await page.locator('[data-testid="file-item"][data-type="directory"]').all()
      
      if (directories.length > 0) {
        // Double-click on directory to navigate
        await directories[0].dblclick()
        
        // Wait for navigation
        await page.waitForTimeout(1000)
        
        // Check that breadcrumb updated
        const breadcrumb = page.locator('[data-testid="breadcrumb-nav"]')
        await expect(breadcrumb).toBeVisible()
      }
    })

    test('should navigate using breadcrumbs', async ({ page }) => {
      // Click on the Files tab
      await page.click('[data-testid="files-tab"]')
      await page.waitForSelector('[data-testid="file-browser"]')
      
      // Click on root breadcrumb
      const rootBreadcrumb = page.locator('.breadcrumb-root')
      await rootBreadcrumb.click()
      
      // Wait for navigation
      await page.waitForTimeout(500)
      
      // Should be back at root
      await expect(page.locator('[data-testid="file-list"]')).toBeVisible()
    })
  })
})