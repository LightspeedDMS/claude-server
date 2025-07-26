/**
 * Debug Basic UI Behavior
 * Let's see exactly what happens when the page loads
 */

import { test, expect } from '@playwright/test'

test.describe('Debug Basic UI Behavior', () => {
  
  test('debug page load sequence', async ({ page }) => {
    console.log('🔍 Starting debug test...')
    
    // Navigate to the page
    await page.goto('/')
    console.log('✅ Page loaded')
    
    // Wait for page to be fully loaded
    await page.waitForLoadState('networkidle')
    console.log('✅ Network idle')
    
    // Check initial state immediately after load
    const loginContainerInitial = page.locator('[data-testid="login-container"]')
    const dashboardInitial = page.locator('[data-testid="dashboard"]')
    
    console.log('🔍 Checking initial visibility...')
    const loginInitiallyVisible = await loginContainerInitial.isVisible()
    const dashboardInitiallyVisible = await dashboardInitial.isVisible()
    
    console.log(`📋 Initial state: Login visible: ${loginInitiallyVisible}, Dashboard visible: ${dashboardInitiallyVisible}`)
    
    // Wait a bit for JavaScript to initialize
    await page.waitForTimeout(2000)
    console.log('✅ Waited 2 seconds for JS initialization')
    
    // Check state after JS initialization
    const loginAfterJS = await loginContainerInitial.isVisible()
    const dashboardAfterJS = await dashboardInitial.isVisible()
    
    console.log(`📋 After JS: Login visible: ${loginAfterJS}, Dashboard visible: ${dashboardAfterJS}`)
    
    // Clear any stored tokens to ensure we get login form
    await page.evaluate(() => {
      localStorage.clear()
      sessionStorage.clear()
    })
    console.log('✅ Cleared storage')
    
    // Reload and check again
    await page.reload()
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)
    
    const loginAfterReload = await loginContainerInitial.isVisible()
    const dashboardAfterReload = await dashboardInitial.isVisible()
    
    console.log(`📋 After reload: Login visible: ${loginAfterReload}, Dashboard visible: ${dashboardAfterReload}`)
    
    // Take a screenshot so we can see what's actually displayed
    await page.screenshot({ path: 'debug-ui-state.png', fullPage: true })
    console.log('📸 Screenshot saved as debug-ui-state.png')
    
    // The test should pass - we're just debugging
    expect(true).toBe(true)
  })
  
  test('debug authentication check', async ({ page }) => {
    console.log('🔍 Testing authentication behavior...')
    
    // Clear storage first
    await page.goto('/')
    await page.evaluate(() => {
      localStorage.clear()
      sessionStorage.clear()
    })
    
    await page.reload()
    await page.waitForLoadState('networkidle')
    
    // Wait for the authentication check to complete
    await page.waitForTimeout(3000)
    
    // Now check if login form is visible
    const loginContainer = page.locator('[data-testid="login-container"]')
    const dashboard = page.locator('[data-testid="dashboard"]')
    
    const loginVisible = await loginContainer.isVisible()
    const dashboardVisible = await dashboard.isVisible()
    
    console.log(`📋 Final state: Login visible: ${loginVisible}, Dashboard visible: ${dashboardVisible}`)
    
    if (loginVisible) {
      console.log('✅ Login form is visible - this is expected for UI-only tests')
      
      // Check if the form elements are there
      const username = page.locator('[data-testid="username"]')
      const password = page.locator('[data-testid="password"]')
      const loginButton = page.locator('[data-testid="login-button"]')
      
      const usernameVisible = await username.isVisible()
      const passwordVisible = await password.isVisible()
      const buttonVisible = await loginButton.isVisible()
      
      console.log(`📋 Form elements: Username: ${usernameVisible}, Password: ${passwordVisible}, Button: ${buttonVisible}`)
      
      // This should work now
      await expect(username).toBeVisible()
      await expect(password).toBeVisible()
      await expect(loginButton).toBeVisible()
      
    } else {
      console.log('❌ Login form is not visible - this might be the issue')
      
      // Take screenshot to see what's actually shown
      await page.screenshot({ path: 'debug-no-login-form.png', fullPage: true })
    }
  })
})