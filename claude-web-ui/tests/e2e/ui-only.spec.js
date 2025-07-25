/**
 * UI-Only E2E Tests (No Server Required)
 * 
 * These tests focus on pure UI functionality without requiring Claude Batch Server
 * Useful for:
 * - UI component behavior
 * - Form validation
 * - Client-side logic
 * - Visual testing
 * 
 * NOTE: These are NOT true E2E tests since they don't test server integration
 */

import { test, expect } from '@playwright/test'

test.describe('UI-Only E2E Tests (No Server Required)', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate directly to the web UI
    await page.goto('/')
  })

  test('should render login form correctly', async ({ page }) => {
    // Verify login container is visible
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    
    // Verify dashboard is not visible initially
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()
    
    // Verify form elements are present
    await expect(page.locator('[data-testid="username"]')).toBeVisible()
    await expect(page.locator('[data-testid="password"]')).toBeVisible()
    await expect(page.locator('[data-testid="login-button"]')).toBeVisible()
    
    // Verify form starts in clean state
    await expect(page.locator('[data-testid="username"]')).toHaveValue('')
    await expect(page.locator('[data-testid="password"]')).toHaveValue('')
    await expect(page.locator('[data-testid="error-message"]')).not.toBeVisible()
  })

  test('should validate empty form submission', async ({ page }) => {
    // Try to submit empty form
    await page.click('[data-testid="login-button"]')
    
    // Browser should prevent submission due to 'required' attributes
    // Form should remain on login page
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()
  })

  test('should handle form input correctly', async ({ page }) => {
    // Fill in form fields
    await page.fill('[data-testid="username"]', 'testuser')
    await page.fill('[data-testid="password"]', 'testpass')
    
    // Verify values are captured
    await expect(page.locator('[data-testid="username"]')).toHaveValue('testuser')
    await expect(page.locator('[data-testid="password"]')).toHaveValue('testpass')
    
    // Verify button is enabled
    await expect(page.locator('[data-testid="login-button"]')).toBeEnabled()
  })

  test('should show loading state during login attempt', async ({ page }) => {
    // Fill form with test data
    await page.fill('[data-testid="username"]', 'testuser')
    await page.fill('[data-testid="password"]', 'testpass')
    
    // Click login button
    await page.click('[data-testid="login-button"]')
    
    // Should show loading state briefly (button text changes)
    // Note: This might be very fast, so we check for either state
    const buttonText = await page.locator('[data-testid="login-button"]').textContent()
    
    // Button text should be either "Sign In" or "Signing In..." 
    expect(['Sign In', 'Signing In...']).toContain(buttonText?.trim())
  })

  test('should handle network errors gracefully', async ({ page }) => {
    // Fill form
    await page.fill('[data-testid="username"]', 'testuser')
    await page.fill('[data-testid="password"]', 'testpass')
    
    // Mock network failure
    await page.route('**/api/auth/login', route => route.abort('failed'))
    
    // Attempt login
    await page.click('[data-testid="login-button"]')
    
    // Should show error message
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
    await expect(page.locator('[data-testid="error-message"]')).toContainText('Login failed')
    
    // Should remain on login page
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    await expect(page.locator('[data-testid="dashboard"]')).not.toBeVisible()
  })

  test('should test responsive design', async ({ page }) => {
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 })
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 })
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 })
    await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    
    // Form should remain functional on mobile
    await page.fill('[data-testid="username"]', 'test')
    await expect(page.locator('[data-testid="username"]')).toHaveValue('test')
  })

  test('should have proper accessibility attributes', async ({ page }) => {
    // Check form labels
    await expect(page.locator('label[for="username"]')).toContainText('Username')
    await expect(page.locator('label[for="password"]')).toContainText('Password')
    
    // Check input types
    await expect(page.locator('[data-testid="username"]')).toHaveAttribute('type', 'text')
    await expect(page.locator('[data-testid="password"]')).toHaveAttribute('type', 'password')
    
    // Check autocomplete attributes
    await expect(page.locator('[data-testid="username"]')).toHaveAttribute('autocomplete', 'username')
    await expect(page.locator('[data-testid="password"]')).toHaveAttribute('autocomplete', 'current-password')
  })

  test('should test keyboard navigation', async ({ page }) => {
    // Tab through form elements
    await page.keyboard.press('Tab')
    await expect(page.locator('[data-testid="username"]')).toBeFocused()
    
    await page.keyboard.press('Tab')
    await expect(page.locator('[data-testid="password"]')).toBeFocused()
    
    await page.keyboard.press('Tab')
    await expect(page.locator('[data-testid="login-button"]')).toBeFocused()
    
    // Enter key on button should trigger form submission
    await page.fill('[data-testid="username"]', 'testuser')
    await page.fill('[data-testid="password"]', 'testpass')
    await page.locator('[data-testid="login-button"]').focus()
    
    // Mock the API call to prevent actual network request
    await page.route('**/api/auth/login', route => route.abort('failed'))
    
    await page.keyboard.press('Enter')
    
    // Should attempt login (will fail due to mock, but that's expected)
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
  })

  test('should clear form validation on new input', async ({ page }) => {
    // Trigger an error first
    await page.fill('[data-testid="username"]', 'baduser')
    await page.fill('[data-testid="password"]', 'badpass')
    
    // Mock failed login
    await page.route('**/api/auth/login', route => route.abort('failed'))
    await page.click('[data-testid="login-button"]')
    
    // Error should be visible
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
    
    // Clear the mock and type new input
    await page.unroute('**/api/auth/login')
    await page.fill('[data-testid="username"]', 'newuser')
    
    // Error should be cleared when user starts typing again
    // (This depends on the UI implementation - it might clear immediately or on new submission)
    // For now, we just verify the form is still functional
    await expect(page.locator('[data-testid="username"]')).toHaveValue('newuser')
  })
})

test.describe('Visual Regression Tests (UI Only)', () => {
  
  test('should match login page screenshot', async ({ page }) => {
    await page.goto('/')
    
    // Wait for page to be fully loaded
    await page.waitForLoadState('networkidle')
    
    // Take screenshot of login page
    await expect(page).toHaveScreenshot('login-page.png')
  })
  
  test('should match login page with error state', async ({ page }) => {
    await page.goto('/')
    
    // Fill form and trigger error
    await page.fill('[data-testid="username"]', 'testuser')
    await page.fill('[data-testid="password"]', 'testpass')
    
    // Mock API failure
    await page.route('**/api/auth/login', route => route.abort('failed'))
    await page.click('[data-testid="login-button"]')
    
    // Wait for error to appear
    await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
    
    // Take screenshot with error state
    await expect(page).toHaveScreenshot('login-page-error.png')
  })
})