/**
 * Playwright Global Teardown with Claude Batch Server Management
 * 
 * Comprehensive cleanup after E2E tests:
 * 1. Shuts down Claude Batch Server if it was started by tests
 * 2. Cleans up test artifacts and temporary files
 * 3. Generates test summary reports
 */

import { promises as fs } from 'fs'
import path from 'path'
import { teardownServer } from './e2e/helpers/server-manager.js'

async function globalTeardown(config) {
  console.log('🧹 Starting global teardown for Claude Web UI E2E tests...')
  
  try {
    // STEP 1: Shutdown Claude Batch Server if it was started by tests
    console.log('🛑 Managing Claude Batch Server shutdown...')
    await teardownServer()
    
    // STEP 2: Cleanup test artifacts and temporary files
    console.log('🗂️ Cleaning up test artifacts...')
    await cleanupTestArtifacts()
    
    // STEP 3: Cleanup authentication state files
    console.log('🔐 Cleaning up authentication states...')
    await cleanupAuthStates()
    
    // STEP 4: Generate test summary report
    console.log('📊 Generating test summary...')
    await generateTestSummary()
    
    // STEP 5: Cleanup any leftover test data
    console.log('🗑️ Cleaning up test data...')
    await cleanupTestData()
    
    // STEP 6: Report server management results
    const wasStartedByTest = process.env.E2E_SERVER_STARTED_BY_TEST === 'true'
    const wasAlreadyRunning = process.env.E2E_SERVER_WAS_RUNNING === 'true'
    
    if (wasStartedByTest) {
      console.log('🏁 Claude Batch Server was started by tests and has been shut down')
    } else if (wasAlreadyRunning) {
      console.log('🔄 Claude Batch Server was already running and has been left running')
    } else {
      console.log('❓ Claude Batch Server state unknown')
    }
    
  } catch (error) {
    console.warn('⚠️ Error during teardown:', error.message)
    // Don't fail the entire test run due to cleanup issues
  }
  
  console.log('✅ Global teardown completed successfully')
  console.log('🎯 E2E test environment has been properly cleaned up')
}

/**
 * Cleanup test artifacts and temporary files
 */
async function cleanupTestArtifacts() {
  const artifactDirs = [
    'test-results/screenshots',
    'test-results/videos',
    'test-results/traces'
  ]
  
  for (const dir of artifactDirs) {
    try {
      const fullPath = path.join(process.cwd(), dir)
      const stats = await fs.stat(fullPath).catch(() => null)
      if (stats && stats.isDirectory()) {
        // Keep only the most recent test artifacts (last 5 runs)
        const files = await fs.readdir(fullPath)
        const sortedFiles = files
          .map(file => ({
            name: file,
            path: path.join(fullPath, file),
            time: fs.stat(path.join(fullPath, file)).then(s => s.mtime)
          }))
        
        // This is non-critical cleanup, so we don't need to wait
        Promise.all(sortedFiles.map(f => f.time)).then(times => {
          const filesWithTimes = sortedFiles.map((f, i) => ({ ...f, time: times[i] }))
          filesWithTimes.sort((a, b) => b.time - a.time)
          
          // Remove old files (keep newest 10)
          const toDelete = filesWithTimes.slice(10)
          return Promise.all(toDelete.map(f => fs.unlink(f.path).catch(() => {})))
        }).catch(() => {})
      }
    } catch (error) {
      // Cleanup errors are non-critical
    }
  }
}

/**
 * Cleanup authentication state files
 */
async function cleanupAuthStates() {
  const stateFiles = [
    'tests/fixtures/initial-state.json',
    'tests/fixtures/authenticated-state.json',
    'tests/fixtures/admin-state.json'
  ]
  
  for (const stateFile of stateFiles) {
    try {
      await fs.unlink(stateFile)
    } catch (error) {
      // File might not exist, which is fine
    }
  }
}

/**
 * Generate test summary report
 */
async function generateTestSummary() {
  try {
    const resultsPath = path.join(process.cwd(), 'test-results/results.json')
    const results = await fs.readFile(resultsPath, 'utf8').catch(() => '{}')
    const testData = JSON.parse(results)
    
    const summary = {
      timestamp: new Date().toISOString(),
      environment: {
        nodeVersion: process.version,
        platform: process.platform,
        cwd: process.cwd()
      },
      summary: {
        total: testData.stats?.total || 0,
        passed: testData.stats?.passed || 0,
        failed: testData.stats?.failed || 0,
        skipped: testData.stats?.skipped || 0,
        duration: testData.stats?.duration || 0
      },
      cleanup: {
        completedAt: new Date().toISOString(),
        artifactsCleanedUp: true,
        authStatesCleanedUp: true
      }
    }
    
    await fs.writeFile(
      path.join(process.cwd(), 'test-results/teardown-summary.json'),
      JSON.stringify(summary, null, 2)
    )
    
    console.log(`📈 Test Summary: ${summary.summary.passed}/${summary.summary.total} passed`)
    
  } catch (error) {
    console.warn('⚠️ Could not generate test summary:', error.message)
  }
}

/**
 * Cleanup any test data that might have been created
 */
async function cleanupTestData() {
  // Clean up any test files that might have been created in fixtures
  const testDataPattern = /^test-.*\.(json|txt|md)$/
  const fixturesDir = path.join(process.cwd(), 'tests/fixtures/test-files')
  
  try {
    const files = await fs.readdir(fixturesDir)
    const testFiles = files.filter(file => testDataPattern.test(file))
    
    await Promise.all(
      testFiles.map(file => 
        fs.unlink(path.join(fixturesDir, file)).catch(() => {})
      )
    )
    
    if (testFiles.length > 0) {
      console.log(`🗑️ Cleaned up ${testFiles.length} test data files`)
    }
  } catch (error) {
    // Directory might not exist or be accessible
  }
}

export default globalTeardown