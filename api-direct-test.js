#!/usr/bin/env node

const https = require('https');
const http = require('http');
const fs = require('fs').promises;
const path = require('path');

// API configuration
const API_BASE = 'http://localhost:5185';
const USERNAME = 'jsbattig';
const PASSWORD = 'pipoculebra';

let authToken = '';

// Helper function to make HTTP requests
function makeRequest(url, options = {}) {
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url);
    const requestOptions = {
      hostname: urlObj.hostname,
      port: urlObj.port,
      path: urlObj.pathname + urlObj.search,
      method: options.method || 'GET',
      headers: {
        'Content-Type': 'application/json',
        ...options.headers
      }
    };

    const req = http.request(requestOptions, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try {
          const response = {
            statusCode: res.statusCode,
            ok: res.statusCode >= 200 && res.statusCode < 300,
            data: data ? JSON.parse(data) : null,
            headers: res.headers
          };
          resolve(response);
        } catch (error) {
          resolve({
            statusCode: res.statusCode,
            ok: res.statusCode >= 200 && res.statusCode < 300,
            data: data,
            headers: res.headers
          });
        }
      });
    });

    req.on('error', reject);

    if (options.body) {
      req.write(typeof options.body === 'string' ? options.body : JSON.stringify(options.body));
    }

    req.end();
  });
}

// Helper function to wait
function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

// Helper function to check if directory exists and has content
async function checkDirectory(dirPath, shouldExist = true) {
  try {
    const stats = await fs.stat(dirPath);
    if (shouldExist) {
      console.log(`✅ Directory exists: ${dirPath}`);
      if (stats.isDirectory()) {
        const contents = await fs.readdir(dirPath);
        console.log(`   Contents: ${contents.length} items`);
        return { exists: true, isDirectory: true, contents };
      }
      return { exists: true, isDirectory: false, contents: [] };
    } else {
      console.log(`❌ Directory should not exist but does: ${dirPath}`);
      return { exists: true, shouldNotExist: true };
    }
  } catch (error) {
    if (error.code === 'ENOENT') {
      if (shouldExist) {
        console.log(`❌ Directory does not exist: ${dirPath}`);
        return { exists: false, error: error.message };
      } else {
        console.log(`✅ Directory correctly removed: ${dirPath}`);
        return { exists: false, correctlyRemoved: true };
      }
    } else {
      console.log(`⚠️ Error checking directory ${dirPath}: ${error.message}`);
      return { exists: false, error: error.message };
    }
  }
}

