import { test, expect } from '@playwright/test';

test.describe('Repository Registration and UI Refresh', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:5173');
    
    // Login with test credentials
    await page.fill('#username', 'jsbattig');
    await page.fill('#password', 'pipoculebra');
    await page.click('#login-button');
    
    // Wait for login to complete and dashboard to load
    await expect(page.locator('#dashboard-container')).toBeVisible();
    
    // Navigate to repositories tab
    await page.click('#nav-repositories');
    await expect(page.locator('#repositories-view')).toBeVisible();
  });

  test('should register repository and observe UI refresh behavior', async ({ page }) => {
    const testRepoName = `test-repo-${Date.now()}`;
    const testRepoUrl = 'https://github.com/jsbattig/tries.git';
    
    console.log(`Testing with repository: ${testRepoName}`);
    
    // Take screenshot of initial state
    await page.screenshot({ path: `test-results/01-initial-repositories.png`, fullPage: true });
    
    // Count initial repositories
    const initialRepos = await page.locator('[data-testid="repository-list"] .repository-item').count();
    console.log(`Initial repository count: ${initialRepos}`);
    
    // Click register repository button
    await page.click('#register-repo-button');
    
    // Wait for modal to appear
    await expect(page.locator('#repo-modal-overlay')).toBeVisible();
    
    // Fill registration form (using modal field IDs)
    await page.fill('#repo-url', testRepoUrl);
    await page.fill('#repo-name', testRepoName);
    await page.fill('#repo-description', `Test repository for UI refresh testing`);
    
    // Take screenshot before submitting
    await page.screenshot({ path: `test-results/02-registration-form.png`, fullPage: true });
    
    // Submit registration (using modal submit button ID)
    await page.click('#repo-submit');
    
    // Wait for registration to complete (look for success message or loading to disappear)
    await expect(page.locator('.loading')).not.toBeVisible({ timeout: 10000 });
    
    // Take screenshot immediately after registration
    await page.screenshot({ path: `test-results/03-immediately-after-registration.png`, fullPage: true });
    
    // Check if new repository appears in list WITHOUT refresh
    const repoAfterRegistration = page.locator(`[data-repo-name="${testRepoName}"]`);
    
    // Wait up to 5 seconds for the repository to appear
    let repoVisible = false;
    try {
      await expect(repoAfterRegistration).toBeVisible({ timeout: 5000 });
      repoVisible = true;
      console.log('✅ Repository appeared without page refresh');
    } catch (error) {
      console.log('❌ Repository did not appear without page refresh');
      repoVisible = false;
    }
    
    // Take screenshot of state after waiting
    await page.screenshot({ path: `test-results/04-after-waiting-5sec.png`, fullPage: true });
    
    // Check current repository count
    const currentRepos = await page.locator('[data-testid="repository-list"] .repository-item').count();
    console.log(`Repository count after registration: ${currentRepos}`);
    
    if (!repoVisible) {
      console.log('🔄 Repository not visible, testing manual refresh...');
      
      // Click refresh button
      await page.click('#refresh-repositories');
      await page.waitForTimeout(2000);
      
      // Take screenshot after manual refresh
      await page.screenshot({ path: `test-results/05-after-manual-refresh.png`, fullPage: true });
      
      // Check if repository appears after manual refresh
      try {
        await expect(repoAfterRegistration).toBeVisible({ timeout: 3000 });
        console.log('✅ Repository appeared after manual refresh');
      } catch (error) {
        console.log('❌ Repository still not visible after manual refresh');
      }
    }
    
    if (!repoVisible) {
      console.log('🔄 Testing hard page refresh...');
      
      // Perform hard page refresh
      await page.reload({ waitUntil: 'networkidle' });
      
      // Navigate back to repositories tab
      await page.click('#nav-repositories');
      await expect(page.locator('#repositories-view')).toBeVisible();
      
      // Take screenshot after hard refresh
      await page.screenshot({ path: `test-results/06-after-hard-refresh.png`, fullPage: true });
      
      // Check if repository appears after hard refresh
      try {
        await expect(repoAfterRegistration).toBeVisible({ timeout: 3000 });
        console.log('✅ Repository appeared after hard page refresh');
      } catch (error) {
        console.log('❌ Repository still not visible after hard page refresh');
      }
    }
    
    // Test repository details display
    if (await repoAfterRegistration.isVisible()) {
      console.log('📋 Checking repository details...');
      
      // Check if all expected fields are visible and not showing "N/A"
      const repoItem = repoAfterRegistration;
      
      // Look for Git URL
      const gitUrlElement = repoItem.locator('.repo-git-url');
      if (await gitUrlElement.isVisible()) {
        const gitUrlText = await gitUrlElement.textContent();
        console.log(`Git URL: ${gitUrlText}`);
        if (gitUrlText.includes('N/A')) {
          console.log('❌ Git URL showing N/A');
        } else {
          console.log('✅ Git URL displaying correctly');
        }
      }
      
      // Look for CIDX status
      const cidxElement = repoItem.locator('.repo-cidx-status');
      if (await cidxElement.isVisible()) {
        const cidxText = await cidxElement.textContent();
        console.log(`CIDX Status: ${cidxText}`);
        if (cidxText.includes('N/A')) {
          console.log('❌ CIDX status showing N/A');
        } else {
          console.log('✅ CIDX status displaying correctly');
        }
      }
      
      // Look for registration date
      const regDateElement = repoItem.locator('.repo-registered-date');
      if (await regDateElement.isVisible()) {
        const regDateText = await regDateElement.textContent();
        console.log(`Registration Date: ${regDateText}`);
        if (regDateText.includes('N/A')) {
          console.log('❌ Registration date showing N/A');
        } else {
          console.log('✅ Registration date displaying correctly');
        }
      }
    }
    
    // Take final screenshot
    await page.screenshot({ path: `test-results/07-final-state.png`, fullPage: true });
    
    // Clean up: unregister the test repository
    if (await repoAfterRegistration.isVisible()) {
      console.log('🧹 Cleaning up test repository...');
      
      // Find and click unregister button for this repository
      const unregisterButton = repoAfterRegistration.locator('.unregister-btn, [data-action="unregister"]');
      if (await unregisterButton.isVisible()) {
        await unregisterButton.click();
        
        // Handle confirmation dialog if present
        const confirmButton = page.locator('button:has-text("Confirm"), button:has-text("Yes"), button:has-text("Unregister")');
        if (await confirmButton.isVisible()) {
          await confirmButton.click();
        }
        
        // Wait for unregistration to complete
        await page.waitForTimeout(3000);
        
        // Take screenshot after cleanup
        await page.screenshot({ path: `test-results/08-after-cleanup.png`, fullPage: true });
        
        // Verify repository is removed
        try {
          await expect(repoAfterRegistration).not.toBeVisible({ timeout: 5000 });
          console.log('✅ Repository successfully unregistered');
        } catch (error) {
          console.log('❌ Repository still visible after unregistration');
        }
      } else {
        console.log('⚠️ Unregister button not found');
      }
    }
  });

  test('should test repository list refresh and caching behavior', async ({ page }) => {
    console.log('🔄 Testing repository list refresh behavior...');
    
    // Initial load
    await page.screenshot({ path: `test-results/cache-01-initial.png`, fullPage: true });
    
    // Click refresh button multiple times and observe behavior
    console.log('Testing manual refresh...');
    await page.click('#refresh-repositories');
    await page.waitForTimeout(1000);
    await page.screenshot({ path: `test-results/cache-02-after-refresh-1.png`, fullPage: true });
    
    await page.click('#refresh-repositories');
    await page.waitForTimeout(1000);
    await page.screenshot({ path: `test-results/cache-03-after-refresh-2.png`, fullPage: true });
    
    // Test navigation away and back
    console.log('Testing navigation refresh...');
    await page.click('#nav-jobs');
    await expect(page.locator('#jobs-view')).toBeVisible();
    await page.waitForTimeout(500);
    
    await page.click('#nav-repositories');
    await expect(page.locator('#repositories-view')).toBeVisible();
    await page.screenshot({ path: `test-results/cache-04-after-navigation.png`, fullPage: true });
    
    console.log('✅ Refresh behavior test completed');
  });
});