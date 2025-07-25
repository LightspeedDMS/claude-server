#!/usr/bin/env node

// Test script to verify staging area workflow
const fs = require('fs');
const FormData = require('form-data');
const fetch = require('node-fetch');

async function testStagingWorkflow() {
    console.log('🧪 Testing staging area workflow with CIDX disabled...');
    
    try {
        // Step 1: Create a test job with CIDX disabled
        console.log('\n1️⃣ Creating job with CIDX disabled...');
        
        const jobData = {
            prompt: "Test file upload with staging area",
            repository: "tries", // Assuming this repository exists and is CIDX-aware
            images: [],
            options: {
                timeout: 300,
                gitAware: true,
                cidxAware: false  // CRITICAL: Disable CIDX to avoid validation error
            }
        };
        
        const createJobResponse = await fetch('http://localhost:5185/jobs', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': 'Bearer test-token' // You'll need a valid token
            },
            body: JSON.stringify(jobData)
        });
        
        if (!createJobResponse.ok) {
            const errorText = await createJobResponse.text();
            console.error('❌ Job creation failed:', createJobResponse.status, errorText);
            return;
        }
        
        const jobResult = await createJobResponse.json();
        console.log('✅ Job created successfully:', jobResult.jobId);
        
        // Step 2: Upload a test file to staging area
        console.log('\n2️⃣ Uploading test file to staging area...');
        
        // Create a test file
        const testFileContent = `# Test File for Staging Area
This file was uploaded before CoW workspace creation.
Job ID: ${jobResult.jobId}
Timestamp: ${new Date().toISOString()}

The staging area workflow should:
1. Accept this file upload immediately after job creation
2. Store it in staging directory: workspace/jobs/${jobResult.jobId}/staging/
3. Copy it to CoW workspace when job execution starts
4. Include it in CIDX indexing if enabled
`;
        
        const testFileName = 'staging-test.md';
        fs.writeFileSync(`/tmp/${testFileName}`, testFileContent);
        
        const formData = new FormData();
        formData.append('file', fs.createReadStream(`/tmp/${testFileName}`));
        
        const uploadResponse = await fetch(`http://localhost:5185/jobs/${jobResult.jobId}/files`, {
            method: 'POST',
            headers: {
                ...formData.getHeaders(),
                'Authorization': 'Bearer test-token' // You'll need a valid token
            },
            body: formData
        });
        
        if (!uploadResponse.ok) {
            const errorText = await uploadResponse.text();
            console.error('❌ File upload failed:', uploadResponse.status, errorText);
            console.error('This indicates the staging area is not working properly');
        } else {
            const uploadResult = await uploadResponse.json();
            console.log('✅ File uploaded successfully to staging area:', uploadResult);
            
            // Step 3: Verify staging area contains the file
            console.log('\n3️⃣ Verifying staging area...');
            const stagingPath = `/home/jsbattig/Dev/claude-server/claude-batch-server/src/ClaudeBatchServer.Api/workspace/jobs/${jobResult.jobId}/staging`;
            
            if (fs.existsSync(stagingPath)) {
                const stagedFiles = fs.readdirSync(stagingPath);
                console.log('✅ Staging directory exists with files:', stagedFiles);
            } else {
                console.error('❌ Staging directory not found at:', stagingPath);
            }
        }
        
        // Step 4: Start the job to test file copying
        console.log('\n4️⃣ Starting job to test CoW clone and file copying...');
        
        const startResponse = await fetch(`http://localhost:5185/jobs/${jobResult.jobId}/start`, {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer test-token' // You'll need a valid token
            }
        });
        
        if (!startResponse.ok) {
            const errorText = await startResponse.text();
            console.error('❌ Job start failed:', startResponse.status, errorText);
        } else {
            const startResult = await startResponse.json();
            console.log('✅ Job started successfully:', startResult);
            console.log('📝 Monitor the job status to see if files are copied to CoW workspace');
        }
        
        // Cleanup
        fs.unlinkSync(`/tmp/${testFileName}`);
        
        console.log(`\n🎯 Test Summary:
- Job ID: ${jobResult.jobId}
- Test file: ${testFileName}
- Expected staging path: workspace/jobs/${jobResult.jobId}/staging/
- Expected CoW path after job start: workspace/repos/tries_cow_${jobResult.jobId}/files/

Monitor the job execution to verify:
1. Staging files are copied to CoW workspace
2. Job executes successfully with uploaded files
3. Staging directory is cleaned up after successful copy`);
        
    } catch (error) {
        console.error('❌ Test failed with error:', error.message);
        console.error('Stack trace:', error.stack);
    }
}

// Note: This script requires authentication
console.log('⚠️  This script needs to be updated with proper authentication');
console.log('You can either:');
console.log('1. Disable authentication temporarily for testing');
console.log('2. Get a valid JWT token from the web UI');
console.log('3. Use shadow file auth with proper credentials');
console.log('');
console.log('For now, check the specific error in browser network tab or server logs');

// Uncomment to run the test (after setting up authentication)
// testStagingWorkflow();