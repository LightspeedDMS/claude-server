import apiClient from '../services/api.js'
import { jobMonitorManager } from '../services/job-monitor.js'
import { MarkdownParser } from '../utils/markdown.js'

/**
 * Job List Component
 * Displays all user jobs with real-time status updates
 */
export class JobListComponent {
  constructor(container, options = {}) {
    this.container = container
    this.options = options
    this.jobs = []
    this.filteredJobs = []
    this.currentFilter = 'all'
    this.searchQuery = ''
    this.sortBy = 'created'
    this.sortOrder = 'desc'
    this.isLoading = false
    
    this.init()
  }

  async init() {
    this.render()
    this.bindEvents()
    await this.loadJobs()
    this.startMonitoring()
  }

  render() {
    this.container.innerHTML = `
      <div class="job-list-container container">
        <div class="job-list-header">
          <h1 class="job-list-title">Jobs</h1>
          <div class="job-list-actions">
            <button class="btn btn-secondary" id="refreshJobs">
              <span>🔄</span> Refresh
            </button>
            <button class="btn btn-primary" id="createJob" data-testid="create-job-button">
              <span>+</span> Create Job
            </button>
          </div>
        </div>

        <div class="job-filters">
          <select class="filter-select" id="statusFilter">
            <option value="all">All Statuses</option>
            <option value="created">Created</option>
            <option value="queued">Queued</option>
            <option value="running">Running</option>
            <option value="completed">Completed</option>
            <option value="failed">Failed</option>
            <option value="cancelled">Cancelled</option>
          </select>

          <input 
            type="text" 
            class="job-search" 
            id="searchJobs" 
            placeholder="Search jobs..."
          />

          <select class="filter-select" id="sortBy">
            <option value="created">Sort by Created</option>
            <option value="title">Sort by Title</option>
            <option value="status">Sort by Status</option>
            <option value="repository">Sort by Repository</option>
          </select>
        </div>

        <div id="jobStats" class="job-stats"></div>

        <div id="jobsContainer" class="jobs-container">
          <div class="loading-spinner"></div>
        </div>
      </div>
    `
  }

  bindEvents() {
    // Refresh button
    const refreshBtn = this.container.querySelector('#refreshJobs')
    refreshBtn.addEventListener('click', () => this.loadJobs())

    // Create job button
    const createBtn = this.container.querySelector('#createJob')
    createBtn.addEventListener('click', () => {
      if (this.options.onCreateJob) {
        this.options.onCreateJob()
      }
    })

    // Filter controls
    const statusFilter = this.container.querySelector('#statusFilter')
    statusFilter.addEventListener('change', (e) => {
      this.currentFilter = e.target.value
      this.filterAndRenderJobs()
    })

    const searchInput = this.container.querySelector('#searchJobs')
    searchInput.addEventListener('input', (e) => {
      this.searchQuery = e.target.value.toLowerCase()
      this.filterAndRenderJobs()
    })

    const sortSelect = this.container.querySelector('#sortBy')
    sortSelect.addEventListener('change', (e) => {
      this.sortBy = e.target.value
      this.filterAndRenderJobs()
    })
  }

