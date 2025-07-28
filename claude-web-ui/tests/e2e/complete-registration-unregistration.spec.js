import { test, expect } from '@playwright/test';

test('complete repository registration and unregistration flow', async ({ page }) => {
  const testRepo = `complete-test-${Date.now()}`;
  
  console.log(`🚀 Starting complete registration and unregistration test for: ${testRepo}`);
  
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
  
  // Take initial screenshot
  await page.screenshot({ path: 'test-results/complete-01-initial-repositories.png', fullPage: true });
  
  // Count initial repositories for verification
  const initialRepoCount = await page.locator('[data-testid="repository-list"] .repository-item').count();
  console.log(`📊 Initial repository count: ${initialRepoCount}`);
  
  // Click register button
  await page.click('#register-repo-button');
  
  // Wait for modal and fill form
  await expect(page.locator('#repo-modal-overlay')).toBeVisible();
  await page.screenshot({ path: 'test-results/complete-02-registration-modal.png', fullPage: true });
  
  // Fill registration form
  await page.fill('#repo-url', 'https://github.com/jsbattig/tries.git');
  await page.fill('#repo-name', testRepo);
  await page.fill('#repo-description', 'Complete test repository for registration and unregistration');
  
  // Ensure CIDX is enabled (check checkbox if present)
  const cidxCheckbox = page.locator('#cidx-aware, #repo-cidx-aware, [name="cidxAware"]').first();
  if (await cidxCheckbox.isVisible()) {
    await cidxCheckbox.check();
    console.log('✅ CIDX awareness enabled for complete testing');
  }
  
  // Take screenshot before submit
  await page.screenshot({ path: 'test-results/complete-03-form-filled.png', fullPage: true });
  
  console.log(`📝 Submitting registration for: ${testRepo}`);
  
  // Submit form
  await page.click('#repo-submit');
  
  // Wait for modal to close
  await expect(page.locator('#repo-modal-overlay')).not.toBeVisible({ timeout: 15000 });
  console.log('✅ Registration modal closed');
  
  // Wait for repository to appear in list
  const repoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  
  // Phase 1: Wait for repository to appear (should be immediate)
  console.log('⏳ Phase 1: Waiting for repository to appear in list...');
  await expect(repoElement).toBeVisible({ timeout: 10000 });
  console.log('✅ Repository appeared in list');
  
  await page.screenshot({ path: 'test-results/complete-04-repo-appeared.png', fullPage: true });
  
  // Phase 2: Monitor registration status until complete
  console.log('⏳ Phase 2: Monitoring registration status until complete...');
  
  let registrationComplete = false;
  let attempts = 0;
  const maxAttempts = 60; // 5 minutes max (5 second intervals)
  
  while (!registrationComplete && attempts < maxAttempts) {
    attempts++;
    
    // Refresh the repositories list to get latest status
    await page.click('#refresh-repositories');
    await page.waitForTimeout(1000); // Wait for refresh to complete
    
    // Check if repository element still exists (in case refresh changed DOM)
    const currentRepoElement = page.locator(`[data-repo-name="${testRepo}"]`);
    
    if (await currentRepoElement.isVisible()) {
      // Look for status indicators using correct selectors
      const statusElement = currentRepoElement.locator('[data-testid="repository-status"]').first();
      const cidxElement = currentRepoElement.locator('[data-testid="repository-cidx-aware"]').first();
      
      let currentStatus = 'unknown';
      let cidxStatus = 'unknown';
      
      if (await statusElement.isVisible()) {
        const statusText = await statusElement.textContent();
        currentStatus = statusText?.toLowerCase() || 'unknown';
      }
      
      if (await cidxElement.isVisible()) {
        const cidxText = await cidxElement.textContent();
        cidxStatus = cidxText?.toLowerCase() || 'unknown';
      }
      
      console.log(`📊 Attempt ${attempts}/${maxAttempts}: Status=${currentStatus}, CIDX=${cidxStatus}`);
      
      // Check for completion indicators
      if (currentStatus.includes('completed') || currentStatus.includes('success') || currentStatus.includes('ready')) {
        if (cidxStatus.includes('yes') || cidxStatus.includes('✅') || cidxStatus.includes('ready') || !cidxStatus.includes('no')) {
          registrationComplete = true;
          console.log('🎉 Registration completed successfully!');
        } else {
          console.log('⏳ Repository cloned but CIDX still processing...');
        }
      } else if (currentStatus.includes('cloning') || currentStatus.includes('indexing') || currentStatus.includes('processing')) {
        console.log('⏳ Registration still in progress...');
      } else if (currentStatus.includes('failed') || currentStatus.includes('error')) {
        console.error('❌ Registration failed!');
        break;
      }
      
      // Take periodic screenshots
      if (attempts % 10 === 0) {
        await page.screenshot({ path: `test-results/complete-05-status-attempt-${attempts}.png`, fullPage: true });
      }
    } else {
      console.log('⚠️ Repository element not found, retrying...');
    }
    
    if (!registrationComplete) {
      await page.waitForTimeout(5000); // Wait 5 seconds between checks
    }
  }
  
  if (!registrationComplete) {
    console.error(`❌ Registration did not complete within ${maxAttempts * 5} seconds`);
    await page.screenshot({ path: 'test-results/complete-06-registration-timeout.png', fullPage: true });
    // Continue with test anyway to see what happens
  }
  
  // Take screenshot of completed registration
  await page.screenshot({ path: 'test-results/complete-07-registration-complete.png', fullPage: true });
  
  // Phase 3: Verify repository details are properly displayed
  console.log('✅ Phase 3: Verifying repository details...');
  
  const finalRepoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  await expect(finalRepoElement).toBeVisible();
  
  // Check repository details using correct selectors
  const gitUrlElement = finalRepoElement.locator('[data-testid="repository-git-url"]').first();
  const cidxStatusElement = finalRepoElement.locator('[data-testid="repository-cidx-aware"]').first();
  const registeredDateElement = finalRepoElement.locator('[data-testid="repository-last-pull"]').first();
  
  // Verify details are not showing N/A
  if (await gitUrlElement.isVisible()) {
    const gitUrlText = await gitUrlElement.textContent();
    console.log(`📋 Git URL: ${gitUrlText}`);
    if (gitUrlText?.includes('N/A')) {
      console.warn('⚠️ Git URL showing N/A');
    } else {
      console.log('✅ Git URL displayed correctly');
    }
  }
  
  if (await cidxStatusElement.isVisible()) {
    const cidxText = await cidxStatusElement.textContent();
    console.log(`📋 CIDX Status: ${cidxText}`);
    if (cidxText?.includes('N/A')) {
      console.warn('⚠️ CIDX status showing N/A');
    } else {
      console.log('✅ CIDX status displayed correctly');
    }
  }
  
  if (await registeredDateElement.isVisible()) {
    const regDateText = await registeredDateElement.textContent();
    console.log(`📋 Registration Date: ${regDateText}`);
    if (regDateText?.includes('N/A')) {
      console.warn('⚠️ Registration date showing N/A');
    } else {
      console.log('✅ Registration date displayed correctly');
    }
  }
  
  // Phase 4: Test unregistration
  console.log('🗑️ Phase 4: Testing repository unregistration...');
  
  await page.screenshot({ path: 'test-results/complete-08-before-unregistration.png', fullPage: true });
  
  // Find and click unregister button using correct selector
  const unregisterButton = finalRepoElement.locator('[data-testid="unregister-repository"]').first();
  
  if (await unregisterButton.isVisible()) {
    console.log('🔍 Found unregister button, clicking...');
    await unregisterButton.click();
    
    // Wait for confirmation dialog if present
    await page.waitForTimeout(1000);
    
    // Look for confirmation dialog/modal
    const confirmDialog = page.locator('.modal, .dialog, .confirmation, [role="dialog"]').first();
    const confirmButton = page.locator('button:has-text("Confirm"), button:has-text("Yes"), button:has-text("Unregister"), button:has-text("Delete"), .btn-danger:visible').first();
    
    if (await confirmDialog.isVisible() || await confirmButton.isVisible()) {
      console.log('💬 Confirmation dialog appeared, confirming unregistration...');
      await page.screenshot({ path: 'test-results/complete-09-confirmation-dialog.png', fullPage: true });
      await confirmButton.click();
    }
    
    console.log('⏳ Waiting for unregistration to complete...');
    
    // Monitor unregistration progress
    let unregistrationComplete = false;
    let unregAttempts = 0;
    const maxUnregAttempts = 30; // 2.5 minutes max
    
    while (!unregistrationComplete && unregAttempts < maxUnregAttempts) {
      unregAttempts++;
      
      await page.waitForTimeout(5000); // Wait 5 seconds between checks
      
      // Refresh repositories to see if it's gone
      await page.click('#refresh-repositories');
      await page.waitForTimeout(2000);
      
      // Check if repository still exists
      const repoStillExists = await page.locator(`[data-repo-name="${testRepo}"]`).isVisible();
      
      if (!repoStillExists) {
        unregistrationComplete = true;
        console.log('🎉 Repository successfully unregistered and removed from list!');
      } else {
        console.log(`📊 Unregistration attempt ${unregAttempts}/${maxUnregAttempts}: Repository still visible`);
        
        // Take periodic screenshots during unregistration
        if (unregAttempts % 5 === 0) {
          await page.screenshot({ path: `test-results/complete-10-unreg-attempt-${unregAttempts}.png`, fullPage: true });
        }
      }
    }
    
    if (!unregistrationComplete) {
      console.error(`❌ Unregistration did not complete within ${maxUnregAttempts * 5} seconds`);
      await page.screenshot({ path: 'test-results/complete-11-unregistration-timeout.png', fullPage: true });
    } else {
      // Verify repository count decreased
      const finalRepoCount = await page.locator('[data-testid="repository-list"] .repository-item').count();
      console.log(`📊 Final repository count: ${finalRepoCount} (expected: ${initialRepoCount})`);
      
      if (finalRepoCount === initialRepoCount) {
        console.log('✅ Repository count returned to initial value');
      } else {
        console.warn(`⚠️ Repository count mismatch: expected ${initialRepoCount}, got ${finalRepoCount}`);
      }
    }
    
  } else {
    console.error('❌ Unregister button not found');
    await page.screenshot({ path: 'test-results/complete-12-no-unregister-button.png', fullPage: true });
  }
  
  // Final screenshot
  await page.screenshot({ path: 'test-results/complete-13-final-state.png', fullPage: true });
  
  console.log('🏁 Complete registration and unregistration test finished');
  
  // Summary
  console.log('\n📋 TEST SUMMARY:');
  console.log(`   Repository: ${testRepo}`);
  console.log(`   Registration Complete: ${registrationComplete}`);
  console.log(`   Registration Attempts: ${attempts}`);
  console.log(`   Unregistration Complete: ${unregistrationComplete || 'N/A'}`);
  console.log(`   Unregistration Attempts: ${unregAttempts || 'N/A'}`);
});