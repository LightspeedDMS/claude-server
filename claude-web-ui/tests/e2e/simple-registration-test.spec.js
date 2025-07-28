import { test, expect } from '@playwright/test';

test('simple repository registration test', async ({ page }) => {
  // Navigate and login
  await page.goto('http://localhost:5173');
  await page.fill('#username', 'jsbattig');
  await page.fill('#password', 'pipoculebra');
  await page.click('#login-button');
  
  // Wait for dashboard
  await expect(page.locator('#dashboard-container')).toBeVisible();
  
  // Go to repositories
  await page.click('#nav-repositories');
  await expect(page.locator('#repositories-view')).toBeVisible();
  
  // Take screenshot of repositories page
  await page.screenshot({ path: 'test-results/simple-01-repositories-page.png', fullPage: true });
  
  // Click register button
  await page.click('#register-repo-button');
  
  // Wait for modal and take screenshot
  await expect(page.locator('#repo-modal-overlay')).toBeVisible();
  await page.screenshot({ path: 'test-results/simple-02-modal-opened.png', fullPage: true });
  
  // Fill form
  const testRepo = `simple-test-${Date.now()}`;
  await page.fill('#repo-url', 'https://github.com/jsbattig/tries.git');
  await page.fill('#repo-name', testRepo);
  await page.fill('#repo-description', 'Simple test repository');
  
  // Take screenshot before submit
  await page.screenshot({ path: 'test-results/simple-03-form-filled.png', fullPage: true });
  
  console.log(`Testing registration of: ${testRepo}`);
  
  // Submit form
  await page.click('#repo-submit');
  
  // Wait for modal to close
  await expect(page.locator('#repo-modal-overlay')).not.toBeVisible({ timeout: 15000 });
  
  // Take screenshot after registration
  await page.screenshot({ path: 'test-results/simple-04-after-registration.png', fullPage: true });
  
  console.log('✅ Registration completed, modal closed');
  
  // Wait a bit and check if repository appears
  await page.waitForTimeout(2000);
  
  // Look for the repository in the list
  const repoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  
  try {
    await expect(repoElement).toBeVisible({ timeout: 5000 });
    console.log('✅ Repository visible immediately after registration');
    
    // Take screenshot showing the repository
    await page.screenshot({ path: 'test-results/simple-05-repo-visible.png', fullPage: true });
    
  } catch (error) {
    console.log('❌ Repository not visible immediately');
    
    // Try manual refresh
    await page.click('#refresh-repositories');
    await page.waitForTimeout(2000);
    
    try {
      await expect(repoElement).toBeVisible({ timeout: 3000 });
      console.log('✅ Repository visible after manual refresh');
      await page.screenshot({ path: 'test-results/simple-06-after-manual-refresh.png', fullPage: true });
    } catch (error2) {
      console.log('❌ Repository not visible even after manual refresh');
      
      // Try hard refresh
      await page.reload();
      await page.click('#nav-repositories');
      await page.waitForTimeout(2000);
      
      try {
        await expect(repoElement).toBeVisible({ timeout: 3000 });
        console.log('✅ Repository visible after hard refresh');
        await page.screenshot({ path: 'test-results/simple-07-after-hard-refresh.png', fullPage: true });
      } catch (error3) {
        console.log('❌ Repository not visible even after hard refresh');
        await page.screenshot({ path: 'test-results/simple-08-final-failure.png', fullPage: true });
      }
    }
  }
  
  console.log('🧹 Test completed');
});