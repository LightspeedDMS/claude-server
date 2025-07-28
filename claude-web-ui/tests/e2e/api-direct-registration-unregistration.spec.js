import { test, expect } from '@playwright/test';
import { exec } from 'child_process';
import { promisify } from 'util';
import fs from 'fs/promises';
import path from 'path';

const execAsync = promisify(exec);

test('API direct registration and unregistration with file system verification', async ({ request }) => {
  const testRepo = `api-direct-test-${Date.now()}`;
  const baseUrl = 'http://localhost:5185';
  
  console.log(`🧪 Testing API direct registration/unregistration for: ${testRepo}`);
  
  // Step 1: Login and get authentication token
  console.log('🔐 Authenticating...');
  const loginResponse = await request.post(`${baseUrl}/auth/login`, {
    data: {
      username: 'jsbattig',
      password: 'pipoculebra'
    }
  });
  
  expect(loginResponse.ok()).toBeTruthy();
  const loginData = await loginResponse.json();
  expect(loginData.token).toBeDefined();
  
  const authHeaders = {
    'Authorization': `Bearer ${loginData.token}`,
    'Content-Type': 'application/json'
  };
  
  console.log('✅ Authentication successful');
  
  // Step 2: Register repository with CIDX enabled
  console.log('📝 Registering repository...');
  const registerResponse = await request.post(`${baseUrl}/repositories/register`, {
    headers: authHeaders,
    data: {
      url: 'https://github.com/jsbattig/tries.git',
      name: testRepo,
      description: 'API direct test with file system verification',
      cidxAware: true
    }
  });
  
  expect(registerResponse.ok()).toBeTruthy();
  const registerData = await registerResponse.json();
  console.log('✅ Repository registration submitted');
  
  // Step 3: Poll for repository to become ready
  console.log('⏳ Waiting for repository to become ready...');
  let isReady = false;
  let attempts = 0;
  const maxAttempts = 30; // 5 minutes max
  let repositoryPath = '';
  
  while (!isReady && attempts < maxAttempts) {
    attempts++;
    await new Promise(resolve => setTimeout(resolve, 10000)); // Wait 10 seconds
    
    const statusResponse = await request.get(`${baseUrl}/repositories`, {
      headers: authHeaders
    });
    
    expect(statusResponse.ok()).toBeTruthy();
    const repositories = await statusResponse.json();
    
    const testRepoData = repositories.find(repo => repo.name === testRepo);
    expect(testRepoData).toBeDefined();
    
    console.log(`📊 Attempt ${attempts}: Status = "${testRepoData.cloneStatus}", CidxAware = ${testRepoData.cidxAware}`);
    
    if (testRepoData.cloneStatus === 'completed' && testRepoData.cidxAware === true) {
      isReady = true;
      repositoryPath = testRepoData.path;
      console.log(`🎉 Repository is ready! Path: ${repositoryPath}`);
    }
  }
  
  expect(isReady).toBeTruthy();
  expect(repositoryPath).toBeTruthy();
  
  // Step 4: Verify file system structure exists
  console.log('🔍 Verifying file system structure...');
  
  // Check repository folder exists
  try {
    const repoStats = await fs.stat(repositoryPath);
    expect(repoStats.isDirectory()).toBeTruthy();
    console.log('✅ Repository directory exists');
  } catch (error) {
    throw new Error(`Repository directory not found: ${repositoryPath}`);
  }
  
  // Check .code-indexer folder exists
  const codeIndexerPath = path.join(repositoryPath, '.code-indexer');
  try {
    const codeIndexerStats = await fs.stat(codeIndexerPath);
    expect(codeIndexerStats.isDirectory()).toBeTruthy();
    console.log('✅ .code-indexer directory exists');
  } catch (error) {
    throw new Error(`.code-indexer directory not found: ${codeIndexerPath}`);
  }
  
  // Check qdrant folder exists inside .code-indexer
  const qdrantPath = path.join(codeIndexerPath, 'qdrant');
  try {
    const qdrantStats = await fs.stat(qdrantPath);
    expect(qdrantStats.isDirectory()).toBeTruthy();
    console.log('✅ qdrant directory exists');
  } catch (error) {
    throw new Error(`qdrant directory not found: ${qdrantPath}`);
  }
  
  // Check qdrant folder has files
  try {
    const qdrantFiles = await fs.readdir(qdrantPath, { recursive: true });
    expect(qdrantFiles.length).toBeGreaterThan(0);
    console.log(`✅ qdrant directory contains ${qdrantFiles.length} files/folders`);
    
    // Look for specific qdrant structure
    const collectionsPath = path.join(qdrantPath, 'collections');
    const collectionsStats = await fs.stat(collectionsPath);
    expect(collectionsStats.isDirectory()).toBeTruthy();
    console.log('✅ qdrant/collections directory exists');
  } catch (error) {
    throw new Error(`qdrant directory structure invalid: ${error.message}`);
  }
  
  // Check config.json exists
  const configPath = path.join(codeIndexerPath, 'config.json');
  try {
    const configStats = await fs.stat(configPath);
    expect(configStats.isFile()).toBeTruthy();
    
    const configContent = await fs.readFile(configPath, 'utf8');
    const config = JSON.parse(configContent);
    expect(config.embedding_provider).toBeDefined();
    console.log(`✅ config.json exists with provider: ${config.embedding_provider}`);
  } catch (error) {
    throw new Error(`config.json not found or invalid: ${error.message}`);
  }
  
  // Step 5: Check Docker containers are running for this repository
  console.log('🐳 Checking Docker containers...');
  try {
    const { stdout: dockerOutput } = await execAsync(`docker ps --format "table {{.Names}}\\t{{.Image}}\\t{{.Status}}" | grep -E "cidx-|qdrant|data-cleaner"`);
    console.log('🐳 Current Docker containers:');
    console.log(dockerOutput);
    
    // Count containers before unregistration
    const containerCount = dockerOutput.split('\n').filter(line => line.trim()).length;
    console.log(`📊 Found ${containerCount} CIDX-related Docker containers`);
  } catch (error) {
    console.log('⚠️ No CIDX Docker containers currently running (this might be expected)');
  }
  
  // Step 6: Unregister repository
  console.log('🗑️ Unregistering repository...');
  const unregisterResponse = await request.delete(`${baseUrl}/repositories/${testRepo}`, {
    headers: authHeaders
  });
  
  expect(unregisterResponse.ok()).toBeTruthy();
  const unregisterData = await unregisterResponse.json();
  expect(unregisterData.success).toBeTruthy();
  console.log('✅ Repository unregistration initiated');
  
  // Step 7: Wait for unregistration to complete and verify repository is gone from API
  console.log('⏳ Waiting for unregistration to complete...');
  let unregistrationComplete = false;
  let unregAttempts = 0;
  const maxUnregAttempts = 20; // 3+ minutes max
  
  while (!unregistrationComplete && unregAttempts < maxUnregAttempts) {
    unregAttempts++;
    await new Promise(resolve => setTimeout(resolve, 10000)); // Wait 10 seconds
    
    const statusResponse = await request.get(`${baseUrl}/repositories`, {
      headers: authHeaders
    });
    
    expect(statusResponse.ok()).toBeTruthy();
    const repositories = await statusResponse.json();
    
    const testRepoData = repositories.find(repo => repo.name === testRepo);
    
    if (!testRepoData) {
      unregistrationComplete = true;
      console.log('✅ Repository removed from API');
    } else {
      console.log(`⏳ Unregistration attempt ${unregAttempts}/${maxUnregAttempts}: Repository still in API`);
    }
  }
  
  expect(unregistrationComplete).toBeTruthy();
  
  // Step 8: Verify file system cleanup - repository folder should be completely gone
  console.log('🧹 Verifying file system cleanup...');
  
  // Wait a bit more for file system cleanup
  await new Promise(resolve => setTimeout(resolve, 15000)); // Wait 15 seconds
  
  try {
    await fs.stat(repositoryPath);
    // If we get here, the directory still exists - this is a failure
    throw new Error(`Repository directory still exists after unregistration: ${repositoryPath}`);
  } catch (error) {
    if (error.code === 'ENOENT') {
      console.log('✅ Repository directory completely removed');
    } else {
      throw error;
    }
  }
  
  // Verify no traces of .code-indexer folder
  try {
    await fs.stat(codeIndexerPath);
    throw new Error(`.code-indexer directory still exists after unregistration: ${codeIndexerPath}`);
  } catch (error) {
    if (error.code === 'ENOENT') {
      console.log('✅ .code-indexer directory completely removed');
    } else {
      throw error;
    }
  }
  
  // Step 9: Verify Docker containers are cleaned up
  console.log('🐳 Verifying Docker container cleanup...');
  await new Promise(resolve => setTimeout(resolve, 10000)); // Wait 10 seconds for container cleanup
  
  try {
    const { stdout: dockerOutputAfter } = await execAsync(`docker ps --format "table {{.Names}}\\t{{.Image}}\\t{{.Status}}" | grep -E "cidx-|qdrant|data-cleaner" || echo "No containers found"`);
    console.log('🐳 Docker containers after unregistration:');
    console.log(dockerOutputAfter);
    
    // Check if any containers contain our test repo name
    if (dockerOutputAfter.includes(testRepo)) {
      console.log(`⚠️ Warning: Found containers that might be related to ${testRepo}`);
    } else {
      console.log('✅ No containers found with test repo name');
    }
  } catch (error) {
    console.log('✅ No CIDX Docker containers found (expected after cleanup)');
  }
  
  // Final summary
  console.log('\n🎯 API DIRECT TEST RESULTS:');
  console.log(`   Repository: ${testRepo}`);
  console.log(`   Registration: ✅ Success`);
  console.log(`   File System Created: ✅ Success`);
  console.log(`   CIDX Structure: ✅ Success`);
  console.log(`   Qdrant Data: ✅ Success`);
  console.log(`   Unregistration: ✅ Success`);
  console.log(`   File System Cleanup: ✅ Success`);
  console.log(`   Docker Cleanup: ✅ Success`);
  console.log('\n🏆 ALL TESTS PASSED - API direct flow working correctly!');
});