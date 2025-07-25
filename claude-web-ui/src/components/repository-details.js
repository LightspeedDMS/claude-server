import apiClient from '../services/api.js'

/**
 * Repository Details Component
 * Shows detailed information about a specific repository
 */
export class RepositoryDetailsComponent {
  constructor(container, repositoryName, options = {}) {
    this.container = container
    this.repositoryName = repositoryName
    this.options = options
    this.repository = null
    this.isLoading = false
    
    this.init()
  }

  async init() {
    this.render()
    this.bindEvents()
    await this.loadRepository()
    this.startPolling()
  }

  render() {
    this.container.innerHTML = `
      <div class="repository-details-container">
        <div class="repository-details-header">
          <button class="btn btn-outline back-button" id="backButton" data-testid="back-button">
            <span>←</span> Back to Repositories
          </button>
          <div class="repository-title-section">
            <h1 class="repository-details-title" data-testid="repository-title">
              ${this.escapeHtml(this.repositoryName)}
            </h1>
            <div class="repository-status-badge" id="statusBadge"></div>
          </div>
          <div class="repository-actions">
            <button class="btn btn-secondary" id="refreshRepository" data-testid="refresh-repository">
              <span>🔄</span> Refresh
            </button>
            <button class="btn btn-primary" id="createJob" data-testid="create-job">
              <span>+</span> Create Job
            </button>
          </div>
        </div>

        <div id="repositoryContent" class="repository-content">
          <div class="loading-spinner"></div>
        </div>
      </div>
    `
  }

  bindEvents() {
    const backButton = this.container.querySelector('#backButton')
    const refreshButton = this.container.querySelector('#refreshRepository')
    const createJobButton = this.container.querySelector('#createJob')

    backButton.addEventListener('click', () => {
      if (this.options.onBack) {
        this.options.onBack()
      }
    })

    refreshButton.addEventListener('click', () => {
      this.loadRepository()
    })

    createJobButton.addEventListener('click', () => {
      if (this.options.onCreateJob) {
        this.options.onCreateJob(this.repositoryName)
      }
    })
  }

