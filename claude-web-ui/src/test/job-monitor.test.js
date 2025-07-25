/**
 * Basic tests for JobMonitor functionality
 * These can be run in the browser console for manual testing
 */

// Simple test framework
class SimpleTest {
  constructor() {
    this.tests = []
    this.passed = 0
    this.failed = 0
  }

  test(name, fn) {
    this.tests.push({ name, fn })
  }

  async run() {
    console.log('🧪 Running JobMonitor Tests...\n')
    
    for (const { name, fn } of this.tests) {
      try {
        await fn()
        console.log(`✅ ${name}`)
        this.passed++
      } catch (error) {
        console.error(`❌ ${name}:`, error.message)
        this.failed++
      }
    }

    console.log(`\n📊 Test Results: ${this.passed} passed, ${this.failed} failed`)
  }
}

// Create test instance
const test = new SimpleTest()

// Import the JobMonitor class (assuming it's available globally)
import { JobMonitor, JobMonitorManager } from '../services/job-monitor.js'

// Test JobMonitor instantiation
test.test('JobMonitor can be instantiated', () => {
  const monitor = new JobMonitor('test-job-id')
  if (!monitor.jobId || monitor.jobId !== 'test-job-id') {
    throw new Error('JobMonitor not properly instantiated')
  }
})

// Test event listener functionality
test.test('JobMonitor can add event listeners', () => {
  const monitor = new JobMonitor('test-job-id')
  let callbackCalled = false
  
  monitor.on('onStatusUpdate', () => {
    callbackCalled = true
  })
  
  monitor.emit('onStatusUpdate', { status: 'running' })
  
  if (!callbackCalled) {
    throw new Error('Event callback was not called')
  }
})

// Test status classification
test.test('JobMonitor correctly identifies completed statuses', () => {
  const completedStatuses = ['completed', 'failed', 'timeout', 'cancelled']
  
  for (const status of completedStatuses) {
    if (!JobMonitor.COMPLETED_STATUSES.includes(status)) {
      throw new Error(`Status '${status}' should be in COMPLETED_STATUSES`)
    }
  }
})

// Test active status classification
test.test('JobMonitor correctly identifies active statuses', () => {
  const activeStatuses = ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running']
  
  for (const status of activeStatuses) {
    if (!JobMonitor.ACTIVE_STATUSES.includes(status)) {
      throw new Error(`Status '${status}' should be in ACTIVE_STATUSES`)
    }
  }
})

// Test JobMonitorManager
test.test('JobMonitorManager can manage multiple monitors', () => {
  const manager = new JobMonitorManager()
  
  // Create a monitor
  const monitor1 = manager.startMonitoring('job-1')
  const monitor2 = manager.startMonitoring('job-2')
  
  if (manager.getAllMonitors().length !== 2) {
    throw new Error('Manager should have 2 active monitors')
  }
  
  // Stop one monitor
  manager.stopMonitoring('job-1')
  
  if (manager.getAllMonitors().length !== 1) {
    throw new Error('Manager should have 1 active monitor after stopping one')
  }
  
  // Stop all
  manager.stopAll()
  
  if (manager.getAllMonitors().length !== 0) {
    throw new Error('Manager should have 0 active monitors after stopping all')
  }
})

// Export for manual testing
window.runJobMonitorTests = () => test.run()

console.log('🧪 JobMonitor tests loaded. Run window.runJobMonitorTests() to execute.')