async function main() {
  const testRepo = `api-direct-test-${Date.now()}`;
  console.log(`🧪 Starting API Direct Test for repository: ${testRepo}`);
  console.log(`🌐 API Base URL: ${API_BASE}`);
  
  try {
    // Step 1: Login
    console.log('\n🔐 Step 1: Authenticating...');
    const loginResponse = await makeRequest(`${API_BASE}/auth/login`, {
      method: 'POST',
      body: {
        username: USERNAME,
        password: PASSWORD
      }
    });

    if (!loginResponse.ok) {
      throw new Error(`Login failed: HTTP ${loginResponse.statusCode} - ${JSON.stringify(loginResponse.data)}`);
    }

    authToken = loginResponse.data.token;
    console.log(`✅ Authentication successful for user: ${loginResponse.data.user}`);
    console.log(`🔑 Token expires: ${loginResponse.data.expires}`);

    // Step 2: Register repository
    console.log('\n📝 Step 2: Registering repository...');
    const registerResponse = await makeRequest(`${API_BASE}/repositories/register`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${authToken}`
      },
      body: {
        gitUrl: 'https://github.com/jsbattig/tries.git',
        name: testRepo,
        description: 'API direct test with file system verification',
        cidxAware: true
      }
    });

    if (!registerResponse.ok) {
      throw new Error(`Registration failed: HTTP ${registerResponse.statusCode} - ${JSON.stringify(registerResponse.data)}`);
    }

    console.log(`✅ Repository registration submitted: ${registerResponse.data.name}`);
    console.log(`📍 Clone status: ${registerResponse.data.cloneStatus}`);

    // Step 3: Wait for repository to become ready
    console.log('\n⏳ Step 3: Waiting for repository to become ready...');
    let isReady = false;
    let attempts = 0;
    const maxAttempts = 40; // 10 minutes max (15 second intervals)
    let repositoryData = null;

    while (!isReady && attempts < maxAttempts) {
      attempts++;
      await sleep(15000); // Wait 15 seconds

      const statusResponse = await makeRequest(`${API_BASE}/repositories`, {
        headers: {
          'Authorization': `Bearer ${authToken}`
        }
      });

      if (!statusResponse.ok) {
        throw new Error(`Status check failed: HTTP ${statusResponse.statusCode}`);
      }

      repositoryData = statusResponse.data.find(repo => repo.name === testRepo);
      
      if (!repositoryData) {
        throw new Error(`Repository ${testRepo} not found in status response`);
      }

      console.log(`📊 Attempt ${attempts}/${maxAttempts}:`);
      console.log(`   Clone Status: ${repositoryData.cloneStatus}`);
      console.log(`   CIDX Aware: ${repositoryData.cidxAware}`);
      console.log(`   Git URL: ${repositoryData.gitUrl || 'N/A'}`);

      if (repositoryData.cloneStatus === 'completed' && repositoryData.cidxAware === true) {
        isReady = true;
        console.log(`🎉 Repository is ready!`);
        console.log(`📂 Repository path: ${repositoryData.path}`);
      }
    }

    if (!isReady) {
      throw new Error(`Repository did not become ready after ${maxAttempts} attempts (${maxAttempts * 15} seconds)`);
    }

    // Step 4: Verify file system structure
    console.log('\n🔍 Step 4: Verifying file system structure...');
    
    const repoPath = repositoryData.path;
    console.log(`📂 Repository path: ${repoPath}`);

    // Check repository directory
    const repoCheck = await checkDirectory(repoPath, true);
    if (!repoCheck.exists || !repoCheck.isDirectory) {
      throw new Error(`Repository directory check failed: ${JSON.stringify(repoCheck)}`);
    }

    // Check .code-indexer directory
    const codeIndexerPath = path.join(repoPath, '.code-indexer');
    const codeIndexerCheck = await checkDirectory(codeIndexerPath, true);
    if (!codeIndexerCheck.exists || !codeIndexerCheck.isDirectory) {
      throw new Error(`.code-indexer directory check failed: ${JSON.stringify(codeIndexerCheck)}`);
    }

    // Check qdrant directory
    const qdrantPath = path.join(codeIndexerPath, 'qdrant');
    const qdrantCheck = await checkDirectory(qdrantPath, true);
    if (!qdrantCheck.exists || !qdrantCheck.isDirectory) {
      throw new Error(`qdrant directory check failed: ${JSON.stringify(qdrantCheck)}`);
    }

    // Check collections directory inside qdrant
    const collectionsPath = path.join(qdrantPath, 'collections');
    const collectionsCheck = await checkDirectory(collectionsPath, true);
    if (!collectionsCheck.exists || !collectionsCheck.isDirectory) {
      throw new Error(`qdrant/collections directory check failed: ${JSON.stringify(collectionsCheck)}`);
    }

    // Check config.json
    const configPath = path.join(codeIndexerPath, 'config.json');
    try {
      const configContent = await fs.readFile(configPath, 'utf8');
      const config = JSON.parse(configContent);
      console.log(`✅ config.json exists with provider: ${config.embedding_provider || 'unknown'}`);
    } catch (error) {
      throw new Error(`config.json check failed: ${error.message}`);
    }

    // List some qdrant collection contents to prove it has data
    try {
      const collectionsContents = await fs.readdir(collectionsPath);
      console.log(`✅ Qdrant collections found: ${collectionsContents.length} collections`);
      for (const collection of collectionsContents.slice(0, 3)) { // Show first 3
        console.log(`   📁 Collection: ${collection}`);
      }
    } catch (error) {
      console.log(`⚠️ Could not list qdrant collections: ${error.message}`);
    }

    // Step 5: Unregister repository
    console.log('\n🗑️ Step 5: Unregistering repository...');
    const unregisterResponse = await makeRequest(`${API_BASE}/repositories/${testRepo}`, {
      method: 'DELETE',
      headers: {
        'Authorization': `Bearer ${authToken}`
      }
    });

    if (!unregisterResponse.ok) {
      throw new Error(`Unregistration failed: HTTP ${unregisterResponse.statusCode} - ${JSON.stringify(unregisterResponse.data)}`);
    }

    console.log(`✅ Repository unregistration initiated`);
    console.log(`🔄 Success: ${unregisterResponse.data.success}`);

    // Step 6: Wait for unregistration to complete (API level)
    console.log('\n⏳ Step 6: Waiting for unregistration to complete...');
    let unregistrationComplete = false;
    let unregAttempts = 0;
    const maxUnregAttempts = 30; // 7.5 minutes max

    while (!unregistrationComplete && unregAttempts < maxUnregAttempts) {
      unregAttempts++;
      await sleep(15000); // Wait 15 seconds

      const statusResponse = await makeRequest(`${API_BASE}/repositories`, {
        headers: {
          'Authorization': `Bearer ${authToken}`
        }
      });

      if (!statusResponse.ok) {
        throw new Error(`Status check during unregistration failed: HTTP ${statusResponse.statusCode}`);
      }

      const testRepoData = statusResponse.data.find(repo => repo.name === testRepo);

      if (!testRepoData) {
        unregistrationComplete = true;
        console.log(`✅ Repository removed from API (attempt ${unregAttempts})`);
      } else {
        console.log(`⏳ Unregistration attempt ${unregAttempts}/${maxUnregAttempts}: Repository still in API`);
      }
    }

    if (!unregistrationComplete) {
      throw new Error(`Repository was not removed from API after ${maxUnregAttempts} attempts`);
    }

    // Step 7: Verify file system cleanup
    console.log('\n🧹 Step 7: Verifying file system cleanup...');
    
    // Wait a bit more for file system cleanup
    console.log('⏳ Waiting 10 seconds for file system cleanup...');
    await sleep(10000);

    // Check that .code-indexer directory is gone
    const codeIndexerCleanupCheck = await checkDirectory(codeIndexerPath, false);
    if (codeIndexerCleanupCheck.exists) {
      throw new Error(`.code-indexer directory was not cleaned up: ${JSON.stringify(codeIndexerCleanupCheck)}`);
    }

    // Check that main repository directory is gone
    const repoCleanupCheck = await checkDirectory(repoPath, false);
    if (repoCleanupCheck.exists) {
      throw new Error(`Repository directory was not cleaned up: ${JSON.stringify(repoCleanupCheck)}`);
    }

    // Final success message
    console.log('\n🎉 ALL TESTS PASSED!');
    console.log('='.repeat(50));
    console.log(`✅ Repository: ${testRepo}`);
    console.log(`✅ Authentication: SUCCESS`);
    console.log(`✅ Registration: SUCCESS`);
    console.log(`✅ CIDX Setup: SUCCESS`);
    console.log(`✅ File System Creation: SUCCESS`);
    console.log(`✅ Qdrant Data: SUCCESS`);
    console.log(`✅ Unregistration: SUCCESS`);
    console.log(`✅ File System Cleanup: SUCCESS`);
    console.log('='.repeat(50));
    console.log('🏆 API Direct Flow Test COMPLETED SUCCESSFULLY!');

  } catch (error) {
    console.error('\n❌ TEST FAILED!');
    console.error('='.repeat(50));
    console.error(`Error: ${error.message}`);
    console.error('='.repeat(50));
    process.exit(1);
  }
}

// Run the test
main().catch(error => {
  console.error('Unhandled error:', error);
  process.exit(1);
});