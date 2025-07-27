import apiClient from '../services/api.js'
import { jobMonitorManager } from '../services/job-monitor.js'
import { FileBrowserComponent } from './file-browser.js'

/**
 * Job Details Component
 * Shows detailed job information with real-time updates
 */
export class JobDetailsComponent {
  constructor(container, jobId, options = {}) {
    this.container = container
    this.jobId = jobId
    this.options = options
    this.job = null
    this.monitor = null
    this.isLoading = false
    this.fileBrowser = null
    this.currentView = 'details' // 'details' or 'files'
    
    this.init()
  }

  async init() {
    this.render()
    await this.loadJob()
    this.startMonitoring()
  }

  render() {
    this.container.innerHTML = `
      <div class="job-details-container container">
        <div class="job-details-nav">
          <button class="nav-tab ${this.currentView === 'details' ? 'active' : ''}" 
                  data-view="details" data-testid="details-tab">
            📋 Job Details
          </button>
          <button class="nav-tab ${this.currentView === 'files' ? 'active' : ''}" 
                  data-view="files" data-testid="files-tab">
            📁 Workspace Files
          </button>
        </div>
        
        <div id="jobDetailsContent" class="${this.currentView === 'details' ? 'active' : 'hidden'}">
          <div class="loading-spinner"></div>
        </div>
        
        <div id="jobFilesContent" class="${this.currentView === 'files' ? 'active' : 'hidden'}">
          <!-- File browser will be rendered here -->
        </div>
      </div>
    `
    
    this.bindNavEvents()
  }

