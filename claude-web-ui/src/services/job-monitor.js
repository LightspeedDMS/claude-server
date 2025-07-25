import apiClient from './api.js'

/**
 * Job Status Monitor
 * Handles real-time job status polling with exponential backoff
 */
export class JobMonitor {
  constructor(jobId) {
    this.jobId = jobId
    this.polling = false
    this.intervalId = null
    this.baseInterval = 2000 // 2 seconds as specified in epic
    this.currentInterval = this.baseInterval
    this.maxInterval = 30000 // Max 30 seconds
    this.backoffMultiplier = 1.5
    this.errorCount = 0
    this.maxErrors = 5
    this.callbacks = {
      onStatusUpdate: [],
      onComplete: [],
      onError: [],
      onStop: []
    }
  }

  /**
   * Job statuses that indicate completion
   */
  static COMPLETED_STATUSES = [
    'completed',
    'failed', 
    'timeout',
    'cancelled',
    'terminated'
  ]

  /**
   * Job statuses that indicate active processing
   */
  static ACTIVE_STATUSES = [
    'created',
    'queued', 
    'git_pulling',
    'cidx_indexing',
    'running'
  ]

  /**
   * Add event listener
   */
  on(event, callback) {
    if (this.callbacks[event]) {
      this.callbacks[event].push(callback)
    }
    return this
  }

  /**
   * Remove event listener
   */
  off(event, callback) {
    if (this.callbacks[event]) {
      const index = this.callbacks[event].indexOf(callback)
      if (index > -1) {
        this.callbacks[event].splice(index, 1)
      }
    }
    return this
  }

  /**
   * Emit event to all listeners
   */
  emit(event, data) {
    if (this.callbacks[event]) {
      this.callbacks[event].forEach(callback => {
        try {
          callback(data)
        } catch (error) {
          console.error(`Error in ${event} callback:`, error)
        }
      })
    }
  }

  /**
   * Start polling for job status
   */
  startPolling() {
    if (this.polling) {
      console.warn(`Already polling job ${this.jobId}`)
      return
    }

    console.log(`Starting polling for job ${this.jobId}`)
    this.polling = true
    this.errorCount = 0
    this.currentInterval = this.baseInterval
    this.poll()
  }

  /**
   * Stop polling
   */
  stopPolling() {
    console.log(`Stopping polling for job ${this.jobId}`)
    this.polling = false
    
    if (this.intervalId) {
      clearTimeout(this.intervalId)
      this.intervalId = null
    }

    this.emit('onStop', { jobId: this.jobId })
  }

  /**
   * Perform single poll
   */
  async poll() {
    if (!this.polling) return

    try {
      const status = await apiClient.getJobStatus(this.jobId)
      
      // Reset error count on successful poll
      this.errorCount = 0
      this.currentInterval = this.baseInterval

      // Emit status update
      this.emit('onStatusUpdate', status)

      // Check if job is complete
      if (JobMonitor.COMPLETED_STATUSES.includes(status.status.toLowerCase())) {
        this.emit('onComplete', status)
        this.stopPolling()
        return
      }

      // Schedule next poll
      this.scheduleNextPoll()

    } catch (error) {
      console.error(`Error polling job ${this.jobId}:`, error)
      this.handleError(error)
    }
  }

  /**
   * Handle polling errors with exponential backoff
   */
  handleError(error) {
    this.errorCount++
    
    this.emit('onError', {
      jobId: this.jobId,
      error,
      errorCount: this.errorCount
    })

    // Stop polling if too many errors
    if (this.errorCount >= this.maxErrors) {
      console.error(`Too many errors polling job ${this.jobId}, stopping`)
      this.stopPolling()
      return
    }

    // Apply exponential backoff
    this.currentInterval = Math.min(
      this.currentInterval * this.backoffMultiplier,
      this.maxInterval
    )

    console.log(`Error ${this.errorCount}/${this.maxErrors}, backing off to ${this.currentInterval}ms`)
    
    // Schedule next poll with backoff
    this.scheduleNextPoll()
  }

  /**
   * Schedule next polling cycle
   */
  scheduleNextPoll() {
    if (!this.polling) return

    this.intervalId = setTimeout(() => {
      this.poll()
    }, this.currentInterval)
  }

  /**
   * Get current polling status
   */
  isPolling() {
    return this.polling
  }

  /**
   * Get current interval
   */
  getCurrentInterval() {
    return this.currentInterval
  }

  /**
   * Get error count
   */
  getErrorCount() {
    return this.errorCount
  }
}

/**
 * Job Monitor Manager
 * Manages multiple job monitors efficiently
 */
export class JobMonitorManager {
  constructor() {
    this.monitors = new Map()
  }

  /**
   * Start monitoring a job
   */
  startMonitoring(jobId, callbacks = {}) {
    // Stop existing monitor if present
    this.stopMonitoring(jobId)

    // Create new monitor
    const monitor = new JobMonitor(jobId)
    
    // Add callbacks
    Object.entries(callbacks).forEach(([event, callback]) => {
      monitor.on(event, callback)
    })

    // Auto-cleanup when complete
    monitor.on('onComplete', () => {
      setTimeout(() => {
        this.stopMonitoring(jobId)
      }, 5000) // Keep for 5 seconds after completion
    })

    monitor.on('onStop', () => {
      this.monitors.delete(jobId)
    })

    // Store and start
    this.monitors.set(jobId, monitor)
    monitor.startPolling()

    return monitor
  }

  /**
   * Stop monitoring a job
   */
  stopMonitoring(jobId) {
    const monitor = this.monitors.get(jobId)
    if (monitor) {
      monitor.stopPolling()
      this.monitors.delete(jobId)
    }
  }

  /**
   * Stop all monitoring
   */
  stopAll() {
    this.monitors.forEach(monitor => monitor.stopPolling())
    this.monitors.clear()
  }

  /**
   * Get monitor for job
   */
  getMonitor(jobId) {
    return this.monitors.get(jobId)
  }

  /**
   * Get all active monitors
   */
  getAllMonitors() {
    return Array.from(this.monitors.values())
  }

  /**
   * Get monitoring stats
   */
  getStats() {
    return {
      activeMonitors: this.monitors.size,
      monitors: Array.from(this.monitors.entries()).map(([jobId, monitor]) => ({
        jobId,
        polling: monitor.isPolling(),
        interval: monitor.getCurrentInterval(),
        errors: monitor.getErrorCount()
      }))
    }
  }
}

// Create global monitor manager
export const jobMonitorManager = new JobMonitorManager()

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
  jobMonitorManager.stopAll()
})