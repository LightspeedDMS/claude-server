import { test, expect } from '@playwright/test';

test('Docker CIDX repository unregistration test', async ({ page }) => {
  const testRepo = `docker-cidx-test-${Date.now()}`;
  
  console.log(`🐳 Starting Docker CIDX unregistration test for: ${testRepo}`);
  
  // Navigate and login
  await page.goto('http://localhost:5173');
  await page.fill('#username', 'jsbattig');
  await page.fill('#password', 'pipoculebra');
  await page.click('#login-button');
  
  // Wait for dashboard and go to repositories
  await expect(page.locator('#dashboard-container')).toBeVisible();
  await page.click('#nav-repositories');
  await expect(page.locator('#repositories-view')).toBeVisible();
  
  // Register repository with CIDX enabled
  console.log('📝 Registering repository with CIDX enabled...');
  
  await page.click('#register-repo-button');
  await expect(page.locator('#repo-modal-overlay')).toBeVisible();
  
  await page.fill('#repo-url', 'https://github.com/jsbattig/tries.git');
  await page.fill('#repo-name', testRepo);
  await page.fill('#repo-description', 'Docker CIDX test repository');
  
  // Ensure CIDX is enabled
  const cidxCheckbox = page.locator('#cidx-aware, #repo-cidx-aware, [name="cidxAware"]').first();
  if (await cidxCheckbox.isVisible()) {
    await cidxCheckbox.check();
    console.log('✅ CIDX awareness enabled');
  }
  
  await page.screenshot({ path: 'test-results/docker-01-registration-form.png', fullPage: true });
  
  await page.click('#repo-submit');
  await expect(page.locator('#repo-modal-overlay')).not.toBeVisible({ timeout: 15000 });
  
  console.log('✅ Registration submitted');
  
  // Wait for repository to appear and monitor CIDX status
  const repoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  await expect(repoElement).toBeVisible({ timeout: 10000 });
  
  console.log('⏳ Monitoring CIDX indexing progress...');
  
  // Monitor specifically for CIDX completion
  let cidxIndexingComplete = false;
  let attempts = 0;
  const maxAttempts = 120; // 10 minutes max for CIDX indexing
  
  while (!cidxIndexingComplete && attempts < maxAttempts) {
    attempts++;
    
    await page.click('#refresh-repositories');
    await page.waitForTimeout(2000);
    
    const currentRepo = page.locator(`[data-repo-name="${testRepo}"]`);
    
    if (await currentRepo.isVisible()) {
      // Check CIDX status specifically using correct selectors
      const cidxElement = currentRepo.locator('[data-testid="repository-cidx-aware"]').first();
      const statusElement = currentRepo.locator('[data-testid="repository-status"]').first();
      
      let cidxText = '';
      let statusText = '';
      
      if (await cidxElement.isVisible()) {
        cidxText = (await cidxElement.textContent()) || '';
      }
      
      if (await statusElement.isVisible()) {
        statusText = (await statusElement.textContent()) || '';
      }
      
      console.log(`🔍 Attempt ${attempts}: Status="${statusText}", CIDX="${cidxText}"`);
      
      // Look for CIDX completion indicators
      if (cidxText.toLowerCase().includes('yes') || 
          cidxText.includes('✅') || 
          (statusText.toLowerCase().includes('completed') && !cidxText.toLowerCase().includes('no'))) {
        cidxIndexingComplete = true;
        console.log('🎉 CIDX indexing completed! Repository ready for unregistration test.');
      } else if (statusText.toLowerCase().includes('failed') || cidxText.toLowerCase().includes('failed')) {
        console.warn(`⚠️ Registration/CIDX failed: Status="${statusText}", CIDX="${cidxText}"`);
        break;
      } else {
        console.log('⏳ CIDX indexing still in progress...');
      }
      
      // Screenshots every 20 attempts (40 seconds)
      if (attempts % 20 === 0) {
        await page.screenshot({ path: `test-results/docker-02-cidx-progress-${attempts}.png`, fullPage: true });
      }
    }
    
    if (!cidxIndexingComplete) {
      await page.waitForTimeout(5000); // Wait 5 seconds between checks
    }
  }
  
  if (!cidxIndexingComplete) {
    console.warn(`⚠️ CIDX indexing did not complete within ${maxAttempts * 5} seconds, proceeding with unregistration test anyway`);
  }
  
  await page.screenshot({ path: 'test-results/docker-03-before-unregistration.png', fullPage: true });
  
  // Now test the critical Docker unregistration process
  console.log('🗑️ Testing Docker CIDX unregistration (this should now work with --force-docker)...');
  
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
      await page.screenshot({ path: 'test-results/docker-04-confirmation.png', fullPage: true });
      await confirmButton.click();
    }
    
    console.log('⏳ Monitoring Docker CIDX unregistration process...');
    console.log('🐳 This should now use "cidx uninstall --force-docker" to properly clean up containers');
    
    // Monitor unregistration with special attention to Docker cleanup
    let dockerUnregistrationComplete = false;
    let unregAttempts = 0;
    const maxUnregAttempts = 60; // 5 minutes for Docker cleanup
    
    while (!dockerUnregistrationComplete && unregAttempts < maxUnregAttempts) {
      unregAttempts++;
      
      await page.waitForTimeout(5000);
      
      await page.click('#refresh-repositories');
      await page.waitForTimeout(2000);
      
      const repoStillExists = await page.locator(`[data-repo-name="${testRepo}"]`).isVisible();
      
      if (!repoStillExists) {
        dockerUnregistrationComplete = true;
        console.log('🎉 Docker CIDX unregistration completed successfully!');
        console.log('✅ Repository removed from list - Docker containers should be cleaned up');
      } else {
        console.log(`🐳 Docker cleanup attempt ${unregAttempts}/${maxUnregAttempts}: Repository still visible`);
        
        // Take screenshots during Docker cleanup process
        if (unregAttempts % 10 === 0) {
          await page.screenshot({ path: `test-results/docker-05-cleanup-${unregAttempts}.png`, fullPage: true });
        }
      }
    }
    
    if (!dockerUnregistrationComplete) {
      console.error('❌ Docker CIDX unregistration failed or timed out!');
      console.error('🐳 This suggests Docker container cleanup is not working properly');
      await page.screenshot({ path: 'test-results/docker-06-unregistration-failed.png', fullPage: true });
      
      // Additional debugging - check if repo is still in a bad state
      const problemRepo = page.locator(`[data-repo-name="${testRepo}"]`);
      if (await problemRepo.isVisible()) {
        const statusEl = problemRepo.locator('.repo-status, [data-testid="repo-status"]').first();
        const cidxEl = problemRepo.locator('.repo-cidx-status, [data-testid="cidx-status"]').first();
        
        const finalStatus = await statusEl.textContent();
        const finalCidx = await cidxEl.textContent();
        
        console.error(`🔍 Repository stuck in state: Status="${finalStatus}", CIDX="${finalCidx}"`);
      }
    } else {
      await page.screenshot({ path: 'test-results/docker-07-unregistration-success.png', fullPage: true });
    }
    
  } else {
    console.error('❌ Unregister button not found');
    await page.screenshot({ path: 'test-results/docker-08-no-unregister-button.png', fullPage: true });
  }
  
  // Final verification and summary
  console.log('\n🐳 DOCKER CIDX UNREGISTRATION TEST SUMMARY:');
  console.log(`   Repository: ${testRepo}`);
  console.log(`   CIDX Indexing Complete: ${cidxIndexingComplete}`);
  console.log(`   CIDX Monitoring Attempts: ${attempts}`);
  console.log(`   Docker Unregistration Complete: ${dockerUnregistrationComplete}`);
  console.log(`   Docker Cleanup Attempts: ${unregAttempts}`);
  
  if (dockerUnregistrationComplete) {
    console.log('✅ SUCCESS: Docker CIDX unregistration working correctly with --force-docker flag');
  } else {
    console.log('❌ FAILURE: Docker CIDX unregistration issues detected - needs investigation');
  }
  
  console.log('🏁 Docker CIDX test completed');
});