  async loadJob() {
    this.setLoading(true)
    
    try {
      this.job = await apiClient.getJob(this.jobId)
      this.renderJobDetails()
    } catch (error) {
      console.error('Failed to load job:', error)
      this.showError('Failed to load job details. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  renderJobDetails() {
    if (!this.job) return

    const content = this.container.querySelector('#jobDetailsContent')
    const statusClass = `status-${this.job.Status.toLowerCase().replace('_', '-')}`
    
    content.innerHTML = `
      <div class="job-details-header">
        <div class="job-details-info">
          <h1 class="job-details-title" data-testid="job-title">${this.escapeHtml(this.job.Title)}</h1>
          <div class="job-details-meta">
            <div class="job-details-meta-item">
              <div class="meta-label">Status</div>
              <div class="meta-value">
                <span class="badge badge-${this.job.Status.toLowerCase().replace('_', '-')}" data-testid="job-status">
                  ${this.formatStatus(this.job.Status)}
                </span>
              </div>
            </div>
            <div class="job-details-meta-item">
              <div class="meta-label">Repository</div>
              <div class="meta-value" data-testid="job-repository">${this.escapeHtml(this.job.CowPath)}</div>
            </div>
            <div class="job-details-meta-item">
              <div class="meta-label">Created</div>
              <div class="meta-value">${new Date(this.job.CreatedAt).toLocaleString()}</div>
            </div>
            ${this.job.StartedAt ? `
              <div class="job-details-meta-item">
                <div class="meta-label">Started</div>
                <div class="meta-value">${new Date(this.job.StartedAt).toLocaleString()}</div>
              </div>
            ` : ''}
            ${this.job.CompletedAt ? `
              <div class="job-details-meta-item">
                <div class="meta-label">Completed</div>
                <div class="meta-value" data-testid="completion-time">${new Date(this.job.CompletedAt).toLocaleString()}</div>
              </div>
            ` : ''}
            ${this.job.ExitCode !== null && this.job.ExitCode !== undefined ? `
              <div class="job-details-meta-item">
                <div class="meta-label">Exit Code</div>
                <div class="meta-value" data-testid="exit-code">${this.job.ExitCode}</div>
              </div>
            ` : ''}
          </div>
        </div>
        <div class="job-details-actions">
          ${this.canCancelJob(this.job.Status) ? `
            <button class="btn btn-warning" id="cancelJob" data-testid="cancel-job-button">
              Cancel Job
            </button>
          ` : ''}
          ${this.canDeleteJob(this.job.Status) ? `
            <button class="btn btn-error" id="deleteJob">
              Delete Job
            </button>
          ` : ''}
          <button class="btn btn-outline" id="refreshJob">
            🔄 Refresh
          </button>
          <button class="btn btn-secondary" id="backToJobs">
            ← Back to Jobs
          </button>
        </div>
      </div>

      <div class="job-details-content">
        <div class="job-output-section">
          <div class="job-output-header">
            <h3>Job Output</h3>
            <div class="output-actions">
              ${this.job.Output ? `
                <button class="btn btn-sm btn-outline" id="copyOutput">
                  📋 Copy
                </button>
                <button class="btn btn-sm btn-outline" id="downloadOutput">
                  💾 Download
                </button>
              ` : ''}
            </div>
          </div>
          <div class="job-output ${this.job.Output ? '' : 'empty'}" data-testid="job-output">
            ${this.job.Output || 'No output available yet...'}
          </div>
        </div>

        <div class="job-sidebar">
          <div class="card job-status-card">
            <div class="card-header">
              <h4>Status Timeline</h4>
            </div>
            <div class="card-body">
              <div class="status-timeline" data-testid="status-timeline">
                ${this.renderStatusTimeline()}
              </div>
            </div>
          </div>

          ${this.job.QueuePosition > 0 ? `
            <div class="card">
              <div class="card-header">
                <h4>Queue Information</h4>
              </div>
              <div class="card-body">
                <div class="queue-position">
                  Position: <strong>#${this.job.QueuePosition}</strong>
                </div>
              </div>
            </div>
          ` : ''}

          <div class="card">
            <div class="card-header">
              <h4>Git Status</h4>
            </div>
            <div class="card-body">
              <div class="git-status">
                Status: <span class="badge badge-${this.job.GitStatus}">${this.job.GitStatus}</span>
              </div>
            </div>
          </div>

          <div class="card">
            <div class="card-header">
              <h4>Cidx Status</h4>
            </div>
            <div class="card-body">
              <div class="cidx-status">
                Status: <span class="badge badge-${this.job.CidxStatus}">${this.job.CidxStatus}</span>
              </div>
            </div>
          </div>

          <div class="card">
            <div class="card-header">
              <h4>Workspace Files</h4>
            </div>
            <div class="card-body">
              <button class="btn btn-outline btn-sm" id="browseFiles" data-testid="browse-files">
                📁 Browse Files
              </button>
            </div>
          </div>
        </div>
      </div>
    `
    
    this.bindJobDetailsEvents()
  }

  renderStatusTimeline() {
    const statuses = [
      { key: 'created', label: 'Created', icon: '⚪' },
      { key: 'queued', label: 'Queued', icon: '🔵' },
      { key: 'git_pulling', label: 'Cloning Repository', icon: '📥' },
      { key: 'cidx_indexing', label: 'Indexing Code', icon: '🔍' },
      { key: 'running', label: 'Running Claude Code', icon: '⚡' },
      { key: 'completed', label: 'Completed', icon: '✅' },
      { key: 'failed', label: 'Failed', icon: '❌' },
      { key: 'cancelled', label: 'Cancelled', icon: '⚫' },
      { key: 'timeout', label: 'Timeout', icon: '⏰' }
    ]

    const currentStatus = this.job.Status.toLowerCase()
    const currentIndex = statuses.findIndex(s => s.key === currentStatus)
    
    return statuses.map((status, index) => {
      let itemClass = 'timeline-item'
      
      if (status.key === currentStatus) {
        itemClass += ' active'
      } else if (index < currentIndex || 
                 (currentStatus === 'completed' && index <= 4) ||
                 (currentStatus === 'failed' && index <= 4) ||
                 (currentStatus === 'cancelled' && index <= currentIndex)) {
        itemClass += ' completed'
      }
      
      return `
        <div class="${itemClass}">
          <span class="timeline-dot">${status.icon}</span>
          <span class="timeline-label">${status.label}</span>
        </div>
      `
    }).join('')
  }

  bindJobDetailsEvents() {
    // Cancel job
    const cancelBtn = this.container.querySelector('#cancelJob')
    if (cancelBtn) {
      cancelBtn.addEventListener('click', () => this.confirmCancelJob())
    }

    // Delete job
    const deleteBtn = this.container.querySelector('#deleteJob')
    if (deleteBtn) {
      deleteBtn.addEventListener('click', () => this.confirmDeleteJob())
    }

    // Refresh job
    const refreshBtn = this.container.querySelector('#refreshJob')
    refreshBtn.addEventListener('click', () => this.loadJob())

    // Back to jobs
    const backBtn = this.container.querySelector('#backToJobs')
    backBtn.addEventListener('click', () => {
      if (this.options.onBack) {
        this.options.onBack()
      }
    })

    // Copy output
    const copyBtn = this.container.querySelector('#copyOutput')
    if (copyBtn) {
      copyBtn.addEventListener('click', () => this.copyOutput())
    }

    // Download output
    const downloadBtn = this.container.querySelector('#downloadOutput')
    if (downloadBtn) {
      downloadBtn.addEventListener('click', () => this.downloadOutput())
    }

    // Browse files
    const browseBtn = this.container.querySelector('#browseFiles')
    browseBtn.addEventListener('click', () => this.showFileBrowser())
  }

  startMonitoring() {
    if (this.job && this.isActiveJob(this.job.Status)) {
      this.monitor = jobMonitorManager.startMonitoring(this.jobId, {
        onStatusUpdate: (status) => {
          this.updateJobStatus(status)
        },
        onComplete: (status) => {
          this.updateJobStatus(status)
          this.showCompletionNotification(status)
        },
        onError: (error) => {
          console.error('Job monitoring error:', error)
        }
      })
    }
  }

  updateJobStatus(status) {
    this.job = { ...this.job, ...status }
    
    // Update status badge
    const statusBadge = this.container.querySelector('.badge')
    if (statusBadge) {
      statusBadge.className = `badge badge-${status.status.toLowerCase().replace('_', '-')}`
      statusBadge.textContent = this.formatStatus(status.status)
    }

    // Update timeline
    const timeline = this.container.querySelector('.status-timeline')
    if (timeline) {
      timeline.innerHTML = this.renderStatusTimeline()
    }

    // Update output
    const outputDiv = this.container.querySelector('.job-output')
    if (outputDiv && status.output) {
      outputDiv.textContent = status.output
      outputDiv.classList.remove('empty')
    }

    // Update metadata
    this.updateMetadata(status)

    // Update action buttons
    this.updateActionButtons(status.status)
  }

  updateMetadata(status) {
    if (status.startedAt) {
      const startedMeta = this.container.querySelector('.job-details-meta')
      // Add started time if not present
      if (!startedMeta.querySelector('[data-label="started"]')) {
        const startedItem = document.createElement('div')
        startedItem.className = 'job-details-meta-item'
        startedItem.setAttribute('data-label', 'started')
        startedItem.innerHTML = `
          <div class="meta-label">Started</div>
          <div class="meta-value">${new Date(status.startedAt).toLocaleString()}</div>
        `
        startedMeta.appendChild(startedItem)
      }
    }

    if (status.completedAt) {
      const completedMeta = this.container.querySelector('.job-details-meta')
      if (!completedMeta.querySelector('[data-label="completed"]')) {
        const completedItem = document.createElement('div')
        completedItem.className = 'job-details-meta-item'
        completedItem.setAttribute('data-label', 'completed')
        completedItem.innerHTML = `
          <div class="meta-label">Completed</div>
          <div class="meta-value">${new Date(status.completedAt).toLocaleString()}</div>
        `
        completedMeta.appendChild(completedItem)
      }
    }

    if (status.exitCode !== null && status.exitCode !== undefined) {
      const exitCodeMeta = this.container.querySelector('.job-details-meta')
      if (!exitCodeMeta.querySelector('[data-label="exit-code"]')) {
        const exitCodeItem = document.createElement('div')
        exitCodeItem.className = 'job-details-meta-item'
        exitCodeItem.setAttribute('data-label', 'exit-code')
        exitCodeItem.innerHTML = `
          <div class="meta-label">Exit Code</div>
          <div class="meta-value">${status.exitCode}</div>
        `
        exitCodeMeta.appendChild(exitCodeItem)
      }
    }
  }

  updateActionButtons(status) {
    const actionsDiv = this.container.querySelector('.job-details-actions')
    const cancelBtn = actionsDiv.querySelector('#cancelJob')
    const deleteBtn = actionsDiv.querySelector('#deleteJob')
    
    // Show/hide cancel button
    if (this.canCancelJob(status) && !cancelBtn) {
      const newCancelBtn = document.createElement('button')
      newCancelBtn.className = 'btn btn-warning'
      newCancelBtn.id = 'cancelJob'
      newCancelBtn.textContent = 'Cancel Job'
      newCancelBtn.addEventListener('click', () => this.confirmCancelJob())
      actionsDiv.insertBefore(newCancelBtn, actionsDiv.firstChild)
    } else if (!this.canCancelJob(status) && cancelBtn) {
      cancelBtn.remove()
    }

    // Show/hide delete button
    if (this.canDeleteJob(status) && !deleteBtn) {
      const newDeleteBtn = document.createElement('button')
      newDeleteBtn.className = 'btn btn-error'
      newDeleteBtn.id = 'deleteJob'
      newDeleteBtn.textContent = 'Delete Job'
      newDeleteBtn.addEventListener('click', () => this.confirmDeleteJob())
      actionsDiv.insertBefore(newDeleteBtn, actionsDiv.lastElementChild.previousElementSibling)
    }
  }

  async confirmCancelJob() {
    if (!confirm('Are you sure you want to cancel this job?')) {
      return
    }

    try {
      await apiClient.cancelJob(this.jobId)
      // Status will be updated via monitoring
    } catch (error) {
      console.error('Failed to cancel job:', error)
      alert('Failed to cancel job. Please try again.')
    }
  }

  async confirmDeleteJob() {
    if (!confirm('Are you sure you want to delete this job? This action cannot be undone.')) {
      return
    }

    try {
      await apiClient.deleteJob(this.jobId)
      if (this.options.onJobDeleted) {
        this.options.onJobDeleted()
      }
    } catch (error) {
      console.error('Failed to delete job:', error)
      alert('Failed to delete job. Please try again.')
    }
  }

  async copyOutput() {
    try {
      await navigator.clipboard.writeText(this.job.Output)
      // Show temporary success message
      const btn = this.container.querySelector('#copyOutput')
      const originalText = btn.textContent
      btn.textContent = '✅ Copied!'
      setTimeout(() => {
        btn.textContent = originalText
      }, 2000)
    } catch (error) {
      console.error('Failed to copy output:', error)
      alert('Failed to copy output to clipboard.')
    }
  }

  downloadOutput() {
    const blob = new Blob([this.job.Output], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `job-${this.jobId}-output.txt`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  showFileBrowser() {
    this.switchView('files')
  }
  
  bindNavEvents() {
    // Handle tab switching
    this.container.addEventListener('click', (e) => {
      if (e.target.matches('.nav-tab')) {
        const view = e.target.dataset.view
        this.switchView(view)
      }
    })
  }
  
  switchView(view) {
    if (this.currentView === view) return
    
    this.currentView = view
    
    // Update tab states
    const tabs = this.container.querySelectorAll('.nav-tab')
    tabs.forEach(tab => {
      tab.classList.toggle('active', tab.dataset.view === view)
    })
    
    // Update content visibility
    const detailsContent = this.container.querySelector('#jobDetailsContent')
    const filesContent = this.container.querySelector('#jobFilesContent')
    
    detailsContent.classList.toggle('active', view === 'details')
    detailsContent.classList.toggle('hidden', view !== 'details')
    filesContent.classList.toggle('active', view === 'files')
    filesContent.classList.toggle('hidden', view !== 'files')
    
    // Initialize file browser if switching to files view
    if (view === 'files' && !this.fileBrowser) {
      this.initFileBrowser()
    }
  }
  
  initFileBrowser() {
    const filesContainer = this.container.querySelector('#jobFilesContent')
    if (!filesContainer || this.fileBrowser) return
    
    try {
      this.fileBrowser = new FileBrowserComponent(filesContainer, this.jobId, {
        onError: (error) => {
          console.error('File browser error:', error)
        },
        onFileSelect: (file) => {
          // Handle file selection if needed
        }
      })
    } catch (error) {
      console.error('Failed to initialize file browser:', error)
      filesContainer.innerHTML = `
        <div class="error-state">
          <div class="error-icon">❌</div>
          <h3>File Browser Error</h3>
          <p>Failed to load the file browser: ${error.message}</p>
          <button class="btn btn-primary" onclick="location.reload()">
            Reload Page
          </button>
        </div>
      `
    }
  }

  showCompletionNotification(status) {
    const isSuccess = status.status.toLowerCase() === 'completed'
    const message = isSuccess 
      ? 'Job completed successfully!' 
      : `Job ${status.status.toLowerCase()}`
    
    // Simple notification - could be enhanced with a proper notification system
    if ('Notification' in window && Notification.permission === 'granted') {
      new Notification(`Claude Batch Server`, {
        body: message,
        icon: '/claude-icon.svg'
      })
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

  setLoading(loading) {
    this.isLoading = loading
    const content = this.container.querySelector('#jobDetailsContent')
    
    if (loading && !this.job) {
      content.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showError(message) {
    const content = this.container.querySelector('#jobDetailsContent')
    content.innerHTML = `
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
    if (this.monitor) {
      jobMonitorManager.stopMonitoring(this.jobId)
    }
    
    if (this.fileBrowser) {
      this.fileBrowser.destroy()
      this.fileBrowser = null
    }
  }
}