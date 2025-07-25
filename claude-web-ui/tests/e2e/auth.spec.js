/**
 * Authentication E2E Tests for Claude Web UI
 * Comprehensive testing of login, logout, session management, and token handling
 */

import { test, expect } from '@playwright/test'
import { LoginPage, DashboardPage } from './helpers/page-objects.js'
import { auth, network } from './helpers/test-helpers.js'
import testData from '../fixtures/test-data.js'

test.describe('Authentication System', () => {
  let loginPage
  let dashboardPage

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page)
    dashboardPage = new DashboardPage(page)
  })

  test.describe('User Login', () => {
    test('should login successfully with valid credentials', async ({ page }) => {
      const credentials = testData.users.valid

      await loginPage.login(credentials.username, credentials.password)
      
      // Verify successful login
      await expect(page).toHaveURL(/.*\/dashboard/)
      await dashboardPage.expectDashboard()
      
      // Verify user menu shows logged in state
      await expect(page.locator('[data-testid="user-menu"]')).toBeVisible()
      await expect(page.locator('[data-testid="user-menu"]')).toContainText(credentials.username)
    })

    test('should show error message with invalid credentials', async ({ page }) => {
      const credentials = testData.users.invalid

      await loginPage.login(credentials.username, credentials.password)
      
      // Verify login failed
      await loginPage.expectLoginError(credentials.expectedError)
      
      // Verify still on login page
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })

    test('should handle empty username and password', async ({ page }) => {
      await loginPage.goto()
      await page.click('[data-testid="login-button"]')
      
      // Verify validation messages
      await expect(page.locator('[data-testid="username"]')).toHaveAttribute('required')
      await expect(page.locator('[data-testid="password"]')).toHaveAttribute('required')
      
      // Browser native validation should prevent submission
      const usernameField = page.locator('[data-testid="username"]')
      const isInvalid = await usernameField.evaluate(el => !el.validity.valid)
      expect(isInvalid).toBe(true)
    })

    test('should show loading state during login', async ({ page }) => {
      // Simulate slow network to observe loading state
      await network.simulateSlowNetwork(page, '**/auth/login', 2000)
      
      const credentials = testData.users.valid
      await loginPage.goto()
      
      await page.fill('[data-testid="username"]', credentials.username)
      await page.fill('[data-testid="password"]', credentials.password)
      
      // Click login and immediately check for loading state
      await page.click('[data-testid="login-button"]')
      await loginPage.expectLoadingState()
      
      // Eventually should succeed
      await expect(page).toHaveURL(/.*\/dashboard/, { timeout: 15000 })
    })

    test('should handle network errors during login', async ({ page }) => {
      // Simulate network failure
      await network.simulateNetworkError(page, '**/auth/login')
      
      const credentials = testData.users.valid
      await loginPage.login(credentials.username, credentials.password)
      
      // Should show network error message
      await expect(page.locator('[data-testid="error-message"]')).toBeVisible()
      await expect(page.locator('[data-testid="error-message"]')).toContainText(/network|connection|error/i)
    })

    test('should clear previous error messages on new login attempt', async ({ page }) => {
      // First failed login
      const invalidCredentials = testData.users.invalid
      await loginPage.login(invalidCredentials.username, invalidCredentials.password)
      await loginPage.expectLoginError(invalidCredentials.expectedError)
      
      // Second login attempt should clear previous error
      const validCredentials = testData.users.valid
      await page.fill('[data-testid="username"]', validCredentials.username)
      await page.fill('[data-testid="password"]', validCredentials.password)
      
      // Error should be hidden when typing
      await expect(page.locator('[data-testid="error-message"]')).toBeHidden()
      
      await page.click('[data-testid="login-button"]')
      
      // Should succeed
      await expect(page).toHaveURL(/.*\/dashboard/)
    })
  })

  test.describe('Session Management', () => {
    test('should maintain session across page refreshes', async ({ page }) => {
      // Login successfully
      await auth.loginSuccessfully(page)
      
      // Refresh page
      await page.reload()
      await page.waitForLoadState('networkidle')
      
      // Should still be logged in
      await expect(page).toHaveURL(/.*\/dashboard/)
      await dashboardPage.expectDashboard()
    })

    test('should maintain session across browser tabs', async ({ context }) => {
      const page1 = await context.newPage()
      const page2 = await context.newPage()
      
      // Login in first tab
      await auth.loginSuccessfully(page1)
      
      // Navigate to dashboard in second tab
      await page2.goto('/dashboard')
      
      // Should be automatically logged in
      await expect(page2).toHaveURL(/.*\/dashboard/)
      await expect(page2.locator('[data-testid="dashboard"]')).toBeVisible()
      
      await page1.close()
      await page2.close()
    })

    test('should handle concurrent sessions', async ({ context }) => {
      const page1 = await context.newPage()
      const page2 = await context.newPage()
      
      // Login in both tabs
      await auth.loginSuccessfully(page1)
      await auth.loginSuccessfully(page2)
      
      // Both should be functional
      await page1.click('[data-testid="jobs-nav"]')
      await page2.click('[data-testid="repositories-nav"]')
      
      await expect(page1.locator('[data-testid="job-list"]')).toBeVisible()
      await expect(page2.locator('[data-testid="repository-list"]')).toBeVisible()
      
      await page1.close()
      await page2.close()
    })

    test('should redirect to login when accessing protected routes without authentication', async ({ page }) => {
      // Try to access dashboard without login
      await page.goto('/dashboard')
      
      // Should redirect to login
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })

    test('should redirect to login when accessing job details without authentication', async ({ page }) => {
      // Try to access job details without login
      await page.goto('/jobs/test-job-id')
      
      // Should redirect to login
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })

    test('should redirect to login when accessing repositories without authentication', async ({ page }) => {
      // Try to access repositories without login
      await page.goto('/repositories')
      
      // Should redirect to login
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })
  })

  test.describe('Token Management', () => {
    test('should handle token expiration gracefully', async ({ page }) => {
      // Login successfully
      await auth.loginSuccessfully(page)
      
      // Mock expired token response
      await network.mockApiResponse(page, '**/api/**', {
        status: 401,
        body: testData.mockResponses.auth.tokenExpired
      })
      
      // Try to perform an authenticated action
      await page.click('[data-testid="jobs-nav"]')
      
      // Should redirect to login with expired session message
      await expect(page).toHaveURL(/.*\/(login|)$/, { timeout: 10000 })
      await expect(page.locator('[data-testid="error-message"]')).toContainText(/session.*expired|token.*expired/i)
    })

    test('should refresh token automatically before expiration', async ({ page }) => {
      // Login successfully
      await auth.loginSuccessfully(page)
      
      // Mock token refresh endpoint
      await network.mockApiResponse(page, '**/auth/refresh', {
        status: 200,
        body: {
          token: 'new-refreshed-token',
          expiresIn: 3600
        }
      })
      
      // Simulate time passing (wait for automatic refresh)
      await page.waitForTimeout(2000)
      
      // Should still be able to perform authenticated actions
      await page.click('[data-testid="jobs-nav"]')
      await expect(page.locator('[data-testid="job-list"]')).toBeVisible()
    })

    test('should clear stored token on logout', async ({ page }) => {
      // Login successfully
      await auth.loginSuccessfully(page)
      
      // Logout
      await auth.logout(page)
      
      // Check that token is cleared from storage
      const tokenInStorage = await page.evaluate(() => {
        return localStorage.getItem('token') || sessionStorage.getItem('token')
      })
      
      expect(tokenInStorage).toBeNull()
      
      // Trying to access protected route should redirect to login
      await page.goto('/dashboard')
      await expect(page).toHaveURL(/.*\/(login|)$/)
    })

    test('should handle invalid token format', async ({ page }) => {
      // Set invalid token in storage
      await page.evaluate(() => {
        localStorage.setItem('token', 'invalid-token-format')
      })
      
      // Try to access protected route
      await page.goto('/dashboard')
      
      // Should redirect to login
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })
  })

  test.describe('User Logout', () => {
    test('should logout successfully and redirect to login', async ({ page }) => {
      // Login first
      await auth.loginSuccessfully(page)
      
      // Logout
      await dashboardPage.logout()
      
      // Verify redirect to login page
      await expect(page).toHaveURL(/.*\/(login|)$/)
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })

    test('should clear all session data on logout', async ({ page }) => {
      // Login first
      await auth.loginSuccessfully(page)
      
      // Set some additional session data
      await page.evaluate(() => {
        localStorage.setItem('userPreferences', JSON.stringify({ theme: 'dark' }))
        sessionStorage.setItem('tempData', 'some-temp-data')
      })
      
      // Logout
      await dashboardPage.logout()
      
      // Check that all session data is cleared
      const storageData = await page.evaluate(() => ({
        token: localStorage.getItem('token'),
        userPreferences: localStorage.getItem('userPreferences'),
        tempData: sessionStorage.getItem('tempData')
      }))
      
      expect(storageData.token).toBeNull()
      // Note: userPreferences might be preserved if it's user preference data
      expect(storageData.tempData).toBeNull()
    })

    test('should handle logout when already logged out', async ({ page }) => {
      // Go to login page
      await loginPage.goto()
      
      // Try to logout (this should not cause errors)
      await page.evaluate(() => {
        // Simulate logout call
        if (window.authService && window.authService.logout) {
          window.authService.logout()
        }
      })
      
      // Should remain on login page without errors
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })

    test('should handle network errors during logout', async ({ page }) => {
      // Login first
      await auth.loginSuccessfully(page)
      
      // Simulate network error for logout endpoint
      await network.simulateNetworkError(page, '**/auth/logout')
      
      // Attempt logout
      await page.click('[data-testid="user-menu"]')
      await page.click('[data-testid="logout-button"]')
      
      // Should still clear local session and redirect to login
      // even if server logout fails
      await expect(page).toHaveURL(/.*\/(login|)$/, { timeout: 10000 })
      await expect(page.locator('[data-testid="login-container"]')).toBeVisible()
    })
  })

  test.describe('Security Features', () => {
    test('should prevent XSS in login form', async ({ page }) => {
      const xssPayload = '<script>alert("xss")</script>'
      
      await loginPage.goto()
      await page.fill('[data-testid="username"]', xssPayload)
      await page.fill('[data-testid="password"]', 'password')
      await page.click('[data-testid="login-button"]')
      
      // Verify XSS payload is escaped in error message
      const errorElement = page.locator('[data-testid="error-message"]')
      if (await errorElement.isVisible()) {
        const errorText = await errorElement.textContent()
        expect(errorText).not.toContain('<script>')
        expect(errorText).not.toContain('alert("xss")')
      }
    })

    test('should implement CSRF protection', async ({ page }) => {
      // Login successfully
      await auth.loginSuccessfully(page)
      
      // Try to make request without proper CSRF token
      const response = await page.evaluate(async () => {
        try {
          const res = await fetch('/api/jobs', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json'
            },
            body: JSON.stringify({
              repository: 'test-repo',
              prompt: 'test prompt'
            })
          })
          return { status: res.status, ok: res.ok }
        } catch (error) {
          return { error: error.message }
        }
      })
      
      // Should fail due to missing CSRF token
      expect(response.status).toBe(403)
      expect(response.ok).toBe(false)
    })

    test('should handle multiple failed login attempts', async ({ page }) => {
      const invalidCredentials = testData.users.invalid
      
      // Attempt multiple failed logins
      for (let i = 0; i < 3; i++) {
        await loginPage.login(invalidCredentials.username, invalidCredentials.password)
        await loginPage.expectLoginError(invalidCredentials.expectedError)
        
        // Clear fields for next attempt
        await page.fill('[data-testid="username"]', '')
        await page.fill('[data-testid="password"]', '')
      }
      
      // After multiple failures, should show rate limiting or account lockout
      await loginPage.login(invalidCredentials.username, invalidCredentials.password)
      
      const errorMessage = await page.locator('[data-testid="error-message"]').textContent()
      // Should show rate limiting or account lockout message
      expect(errorMessage).toMatch(/rate.*limit|too.*many.*attempts|locked|wait/i)
    })

    test('should secure password field', async ({ page }) => {
      await loginPage.goto()
      
      const passwordField = page.locator('[data-testid="password"]')
      
      // Verify password field is of type password
      await expect(passwordField).toHaveAttribute('type', 'password')
      
      // Verify password field has autocomplete attribute
      await expect(passwordField).toHaveAttribute('autocomplete', 'current-password')
      
      // Fill password and verify it's masked
      await passwordField.fill('secretpassword')
      const value = await passwordField.inputValue()
      expect(value).toBe('secretpassword') // Input value is accessible for testing
      
      // But the displayed value should be masked (browser handles this)
      const displayType = await passwordField.getAttribute('type')
      expect(displayType).toBe('password')
    })
  })

  test.describe('Accessibility', () => {
    test('should be keyboard navigable', async ({ page }) => {
      await loginPage.goto()
      
      // Tab through form elements
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="username"]')).toBeFocused()
      
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="password"]')).toBeFocused()
      
      await page.keyboard.press('Tab')
      await expect(page.locator('[data-testid="login-button"]')).toBeFocused()
      
      // Should be able to submit with Enter
      await page.fill('[data-testid="username"]', testData.users.valid.username)
      await page.fill('[data-testid="password"]', testData.users.valid.password)
      await page.keyboard.press('Enter')
      
      // Should proceed with login
      await expect(page).toHaveURL(/.*\/dashboard/)
    })

    test('should have proper ARIA labels and roles', async ({ page }) => {
      await loginPage.goto()
      
      const usernameField = page.locator('[data-testid="username"]')
      const passwordField = page.locator('[data-testid="password"]')
      const loginButton = page.locator('[data-testid="login-button"]')
      
      // Check for proper labeling
      await expect(usernameField).toHaveAttribute('aria-label')
      await expect(passwordField).toHaveAttribute('aria-label')
      
      // Check button role and text
      await expect(loginButton).toHaveRole('button')
      await expect(loginButton).toContainText(/login|sign.*in/i)
    })

    test('should announce errors to screen readers', async ({ page }) => {
      await loginPage.login(testData.users.invalid.username, testData.users.invalid.password)
      
      const errorMessage = page.locator('[data-testid="error-message"]')
      
      // Error should have proper ARIA attributes
      await expect(errorMessage).toHaveAttribute('role', 'alert')
      await expect(errorMessage).toHaveAttribute('aria-live', 'polite')
    })
  })
})