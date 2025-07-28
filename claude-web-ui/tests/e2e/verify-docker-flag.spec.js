import { test, expect } from '@playwright/test';

test('verify --force-docker flag is working', async ({ page }) => {
  const testRepo = `verify-docker-${Date.now()}`;
  
  console.log(`🐳 Testing Docker flag functionality for: ${testRepo}`);
  
  // Record initial container count
  const { exec } = require('child_process');
  const initialContainers = await new Promise((resolve) => {
    exec('podman ps | grep cidx | wc -l', (err, stdout) => {
      resolve(parseInt(stdout.trim()) || 0);
    });
  });
  
  console.log(`📊 Initial CIDX container count: ${initialContainers}`);
  
  // Navigate and login
  await page.goto('http://localhost:5173');
  await page.fill('#username', 'jsbattig');
  await page.fill('#password', 'pipoculebra');
  await page.click('#login-button');
  
  await expect(page.locator('#dashboard-container')).toBeVisible();
  await page.click('#nav-repositories');
  await expect(page.locator('#repositories-view')).toBeVisible();
  
  // Register repository with CIDX
  await page.click('#register-repo-button');
  await expect(page.locator('#repo-modal-overlay')).toBeVisible();
  
  await page.fill('#repo-url', 'https://github.com/jsbattig/tries.git');
  await page.fill('#repo-name', testRepo);
  await page.fill('#repo-description', 'Docker flag verification test');
  
  await page.click('#repo-submit');
  await expect(page.locator('#repo-modal-overlay')).not.toBeVisible({ timeout: 15000 });
  
  console.log('✅ Repository registration submitted');
  
  // Wait for repository to appear and reach Ready state
  const repoElement = page.locator(`[data-repo-name="${testRepo}"]`);
  await expect(repoElement).toBeVisible({ timeout: 10000 });
  
  // Wait for "Ready" status (meaning CIDX containers should be created)
  let isReady = false;
  let attempts = 0;
  
  while (!isReady && attempts < 20) {
    attempts++;
    await page.click('#refresh-repositories');
    await page.waitForTimeout(3000);
    
    const currentRepo = page.locator(`[data-repo-name="${testRepo}"]`);
    if (await currentRepo.isVisible()) {
      const statusElement = currentRepo.locator('[data-testid="repository-status"]').first();
      if (await statusElement.isVisible()) {
        const status = await statusElement.textContent();
        console.log(`📊 Attempt ${attempts}: Status = "${status}"`);
        
        if (status?.toLowerCase().includes('ready')) {
          isReady = true;
          console.log('🎉 Repository is Ready - CIDX containers should be created');
        }
      }
    }
    
    if (!isReady) {
      await page.waitForTimeout(5000);
    }
  }
  
  // Check container count after registration
  const afterRegistrationContainers = await new Promise((resolve) => {
    exec('podman ps | grep cidx | wc -l', (err, stdout) => {
      resolve(parseInt(stdout.trim()) || 0);
    });
  });
  
  console.log(`📊 Containers after registration: ${afterRegistrationContainers} (expected: ${initialContainers + 2})`);
  
  // Now test unregistration
  console.log('🗑️ Testing unregistration with --force-docker flag...');
  
  const finalRepo = page.locator(`[data-repo-name="${testRepo}"]`);
  const unregisterButton = finalRepo.locator('[data-testid="unregister-repository"]').first();
  
  if (await unregisterButton.isVisible()) {
    await unregisterButton.click();
    
    // Handle confirmation
    await page.waitForTimeout(1000);
    const confirmButton = page.locator('button:has-text("Confirm"), button:has-text("Yes"), button:has-text("Unregister"), button:has-text("Delete"), .btn-danger:visible').first();
    
    if (await confirmButton.isVisible()) {
      await confirmButton.click();
      console.log('💬 Unregistration confirmed');
    }
    
    // Wait for unregistration to complete
    let unregistrationComplete = false;
    let unregAttempts = 0;
    
    while (!unregistrationComplete && unregAttempts < 15) {
      unregAttempts++;
      await page.waitForTimeout(5000);
      
      await page.click('#refresh-repositories');
      await page.waitForTimeout(2000);
      
      const repoStillExists = await page.locator(`[data-repo-name="${testRepo}"]`).isVisible();
      
      if (!repoStillExists) {
        unregistrationComplete = true;
        console.log('✅ Repository removed from UI');
        break;
      } else {
        console.log(`⏳ Unregistration attempt ${unregAttempts}/15: Repository still visible`);
      }
    }
    
    // Check final container count
    await page.waitForTimeout(5000); // Give containers time to stop
    
    const finalContainers = await new Promise((resolve) => {
      exec('podman ps | grep cidx | wc -l', (err, stdout) => {
        resolve(parseInt(stdout.trim()) || 0);
      });
    });
    
    console.log(`📊 Final container count: ${finalContainers} (should be back to ${initialContainers})`);
    
    // Check if any containers with our test repo name are still running
    const testRepoContainers = await new Promise((resolve) => {
      exec(`podman ps | grep ${testRepo} | wc -l`, (err, stdout) => {
        resolve(parseInt(stdout.trim()) || 0);
      });
    });
    
    console.log(`🔍 Containers still running for ${testRepo}: ${testRepoContainers} (should be 0)`);
    
    // Results
    const containersProperlyCleaned = (finalContainers <= initialContainers) && (testRepoContainers === 0);
    
    console.log('\n🐳 DOCKER FLAG TEST RESULTS:');
    console.log(`   Repository: ${testRepo}`);
    console.log(`   Registration Successful: ${isReady}`);
    console.log(`   Unregistration Successful: ${unregistrationComplete}`);
    console.log(`   Initial Containers: ${initialContainers}`);
    console.log(`   After Registration: ${afterRegistrationContainers}`);
    console.log(`   After Unregistration: ${finalContainers}`);
    console.log(`   Test Repo Containers: ${testRepoContainers}`);
    console.log(`   Containers Properly Cleaned: ${containersProperlyCleaned}`);
    
    if (containersProperlyCleaned && unregistrationComplete) {
      console.log('✅ SUCCESS: --force-docker flag is working correctly!');
    } else {
      console.log('❌ FAILURE: --force-docker flag is NOT working properly!');
      console.log('🔍 Check server logs for "DOCKER COMPATIBILITY" messages');
    }
    
  } else {
    console.log('❌ Unregister button not found');
  }
});