  async loadRepository() {
    this.setLoading(true)
    
    try {
      this.repository = await apiClient.getRepository(this.repositoryName)
      this.renderRepository()
      this.updateStatusBadge()
    } catch (error) {
      console.error('Failed to load repository:', error)
      this.showError('Failed to load repository details. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  renderRepository() {
    if (!this.repository) return

    const contentContainer = this.container.querySelector('#repositoryContent')
    const status = this.getRepositoryStatus(this.repository)
    
    contentContainer.innerHTML = `
      <div class="repository-details-grid">
        <!-- Basic Information -->
        <div class="card repository-info-card">
          <div class="card-header">
            <h2>Repository Information</h2>
          </div>
          <div class="card-body">
            <div class="info-grid">
              <div class="info-item">
                <label class="info-label">Name</label>
                <span class="info-value" data-testid="repository-name">${this.escapeHtml(this.repository.name)}</span>
              </div>
              
              <div class="info-item">
                <label class="info-label">Status</label>
                <span class="badge badge-${status}" data-testid="repository-status">${this.formatStatus(status)}</span>
              </div>
              
              <div class="info-item">
                <label class="info-label">Type</label>
                <span class="info-value">${this.repository.type || 'Git'}</span>
              </div>
              
              <div class="info-item">
                <label class="info-label">Size</label>
                <span class="info-value" data-testid="repository-size">${this.formatSize(this.repository.size || 0)}</span>
              </div>

              ${this.repository.cidxAware !== undefined ? `
                <div class="info-item">
                  <label class="info-label">Cidx Aware</label>
                  <span class="info-value cidx-aware-${this.repository.cidxAware}" data-testid="repository-cidx-aware">
                    ${this.repository.cidxAware ? '🧠 Yes' : '❌ No'}
                  </span>
                </div>
              ` : ''}
              
              ${this.repository.description ? `
                <div class="info-item info-item-full">
                  <label class="info-label">Description</label>
                  <span class="info-value" data-testid="repository-description">${this.escapeHtml(this.repository.description)}</span>
                </div>
              ` : ''}
              
              <div class="info-item">
                <label class="info-label">Git URL</label>
                <span class="info-value git-url" data-testid="repository-git-url">
                  <a href="${this.escapeHtml(this.repository.gitUrl || '')}" target="_blank" rel="noopener noreferrer">
                    ${this.escapeHtml(this.repository.gitUrl || 'N/A')}
                  </a>
                </span>
              </div>
              
              <div class="info-item">
                <label class="info-label">Local Path</label>
                <span class="info-value code" data-testid="repository-path">${this.escapeHtml(this.repository.path || 'N/A')}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Git Metadata -->
        ${this.renderGitMetadata()}

        <!-- Timeline -->
        ${this.renderTimeline()}

        <!-- Clone Status Details -->
        ${status === 'cloning' ? this.renderCloneProgress() : ''}
        ${status === 'indexing' ? this.renderIndexingProgress() : ''}
        
        <!-- Actions -->
        <div class="card repository-actions-card">
          <div class="card-header">
            <h2>Actions</h2>
          </div>
          <div class="card-body">
            <div class="actions-grid">
              <button class="btn btn-outline action-btn" id="browseFiles" data-testid="browse-files" ${status !== 'ready' ? 'disabled' : ''}>
                <span class="action-icon">📁</span>
                <div class="action-text">
                  <div class="action-title">Browse Files</div>
                  <div class="action-description">Explore repository contents</div>
                </div>
              </button>
              
              <button class="btn btn-outline action-btn" id="createJobAction" data-testid="create-job-action">
                <span class="action-icon">⚡</span>
                <div class="action-text">
                  <div class="action-title">Create Job</div>
                  <div class="action-description">Run Claude Code on this repository</div>
                </div>
              </button>
              
              <button class="btn btn-outline action-btn" id="pullRepository" data-testid="pull-repository" ${status !== 'ready' ? 'disabled' : ''}>
                <span class="action-icon">⬇️</span>
                <div class="action-text">
                  <div class="action-title">Pull Latest</div>
                  <div class="action-description">Update from remote repository</div>
                </div>
              </button>
              
              <button class="btn btn-error action-btn" id="unregisterRepository" data-testid="unregister-repository">
                <span class="action-icon">🗑️</span>
                <div class="action-text">
                  <div class="action-title">Unregister</div>
                  <div class="action-description">Remove from server</div>
                </div>
              </button>
            </div>
          </div>
        </div>
      </div>
    `

    this.bindRepositoryEvents()
  }

  renderGitMetadata() {
    if (!this.repository) return ''

    const hasGitData = this.repository.currentBranch || this.repository.commitHash || this.repository.remoteUrl

    if (!hasGitData) {
      return `
        <div class="card git-metadata-card">
          <div class="card-header">
            <h2>Git Information</h2>
          </div>
          <div class="card-body">
            <p class="text-muted">Git information will be available after successful clone.</p>
          </div>
        </div>
      `
    }

    return `
      <div class="card git-metadata-card">
        <div class="card-header">
          <h2>Git Information</h2>
        </div>
        <div class="card-body">
          <div class="git-info-grid">
            ${this.repository.currentBranch ? `
              <div class="git-info-item">
                <label class="git-info-label">Current Branch</label>
                <span class="git-info-value branch-name" data-testid="repository-branch">
                  <span class="branch-icon">🌿</span> ${this.escapeHtml(this.repository.currentBranch)}
                </span>
              </div>
            ` : ''}
            
            ${this.repository.remoteUrl ? `
              <div class="git-info-item">
                <label class="git-info-label">Remote URL</label>
                <span class="git-info-value" data-testid="repository-remote-url">${this.escapeHtml(this.repository.remoteUrl)}</span>
              </div>
            ` : ''}
            
            ${this.repository.commitHash ? `
              <div class="git-info-item">
                <label class="git-info-label">Latest Commit</label>
                <div class="commit-info" data-testid="repository-commit-info">
                  <div class="commit-hash" data-testid="repository-commit-hash">
                    <span class="commit-icon">📝</span> ${this.repository.commitHash.substring(0, 8)}...
                  </div>
                  ${this.repository.commitMessage ? `
                    <div class="commit-message" data-testid="repository-commit-message">
                      ${this.escapeHtml(this.repository.commitMessage)}
                    </div>
                  ` : ''}
                  ${this.repository.commitAuthor ? `
                    <div class="commit-author" data-testid="repository-commit-author">
                      by ${this.escapeHtml(this.repository.commitAuthor)}
                    </div>
                  ` : ''}
                  ${this.repository.commitDate ? `
                    <div class="commit-date">
                      ${new Date(this.repository.commitDate).toLocaleString()}
                    </div>
                  ` : ''}
                </div>
              </div>
            ` : ''}
            
            ${this.repository.aheadBehind ? `
              <div class="git-info-item">
                <label class="git-info-label">Sync Status</label>
                <div class="sync-status" data-testid="repository-sync-status">
                  ${this.repository.aheadBehind.ahead === 0 && this.repository.aheadBehind.behind === 0 ? `
                    <span class="sync-up-to-date">✅ Up to date</span>
                  ` : `
                    ${this.repository.aheadBehind.ahead > 0 ? `<span class="sync-ahead">↑ ${this.repository.aheadBehind.ahead} ahead</span>` : ''}
                    ${this.repository.aheadBehind.behind > 0 ? `<span class="sync-behind">↓ ${this.repository.aheadBehind.behind} behind</span>` : ''}
                  `}
                </div>
              </div>
            ` : ''}
            
            ${this.repository.hasUncommittedChanges !== undefined ? `
              <div class="git-info-item">
                <label class="git-info-label">Working Directory</label>
                <span class="working-dir-status">
                  ${this.repository.hasUncommittedChanges ? 
                    '<span class="uncommitted-changes">⚠️ Has uncommitted changes</span>' : 
                    '<span class="clean">✅ Clean'
                  }
                </span>
              </div>
            ` : ''}
          </div>
        </div>
      </div>
    `
  }

  renderTimeline() {
    if (!this.repository) return ''

    const events = []

    if (this.repository.registeredAt) {
      events.push({
        type: 'registered',
        date: this.repository.registeredAt,
        title: 'Repository Registered',
        description: 'Repository was registered and clone started'
      })
    }

    if (this.repository.lastPull) {
      events.push({
        type: 'pull',
        date: this.repository.lastPull,
        title: 'Last Pull',
        description: `Status: ${this.repository.lastPullStatus || 'Unknown'}`,
        status: this.repository.lastPullStatus
      })
    }

    if (this.repository.lastModified) {
      events.push({
        type: 'modified',
        date: this.repository.lastModified,
        title: 'Last Modified',
        description: 'Repository contents were last updated'
      })
    }

    // Sort events by date (newest first)
    events.sort((a, b) => new Date(b.date) - new Date(a.date))

    return `
      <div class="card timeline-card">
        <div class="card-header">
          <h2>Timeline</h2>
        </div>
        <div class="card-body">
          ${events.length === 0 ? `
            <p class="text-muted">No timeline events available.</p>
          ` : `
            <div class="timeline">
              ${events.map(event => `
                <div class="timeline-item">
                  <div class="timeline-marker timeline-marker-${event.type}"></div>
                  <div class="timeline-content">
                    <div class="timeline-title">${event.title}</div>
                    <div class="timeline-description">${event.description}</div>
                    <div class="timeline-date">${new Date(event.date).toLocaleString()}</div>
                  </div>
                </div>
              `).join('')}
            </div>
          `}
        </div>
      </div>
    `
  }

  renderCloneProgress() {
    return `
      <div class="card clone-progress-card">
        <div class="card-header">
          <h2>Clone Progress</h2>
        </div>
        <div class="card-body">
          <div class="clone-status">
            <div class="clone-status-icon">
              <div class="spinner"></div>
            </div>
            <div class="clone-status-text">
              <div class="clone-status-title">Cloning Repository</div>
              <div class="clone-status-description">
                The repository is being cloned from the remote source. This may take a few minutes depending on the repository size.
              </div>
            </div>
          </div>
          <div class="clone-progress-bar">
            <div class="progress-bar-track">
              <div class="progress-bar-fill indeterminate"></div>
            </div>
          </div>
        </div>
      </div>
    `
  }

  renderIndexingProgress() {
    return `
      <div class="card indexing-progress-card">
        <div class="card-header">
          <h2>Semantic Indexing Progress</h2>
        </div>
        <div class="card-body">
          <div class="indexing-status">
            <div class="indexing-status-icon">
              <div class="spinner"></div>
            </div>
            <div class="indexing-status-text">
              <div class="indexing-status-title">Building Semantic Index</div>
              <div class="indexing-status-description">
                Creating semantic index with cidx for enhanced search capabilities. This process may take some time for large repositories.
              </div>
            </div>
          </div>
          <div class="indexing-progress-bar">
            <div class="progress-bar-track">
              <div class="progress-bar-fill indeterminate"></div>
            </div>
          </div>
        </div>
      </div>
    `
  }

  bindRepositoryEvents() {
    const browseFilesBtn = this.container.querySelector('#browseFiles')
    const createJobBtn = this.container.querySelector('#createJobAction')
    const pullBtn = this.container.querySelector('#pullRepository')
    const unregisterBtn = this.container.querySelector('#unregisterRepository')

    if (browseFilesBtn) {
      browseFilesBtn.addEventListener('click', () => {
        if (this.options.onBrowseFiles) {
          this.options.onBrowseFiles(this.repositoryName)
        }
      })
    }

    if (createJobBtn) {
      createJobBtn.addEventListener('click', () => {
        if (this.options.onCreateJob) {
          this.options.onCreateJob(this.repositoryName)
        }
      })
    }

    if (pullBtn) {
      pullBtn.addEventListener('click', () => {
        this.pullRepository()
      })
    }

    if (unregisterBtn) {
      unregisterBtn.addEventListener('click', () => {
        this.confirmUnregisterRepository()
      })
    }
  }

  async pullRepository() {
    try {
      // For now, just refresh - in a real implementation, this would trigger a git pull
      await this.loadRepository()
    } catch (error) {
      console.error('Failed to pull repository:', error)
      alert('Failed to pull repository. Please try again.')
    }
  }

  async confirmUnregisterRepository() {
    if (!confirm(`Are you sure you want to unregister repository "${this.repositoryName}"? This will remove it from the server but won't delete the original repository.`)) {
      return
    }

    try {
      await apiClient.deleteRepository(this.repositoryName)
      
      if (this.options.onUnregistered) {
        this.options.onUnregistered(this.repositoryName)
      }
    } catch (error) {
      console.error('Failed to unregister repository:', error)
      alert('Failed to unregister repository. Please try again.')
    }
  }

  updateStatusBadge() {
    if (!this.repository) return

    const statusBadge = this.container.querySelector('#statusBadge')
    const status = this.getRepositoryStatus(this.repository)
    
    statusBadge.innerHTML = `
      <span class="badge badge-${status}" data-testid="repository-status-badge">
        ${this.formatStatus(status)}
      </span>
    `
  }

  getRepositoryStatus(repo) {
    if (repo.cloneStatus) {
      switch (repo.cloneStatus.toLowerCase()) {
        case 'completed':
        case 'ready':
          return 'ready'
        case 'cloning':
        case 'git_pulling':
          return 'cloning'
        case 'cidx_indexing':
          return 'indexing'
        case 'failed':
        case 'git_failed':
        case 'cidx_failed':
          return 'failed'
        default:
          return 'unknown'
      }
    }
    return 'unknown'
  }

  formatStatus(status) {
    const statusMap = {
      'ready': 'Ready',
      'cloning': 'Cloning',
      'indexing': 'Indexing',
      'failed': 'Failed',
      'unknown': 'Unknown'
    }
    
    return statusMap[status] || status
  }

  formatSize(bytes) {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  startPolling() {
    // Poll for status updates every 5 seconds if repository is cloning or indexing
    this.pollingInterval = setInterval(() => {
      if (this.repository) {
        const status = this.getRepositoryStatus(this.repository)
        if (status === 'cloning' || status === 'indexing') {
          this.loadRepository()
        }
      }
    }, 5000)
  }

  setLoading(loading) {
    this.isLoading = loading
    const contentContainer = this.container.querySelector('#repositoryContent')
    
    if (loading) {
      contentContainer.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showError(message) {
    const contentContainer = this.container.querySelector('#repositoryContent')
    contentContainer.innerHTML = `
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
    if (!text) return ''
    const div = document.createElement('div')
    div.textContent = text
    return div.innerHTML
  }

  destroy() {
    if (this.pollingInterval) {
      clearInterval(this.pollingInterval)
    }
  }
}