  async loadJobs() {
    this.setLoading(true)
    
    try {
      this.jobs = await apiClient.getJobs()
      this.filterAndRenderJobs()
      this.updateStats()
    } catch (error) {
      console.error('Failed to load jobs:', error)
      this.showError('Failed to load jobs. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  filterAndRenderJobs() {
    // Apply filters
    this.filteredJobs = this.jobs.filter(job => {
      // Status filter
      if (this.currentFilter !== 'all' && job.status.toLowerCase() !== this.currentFilter) {
        return false
      }

      // Search filter
      if (this.searchQuery) {
        const searchTerm = this.searchQuery.toLowerCase()
        return (
          job.title.toLowerCase().includes(searchTerm) ||
          job.repository.toLowerCase().includes(searchTerm) ||
          job.status.toLowerCase().includes(searchTerm)
        )
      }

      return true
    })

    // Apply sorting
    this.filteredJobs.sort((a, b) => {
      let valueA, valueB

      switch (this.sortBy) {
        case 'title':
          valueA = a.title.toLowerCase()
          valueB = b.title.toLowerCase()
          break
        case 'status':
          valueA = a.status.toLowerCase()
          valueB = b.status.toLowerCase()
          break
        case 'repository':
          valueA = a.repository.toLowerCase()
          valueB = b.repository.toLowerCase()
          break
        case 'created':
        default:
          valueA = new Date(a.started)
          valueB = new Date(b.started)
          break
      }

      if (valueA < valueB) return this.sortOrder === 'asc' ? -1 : 1
      if (valueA > valueB) return this.sortOrder === 'asc' ? 1 : -1
      return 0
    })

    this.renderJobs()
  }

  renderJobs() {
    const container = this.container.querySelector('#jobsContainer')
    
    if (this.filteredJobs.length === 0) {
      container.innerHTML = `
        <div class="empty-state">
          <h3>No jobs found</h3>
          <p>
            ${this.currentFilter !== 'all' || this.searchQuery 
              ? 'Try adjusting your filters or search terms.' 
              : 'Create your first job to get started.'}
          </p>
          ${this.currentFilter === 'all' && !this.searchQuery ? `
            <button class="btn btn-primary" onclick="document.getElementById('createJob').click()">
              Create Your First Job
            </button>
          ` : ''}
        </div>
      `
      return
    }

    const jobsHtml = this.filteredJobs.map(job => this.renderJobCard(job)).join('')
    
    container.innerHTML = `
      <div class="job-grid" data-testid="job-list">
        ${jobsHtml}
      </div>
    `
    
    this.bindJobEvents()
  }

  renderJobCard(job) {
    const statusClass = `status-${job.status.toLowerCase().replace('_', '-')}`
    const createdAt = new Date(job.started || job.createdAt || job.created).toLocaleDateString()
    const statusText = this.formatStatus(job.status)
    const showResults = this.canShowResults(job.status)
    
    return `
      <div class="card job-card ${statusClass}" data-job-id="${job.jobId}" data-testid="job-item">
        <div class="card-body">
          <div class="job-card-header">
            <h3 class="job-title" data-testid="job-title">${this.escapeHtml(job.title)}</h3>
            <div class="job-status">
              <div class="status-indicator"></div>
              <span class="badge badge-${job.status.toLowerCase().replace('_', '-')}" data-testid="job-status">
                ${statusText}
              </span>
            </div>
          </div>

          <div class="job-meta">
            <div class="job-repository" data-testid="job-repository">
              📁 ${this.escapeHtml(job.repository)}
            </div>
            <div class="job-timing">
              <span>Created: ${createdAt}</span>
              <span data-testid="job-id" style="display: none;">${job.jobId}</span>
            </div>
          </div>

          <div class="job-actions">
            <button class="btn btn-sm btn-outline view-job" data-job-id="${job.jobId}">
              View Details
            </button>
            ${this.canCancelJob(job.status) ? `
              <button class="btn btn-sm btn-warning cancel-job" data-job-id="${job.jobId}" data-testid="cancel-job-button">
                Cancel
              </button>
            ` : ''}
            ${this.canDeleteJob(job.status) ? `
              <button class="btn btn-sm btn-error delete-job" data-job-id="${job.jobId}" data-testid="delete-job-${job.jobId}">
                Delete
              </button>
            ` : ''}
          </div>
        </div>
        
        ${showResults ? this.renderJobResults(job) : ''}
      </div>
    `
  }

  bindJobEvents() {
    // View job details
    this.container.querySelectorAll('.view-job').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const jobId = e.target.dataset.jobId
        if (this.options.onViewJob) {
          this.options.onViewJob(jobId)
        }
      })
    })

    // Cancel job
    this.container.querySelectorAll('.cancel-job').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const jobId = e.target.dataset.jobId
        this.confirmCancelJob(jobId)
      })
    })

    // Delete job
    this.container.querySelectorAll('.delete-job').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const jobId = e.target.dataset.jobId
        this.confirmDeleteJob(jobId)
      })
    })

    // Expand/collapse job results
    this.container.querySelectorAll('.job-results-header').forEach(header => {
      header.addEventListener('click', (e) => {
        e.stopPropagation()
        const jobId = header.dataset.jobId
        this.toggleJobResults(jobId)
      })
    })

    // Copy output button
    this.container.querySelectorAll('.copy-output-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation()
        const jobId = btn.dataset.jobId
        await this.copyJobOutput(jobId)
      })
    })

    // Download output button
    this.container.querySelectorAll('.download-output-btn').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation()
        const jobId = btn.dataset.jobId
        this.downloadJobOutput(jobId)
      })
    })

    // Tab switching in results
    this.container.querySelectorAll('.results-tab').forEach(tab => {
      tab.addEventListener('click', (e) => {
        e.stopPropagation()
        const jobId = tab.dataset.jobId
        const tabName = tab.dataset.tab
        this.switchResultsTab(jobId, tabName)
      })
    })

    // Click on job card to view details (but not if clicking on results section)
    this.container.querySelectorAll('.job-card').forEach(card => {
      card.addEventListener('click', (e) => {
        // Don't trigger if clicking on buttons or results section
        if (e.target.closest('button') || e.target.closest('.job-results-section')) return
        
        const jobId = card.dataset.jobId
        if (this.options.onViewJob) {
          this.options.onViewJob(jobId)
        }
      })
    })
  }

  async confirmCancelJob(jobId) {
    if (!confirm('Are you sure you want to cancel this job?')) {
      return
    }

    try {
      await apiClient.cancelJob(jobId)
      this.loadJobs() // Refresh the list
    } catch (error) {
      console.error('Failed to cancel job:', error)
      alert('Failed to cancel job. Please try again.')
    }
  }

  async confirmDeleteJob(jobId) {
    if (!confirm('Are you sure you want to delete this job? This action cannot be undone.')) {
      return
    }

    try {
      await apiClient.deleteJob(jobId)
      this.loadJobs() // Refresh the list
    } catch (error) {
      console.error('Failed to delete job:', error)
      alert('Failed to delete job. Please try again.')
    }
  }

  startMonitoring() {
    // Start monitoring all active jobs
    this.jobs.forEach(job => {
      if (this.isActiveJob(job.status)) {
        jobMonitorManager.startMonitoring(job.jobId, {
          onStatusUpdate: (status) => {
            this.updateJobStatus(job.jobId, status)
          },
          onComplete: () => {
            this.loadJobs() // Refresh when job completes
          }
        })
      }
    })
  }

  updateJobStatus(jobId, status) {
    // Update job in local array
    const job = this.jobs.find(j => j.jobId === jobId)
    if (job) {
      job.status = status.status
      // Update the card in the DOM
      this.updateJobCard(jobId, status)
    }
  }

  updateJobCard(jobId, status) {
    const card = this.container.querySelector(`[data-job-id="${jobId}"]`)
    if (!card) return

    const statusBadge = card.querySelector('.badge')
    const statusIndicator = card.querySelector('.status-indicator')
    
    if (statusBadge) {
      statusBadge.className = `badge badge-${status.status.toLowerCase().replace('_', '-')}`
      statusBadge.textContent = this.formatStatus(status.status)
    }

    // Update card class
    card.className = `card job-card status-${status.status.toLowerCase().replace('_', '-')}`
  }

  updateStats() {
    const stats = this.calculateStats()
    const statsContainer = this.container.querySelector('#jobStats')
    
    statsContainer.innerHTML = `
      <div class="job-stats-grid">
        <div class="stat-item">
          <span class="stat-value">${stats.total}</span>
          <span class="stat-label">Total Jobs</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-success">${stats.running}</span>
          <span class="stat-label">Running</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-info">${stats.queued}</span>
          <span class="stat-label">Queued</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-success">${stats.completed}</span>
          <span class="stat-label">Completed</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-error">${stats.failed}</span>
          <span class="stat-label">Failed</span>
        </div>
      </div>
    `
  }

  calculateStats() {
    return {
      total: this.jobs.length,
      running: this.jobs.filter(j => ['running', 'git_pulling', 'cidx_indexing'].includes(j.status.toLowerCase())).length,
      queued: this.jobs.filter(j => j.status.toLowerCase() === 'queued').length,
      completed: this.jobs.filter(j => j.status.toLowerCase() === 'completed').length,
      failed: this.jobs.filter(j => ['failed', 'timeout'].includes(j.status.toLowerCase())).length
    }
  }

  formatStatus(status) {
    const statusMap = {
      'created': 'Created',
      'queued': 'Queued',
      'git_pulling': 'Cloning',
      'cidx_indexing': 'Indexing',
      'running': 'Running',
      'completed': 'Completed',
      'failed': 'Failed',
      'cancelled': 'Cancelled',
      'timeout': 'Timeout'
    }
    
    return statusMap[status.toLowerCase()] || status
  }

  canCancelJob(status) {
    return ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running'].includes(status.toLowerCase())
  }

  canDeleteJob(status) {
    return ['completed', 'failed', 'cancelled', 'timeout'].includes(status.toLowerCase())
  }

  isActiveJob(status) {
    return ['created', 'queued', 'git_pulling', 'cidx_indexing', 'running'].includes(status.toLowerCase())
  }

  canShowResults(status) {
    return ['completed', 'failed', 'cancelled', 'timeout'].includes(status.toLowerCase())
  }

  renderJobResults(job) {
    const isExpanded = this.isJobResultsExpanded(job.jobId)
    const expandClass = isExpanded ? 'expanded' : ''
    const iconClass = isExpanded ? 'expanded' : ''
    
    // Determine result status and icon
    let statusIcon, statusClass, resultTitle, resultSummary
    
    switch (job.status.toLowerCase()) {
      case 'completed':
        statusIcon = '✅'
        statusClass = 'success'
        resultTitle = 'Job Completed Successfully'
        resultSummary = job.exitCode === 0 ? 'Execution finished without errors' : `Completed with exit code ${job.exitCode}`
        break
      case 'failed':
        statusIcon = '❌'
        statusClass = 'error'
        resultTitle = 'Job Failed'
        resultSummary = job.exitCode ? `Failed with exit code ${job.exitCode}` : 'Execution encountered an error'
        break
      case 'timeout':
        statusIcon = '⏰'
        statusClass = 'timeout'
        resultTitle = 'Job Timed Out'
        resultSummary = 'Execution exceeded the maximum allowed time'
        break
      case 'cancelled':
        statusIcon = '⚫'
        statusClass = 'error'
        resultTitle = 'Job Cancelled'
        resultSummary = 'Execution was cancelled by user'
        break
      default:
        statusIcon = '🔄'
        statusClass = 'info'
        resultTitle = 'Job In Progress'
        resultSummary = 'Execution is still running'
    }

    return `
      <div class="job-results-section" data-job-id="${job.jobId}">
        <div class="job-results-header" data-job-id="${job.jobId}">
          <div class="results-header-left">
            <div class="results-status-icon ${statusClass}">
              ${statusIcon}
            </div>
            <div class="results-header-content">
              <h4 class="results-title">${resultTitle}</h4>
              <p class="results-summary">${resultSummary}</p>
            </div>
          </div>
          <div class="results-expand-icon ${iconClass}">
            ⌄
          </div>
        </div>
        
        <div class="job-results-content ${expandClass}">
          ${this.renderJobResultsContent(job)}
        </div>
      </div>
    `
  }

  renderJobResultsContent(job) {
    const activeTab = this.getActiveResultsTab(job.jobId) || 'formatted'
    
    return `
      <div class="job-results-tabs">
        <button class="results-tab ${activeTab === 'formatted' ? 'active' : ''}" 
                data-job-id="${job.jobId}" data-tab="formatted">
          📝 Formatted Output
        </button>
        <button class="results-tab ${activeTab === 'raw' ? 'active' : ''}" 
                data-job-id="${job.jobId}" data-tab="raw">
          🔤 Raw Output
        </button>
        ${job.status.toLowerCase() === 'failed' ? `
          <button class="results-tab ${activeTab === 'error' ? 'active' : ''}" 
                  data-job-id="${job.jobId}" data-tab="error">
            ⚠️ Error Details
          </button>
        ` : ''}
      </div>
      
      <div class="results-tab-content ${activeTab === 'formatted' ? 'active' : ''}" 
           data-tab="formatted">
        ${this.renderFormattedOutput(job)}
      </div>
      
      <div class="results-tab-content ${activeTab === 'raw' ? 'active' : ''}" 
           data-tab="raw">
        ${this.renderRawOutput(job)}
      </div>
      
      ${job.status.toLowerCase() === 'failed' ? `
        <div class="results-tab-content ${activeTab === 'error' ? 'active' : ''}" 
             data-tab="error">
          ${this.renderErrorDetails(job)}
        </div>
      ` : ''}
      
      <div class="job-results-actions">
        <div class="results-actions-left">
          <button class="btn btn-sm btn-outline copy-output-btn" data-job-id="${job.jobId}">
            📋 Copy Output
          </button>
          <button class="btn btn-sm btn-outline download-output-btn" data-job-id="${job.jobId}">
            💾 Download
          </button>
        </div>
        <div class="results-actions-right">
          <div class="results-info">
            ${job.exitCode !== null && job.exitCode !== undefined ? `Exit Code: ${job.exitCode}` : ''}
            ${job.completedAt ? `• Completed: ${new Date(job.completedAt).toLocaleString()}` : ''}
          </div>
        </div>
      </div>
    `
  }

  renderFormattedOutput(job) {
    if (!job.output || job.output.trim() === '') {
      return `
        <div class="job-output-display empty">
          No output available
        </div>
      `
    }

    // Try to parse as markdown first, fallback to plain text
    const formattedOutput = MarkdownParser.parse(job.output)
    
    return `
      <div class="markdown-content">
        ${formattedOutput}
      </div>
    `
  }

  renderRawOutput(job) {
    if (!job.output || job.output.trim() === '') {
      return `
        <div class="job-output-display empty">
          No output available
        </div>
      `
    }

    return `
      <div class="job-output-display">
        ${this.escapeHtml(job.output)}
      </div>
    `
  }

  renderErrorDetails(job) {
    const errorOutput = job.output || 'No error details available'
    
    return `
      <div class="job-error-display">
        <div class="error-title">
          <span class="error-icon">⚠️</span>
          Execution Error Details
        </div>
        <div class="error-details">
          ${this.escapeHtml(errorOutput)}
        </div>
      </div>
    `
  }

  // State management for expandable results
  isJobResultsExpanded(jobId) {
    return this.expandedResults?.has(jobId) || false
  }

  getActiveResultsTab(jobId) {
    return this.activeResultsTabs?.get(jobId) || 'formatted'
  }

  toggleJobResults(jobId) {
    if (!this.expandedResults) {
      this.expandedResults = new Set()
    }

    const isExpanded = this.isJobResultsExpanded(jobId)
    
    if (isExpanded) {
      this.expandedResults.delete(jobId)
    } else {
      this.expandedResults.add(jobId)
      // Load detailed job data if not already loaded
      this.loadJobDetails(jobId)
    }

    // Update UI
    this.updateJobResultsUI(jobId)
  }

  switchResultsTab(jobId, tabName) {
    if (!this.activeResultsTabs) {
      this.activeResultsTabs = new Map()
    }

    this.activeResultsTabs.set(jobId, tabName)
    this.updateJobResultsTabsUI(jobId)
  }

  updateJobResultsUI(jobId) {
    const card = this.container.querySelector(`[data-job-id="${jobId}"]`)
    if (!card) return

    const resultsSection = card.querySelector('.job-results-section')
    const content = resultsSection?.querySelector('.job-results-content')
    const icon = resultsSection?.querySelector('.results-expand-icon')

    if (content && icon) {
      const isExpanded = this.isJobResultsExpanded(jobId)
      content.classList.toggle('expanded', isExpanded)
      icon.classList.toggle('expanded', isExpanded)
    }
  }

  updateJobResultsTabsUI(jobId) {
    const card = this.container.querySelector(`[data-job-id="${jobId}"]`)
    if (!card) return

    const activeTab = this.getActiveResultsTab(jobId)
    
    // Update tab buttons
    card.querySelectorAll('.results-tab').forEach(tab => {
      const isActive = tab.dataset.tab === activeTab
      tab.classList.toggle('active', isActive)
    })

    // Update tab content
    card.querySelectorAll('.results-tab-content').forEach(content => {
      const isActive = content.dataset.tab === activeTab
      content.classList.toggle('active', isActive)
    })
  }

  async loadJobDetails(jobId) {
    try {
      const detailedJob = await apiClient.getJob(jobId)
      
      // Update job in local array
      const jobIndex = this.jobs.findIndex(j => j.jobId === jobId)
      if (jobIndex !== -1) {
        this.jobs[jobIndex] = { ...this.jobs[jobIndex], ...detailedJob }
        
        // Re-render just this job's results content
        this.updateJobResultsContent(jobId, detailedJob)
      }
    } catch (error) {
      console.error('Failed to load job details:', error)
    }
  }

  updateJobResultsContent(jobId, job) {
    const card = this.container.querySelector(`[data-job-id="${jobId}"]`)
    if (!card) return

    const resultsContent = card.querySelector('.job-results-content')
    if (resultsContent) {
      resultsContent.innerHTML = this.renderJobResultsContent(job)
      
      // Re-bind events for the updated content
      this.bindJobResultsEvents(card)
    }
  }

  bindJobResultsEvents(card) {
    // Copy output button
    card.querySelectorAll('.copy-output-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation()
        const jobId = btn.dataset.jobId
        await this.copyJobOutput(jobId)
      })
    })

    // Download output button
    card.querySelectorAll('.download-output-btn').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation()
        const jobId = btn.dataset.jobId
        this.downloadJobOutput(jobId)
      })
    })

    // Tab switching
    card.querySelectorAll('.results-tab').forEach(tab => {
      tab.addEventListener('click', (e) => {
        e.stopPropagation()
        const jobId = tab.dataset.jobId
        const tabName = tab.dataset.tab
        this.switchResultsTab(jobId, tabName)
      })
    })
  }

  async copyJobOutput(jobId) {
    const job = this.jobs.find(j => j.jobId === jobId)
    if (!job || !job.output) return

    try {
      await navigator.clipboard.writeText(job.output)
      
      // Show temporary success feedback
      const btn = this.container.querySelector(`[data-job-id="${jobId}"] .copy-output-btn`)
      if (btn) {
        const originalText = btn.textContent
        btn.textContent = '✅ Copied!'
        btn.disabled = true
        setTimeout(() => {
          btn.textContent = originalText
          btn.disabled = false
        }, 2000)
      }
    } catch (error) {
      console.error('Failed to copy output:', error)
      alert('Failed to copy output to clipboard.')
    }
  }

  downloadJobOutput(jobId) {
    const job = this.jobs.find(j => j.jobId === jobId)
    if (!job || !job.output) return

    const blob = new Blob([job.output], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `job-${jobId}-output.txt`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  setLoading(loading) {
    this.isLoading = loading
    const container = this.container.querySelector('#jobsContainer')
    
    if (loading && this.jobs.length === 0) {
      container.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showError(message) {
    const container = this.container.querySelector('#jobsContainer')
    container.innerHTML = `
      <div class="error-state">
        <h3>Error</h3>
        <p>${message}</p>
        <button class="btn btn-primary" onclick="location.reload()">
          Reload Page
        </button>
      </div>
    `
  }

  escapeHtml(text) {
    const div = document.createElement('div')
    div.textContent = text
    return div.innerHTML
  }

  destroy() {
    // Stop all monitoring
    jobMonitorManager.stopAll()
  }
}