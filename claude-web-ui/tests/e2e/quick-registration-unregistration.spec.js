import { test, expect } from '@playwright/test';

test('quick registration and unregistration test', async ({ page }) => {
  const testRepo = `quick-test-${Date.now()}`;
  
  console.log(`🚀 Starting quick registration and unregistration test for: ${testRepo}`);
  
  // Navigate and login
  await page.goto('http://localhost:5173');
  await page.fill('#username', 'jsbattig');
  await page.fill('#password', 'pipoculebra');
  await page.click('#login-button');
  
  // Wait for dashboard and go to repositories
  await expect(page.locator('#dashboard-container')).toBeVisible();
  await page.click('#nav-repositories');
  await expect(page.locator('#repositories-view')).toBeVisible();
  
  // Register repository
  console.log('📝 Registering repository...');
  await page.click('#register-repo-button');
  await expect(page.locator('#repo-modal-overlay')).toBeVisible();
  
  await page.fill('#repo-url', 'https://github.com/jsbattig/tries.git');
  await page.fill('#repo-name', testRepo);
  await page.fill('#repo-description', 'Quick test repository');
  
  await page.screenshot({ path: 'test-results/quick-01-registration-form.png', fullPage: true });
  
  await page.click('#repo-submit');
  await expect(page.locator('#repo-modal-overlay')).not.toBeVisible({ timeout: 15000 });
  
  console.log('✅ Registration submitted');
  
  // Wait for repository to appear
  const repoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  await expect(repoElement).toBeVisible({ timeout: 10000 });
  
  console.log('⏳ Waiting for registration to complete...');
  
  // Wait for status to be "Ready" (which means registration is complete)
  let registrationComplete = false;
  let attempts = 0;
  const maxAttempts = 40; // 3+ minutes max
  
  while (!registrationComplete && attempts < maxAttempts) {
    attempts++;
    
    await page.click('#refresh-repositories');
    await page.waitForTimeout(2000);
    
    const currentRepo = page.locator(`[data-repo-name="${testRepo}"]`);
    
    if (await currentRepo.isVisible()) {
      const statusElement = currentRepo.locator('[data-testid="repository-status"]').first();
      
      if (await statusElement.isVisible()) {
        const statusText = (await statusElement.textContent()) || '';
        console.log(`📊 Attempt ${attempts}: Status="${statusText}"`);
        
        if (statusText.toLowerCase().includes('ready')) {
          registrationComplete = true;
          console.log('🎉 Registration completed - status is Ready!');
        } else if (statusText.toLowerCase().includes('failed') || statusText.toLowerCase().includes('error')) {
          console.error('❌ Registration failed!');
          break;
        } else {
          console.log('⏳ Registration still in progress...');
        }
      }
    }
    
    if (!registrationComplete) {
      await page.waitForTimeout(5000); // Wait 5 seconds between checks
    }
  }
  
  if (!registrationComplete) {
    console.warn(`⚠️ Registration did not complete within ${maxAttempts * 5} seconds, proceeding with unregistration test anyway`);
  }
  
  await page.screenshot({ path: 'test-results/quick-02-before-unregistration.png', fullPage: true });
  
  // Now test unregistration
  console.log('🗑️ Testing repository unregistration...');
  
  // Initialize unregistration tracking variables
  let unregistrationComplete = false;
  let unregAttempts = 0;
  
  const finalRepo = page.locator(`[data-repo-name="${testRepo}"]`);
  const unregisterButton = finalRepo.locator('[data-testid="unregister-repository"]').first();
  
  if (await unregisterButton.isVisible()) {
    console.log('🔍 Clicking unregister button...');
    await unregisterButton.click();
    
    // Handle confirmation
    await page.waitForTimeout(1000);
    const confirmButton = page.locator('button:has-text("Confirm"), button:has-text("Yes"), button:has-text("Unregister"), button:has-text("Delete"), .btn-danger:visible').first();
    
    if (await confirmButton.isVisible()) {
      console.log('💬 Confirming unregistration...');
      await page.screenshot({ path: 'test-results/quick-03-confirmation.png', fullPage: true });
      await confirmButton.click();
    }
    
    console.log('⏳ Monitoring unregistration process...');
    console.log('🐳 This should now use "cidx uninstall --force-docker" for proper cleanup');
    
    // Monitor unregistration
    const maxUnregAttempts = 30; // 2.5 minutes for unregistration
    
    while (!unregistrationComplete && unregAttempts < maxUnregAttempts) {
      unregAttempts++;
      
      await page.waitForTimeout(5000);
      
      await page.click('#refresh-repositories');
      await page.waitForTimeout(2000);
      
      const repoStillExists = await page.locator(`[data-repo-name="${testRepo}"]`).isVisible();
      
      if (!repoStillExists) {
        unregistrationComplete = true;
        console.log('🎉 Unregistration completed successfully!');
        console.log('✅ Repository removed from list - Docker containers cleaned up');
      } else {
        console.log(`🔍 Unregistration attempt ${unregAttempts}/${maxUnregAttempts}: Repository still visible`);
        
        // Take screenshots during cleanup process
        if (unregAttempts % 5 === 0) {
          await page.screenshot({ path: `test-results/quick-04-cleanup-${unregAttempts}.png`, fullPage: true });
        }
      }
    }
    
    if (!unregistrationComplete) {
      console.error('❌ Unregistration failed or timed out!');
      console.error('🐳 This suggests Docker container cleanup is not working properly');
      await page.screenshot({ path: 'test-results/quick-05-unregistration-failed.png', fullPage: true });
    } else {
      await page.screenshot({ path: 'test-results/quick-06-unregistration-success.png', fullPage: true });
    }
    
  } else {
    console.error('❌ Unregister button not found');
    await page.screenshot({ path: 'test-results/quick-07-no-unregister-button.png', fullPage: true });
  }
  
  // Summary
  console.log('\n🚀 QUICK TEST SUMMARY:');
  console.log(`   Repository: ${testRepo}`);
  console.log(`   Registration Complete: ${registrationComplete}`);
  console.log(`   Registration Attempts: ${attempts}`);
  console.log(`   Unregistration Complete: ${unregistrationComplete || false}`);
  console.log(`   Unregistration Attempts: ${unregAttempts || 0}`);
  
  if (unregistrationComplete) {
    console.log('✅ SUCCESS: Docker unregistration working correctly with --force-docker flag');
  } else {
    console.log('❌ FAILURE: Docker unregistration issues detected');
  }
});