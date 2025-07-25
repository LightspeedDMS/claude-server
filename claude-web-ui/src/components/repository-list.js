import apiClient from '../services/api.js'

/**
 * Repository List Component
 * Displays all registered repositories with management functionality
 */
export class RepositoryListComponent {
  constructor(container, options = {}) {
    this.container = container
    this.options = options
    this.repositories = []
    this.filteredRepositories = []
    this.currentFilter = 'all'
    this.searchQuery = ''
    this.sortBy = 'name'
    this.sortOrder = 'asc'
    this.isLoading = false
    this.viewMode = 'grid' // 'grid' or 'list'
    
    this.init()
  }

  async init() {
    this.render()
    this.bindEvents()
    await this.loadRepositories()
    this.startPolling()
  }

  render() {
    this.container.innerHTML = `
      <div class="repository-list-container container">
        <div class="repository-list-header">
          <h1 class="repository-list-title">Repositories</h1>
          <div class="repository-list-actions">
            <button class="btn btn-secondary" id="refreshRepositories" data-testid="refresh-repositories">
              <span>🔄</span> Refresh
            </button>
            <button class="btn btn-primary" id="registerRepository" data-testid="register-repository-button">
              <span>+</span> Register Repository
            </button>
          </div>
        </div>

        <div class="repository-filters">
          <select class="filter-select" id="statusFilter" data-testid="status-filter">
            <option value="all">All Statuses</option>
            <option value="ready">Ready</option>
            <option value="cloning">Cloning</option>
            <option value="indexing">Indexing</option>
            <option value="failed">Failed</option>
          </select>

          <input 
            type="text" 
            class="repository-search" 
            id="searchRepositories" 
            placeholder="Search repositories..."
            data-testid="search-repositories"
          />

          <select class="filter-select" id="sortBy" data-testid="sort-by">
            <option value="name">Sort by Name</option>
            <option value="registered">Sort by Registered</option>
            <option value="lastPull">Sort by Last Pull</option>
            <option value="size">Sort by Size</option>
          </select>

          <div class="view-mode-toggle">
            <button class="view-mode-btn active" id="gridView" data-testid="grid-view">
              <span>⊞</span> Grid
            </button>
            <button class="view-mode-btn" id="listView" data-testid="list-view">
              <span>☰</span> List
            </button>
          </div>
        </div>

        <div id="repositoryStats" class="repository-stats"></div>

        <div id="repositoriesContainer" class="repositories-container">
          <div class="loading-spinner"></div>
        </div>
      </div>
    `
  }

  bindEvents() {
    // Refresh button
    const refreshBtn = this.container.querySelector('#refreshRepositories')
    refreshBtn.addEventListener('click', () => this.loadRepositories())

    // Register repository button
    const registerBtn = this.container.querySelector('#registerRepository')
    registerBtn.addEventListener('click', () => {
      if (this.options.onRegisterRepository) {
        this.options.onRegisterRepository()
      }
    })

    // Filter controls
    const statusFilter = this.container.querySelector('#statusFilter')
    statusFilter.addEventListener('change', (e) => {
      this.currentFilter = e.target.value
      this.filterAndRenderRepositories()
    })

    const searchInput = this.container.querySelector('#searchRepositories')
    searchInput.addEventListener('input', (e) => {
      this.searchQuery = e.target.value.toLowerCase()
      this.filterAndRenderRepositories()
    })

    const sortSelect = this.container.querySelector('#sortBy')
    sortSelect.addEventListener('change', (e) => {
      this.sortBy = e.target.value
      this.filterAndRenderRepositories()
    })

    // View mode toggle
    const gridViewBtn = this.container.querySelector('#gridView')
    const listViewBtn = this.container.querySelector('#listView')
    
    gridViewBtn.addEventListener('click', () => {
      this.viewMode = 'grid'
      this.updateViewModeButtons()
      this.renderRepositories()
    })

    listViewBtn.addEventListener('click', () => {
      this.viewMode = 'list'
      this.updateViewModeButtons()
      this.renderRepositories()
    })
  }

  updateViewModeButtons() {
    const gridBtn = this.container.querySelector('#gridView')
    const listBtn = this.container.querySelector('#listView')
    
    gridBtn.classList.toggle('active', this.viewMode === 'grid')
    listBtn.classList.toggle('active', this.viewMode === 'list')
  }

  async loadRepositories() {
    this.setLoading(true)
    
    try {
      this.repositories = await apiClient.getRepositories()
      this.filterAndRenderRepositories()
      this.updateStats()
    } catch (error) {
      console.error('Failed to load repositories:', error)
      this.showError('Failed to load repositories. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  filterAndRenderRepositories() {
    // Apply filters
    this.filteredRepositories = this.repositories.filter(repo => {
      // Status filter
      if (this.currentFilter !== 'all') {
        const status = this.getRepositoryStatus(repo)
        if (status !== this.currentFilter) {
          return false
        }
      }

      // Search filter
      if (this.searchQuery) {
        const searchTerm = this.searchQuery.toLowerCase()
        return (
          repo.name.toLowerCase().includes(searchTerm) ||
          (repo.description && repo.description.toLowerCase().includes(searchTerm)) ||
          (repo.gitUrl && repo.gitUrl.toLowerCase().includes(searchTerm))
        )
      }

      return true
    })

    // Apply sorting
    this.filteredRepositories.sort((a, b) => {
      let valueA, valueB

      switch (this.sortBy) {
        case 'name':
          valueA = a.name.toLowerCase()
          valueB = b.name.toLowerCase()
          break
        case 'registered':
          valueA = new Date(a.registeredAt || 0)
          valueB = new Date(b.registeredAt || 0)
          break
        case 'lastPull':
          valueA = new Date(a.lastPull || 0)
          valueB = new Date(b.lastPull || 0)
          break
        case 'size':
          valueA = a.size || 0
          valueB = b.size || 0
          break
        default:
          valueA = a.name.toLowerCase()
          valueB = b.name.toLowerCase()
          break
      }

      if (valueA < valueB) return this.sortOrder === 'asc' ? -1 : 1
      if (valueA > valueB) return this.sortOrder === 'asc' ? 1 : -1
      return 0
    })

    this.renderRepositories()
  }

  renderRepositories() {
    const container = this.container.querySelector('#repositoriesContainer')
    
    if (this.filteredRepositories.length === 0) {
      container.innerHTML = `
        <div class="empty-state">
          <h3>No repositories found</h3>
          <p>
            ${this.currentFilter !== 'all' || this.searchQuery 
              ? 'Try adjusting your filters or search terms.' 
              : 'Register your first repository to get started.'}
          </p>
          ${this.currentFilter === 'all' && !this.searchQuery ? `
            <button class="btn btn-primary" onclick="document.getElementById('registerRepository').click()">
              Register Your First Repository
            </button>
          ` : ''}
        </div>
      `
      return
    }

    const repositoriesHtml = this.filteredRepositories.map(repo => 
      this.viewMode === 'grid' ? this.renderRepositoryCard(repo) : this.renderRepositoryRow(repo)
    ).join('')
    
    const containerClass = this.viewMode === 'grid' ? 'repository-grid' : 'repository-list-view'
    
    container.innerHTML = `
      <div class="${containerClass}" data-testid="repository-list">
        ${repositoriesHtml}
      </div>
    `
    
    this.bindRepositoryEvents()
  }

  renderRepositoryCard(repo) {
    const status = this.getRepositoryStatus(repo)
    const statusClass = `status-${status}`
    const statusText = this.formatStatus(status)
    const registeredAt = repo.registeredAt ? new Date(repo.registeredAt).toLocaleDateString() : 'Unknown'
    const lastPull = repo.lastPull ? new Date(repo.lastPull).toLocaleDateString() : 'Never'
    const size = this.formatSize(repo.size || 0)
    
    return `
      <div class="card repository-card ${statusClass}" data-repo-name="${repo.name}" data-testid="repository-item">
        <div class="card-body">
          <div class="repository-card-header">
            <h3 class="repository-title" data-testid="repository-name">${this.escapeHtml(repo.name)}</h3>
            <div class="repository-status">
              <div class="status-indicator status-${status}"></div>
              <span class="badge badge-${status}" data-testid="repository-status">
                ${statusText}
              </span>
            </div>
          </div>

          <div class="repository-meta">
            ${repo.description ? `
              <div class="repository-description" data-testid="repository-description">
                ${this.escapeHtml(repo.description)}
              </div>
            ` : ''}
            
            <div class="repository-details">
              <div class="detail-item">
                <span class="detail-label">Git URL:</span>
                <span class="detail-value" data-testid="repository-git-url">${this.escapeHtml(repo.gitUrl || 'N/A')}</span>
              </div>
              <div class="detail-item">
                <span class="detail-label">Size:</span>
                <span class="detail-value">${size}</span>
              </div>
              <div class="detail-item">
                <span class="detail-label">Registered:</span>
                <span class="detail-value">${registeredAt}</span>
              </div>
              <div class="detail-item">
                <span class="detail-label">Last Pull:</span>
                <span class="detail-value" data-testid="repository-last-pull">${lastPull}</span>
              </div>
              ${repo.cidxAware !== undefined ? `
                <div class="detail-item">
                  <span class="detail-label">Cidx Aware:</span>
                  <span class="detail-value cidx-aware-${repo.cidxAware}" data-testid="repository-cidx-aware">
                    ${repo.cidxAware ? '🧠 Yes' : '❌ No'}
                  </span>
                </div>
              ` : ''}
            </div>

            ${this.renderGitMetadata(repo)}
          </div>

          <div class="repository-actions">
            <button class="btn btn-sm btn-outline view-repository" data-repo-name="${repo.name}" data-testid="view-repository">
              View Details
            </button>
            ${status === 'ready' ? `
              <button class="btn btn-sm btn-secondary refresh-repository" data-repo-name="${repo.name}" data-testid="refresh-repository">
                Refresh
              </button>
            ` : ''}
            <button class="btn btn-sm btn-error unregister-repository" data-repo-name="${repo.name}" data-testid="unregister-repository">
              Unregister
            </button>
          </div>
        </div>
      </div>
    `
  }

  renderRepositoryRow(repo) {
    const status = this.getRepositoryStatus(repo)
    const statusClass = `status-${status}`
    const statusText = this.formatStatus(status)
    const registeredAt = repo.registeredAt ? new Date(repo.registeredAt).toLocaleDateString() : 'Unknown'
    const lastPull = repo.lastPull ? new Date(repo.lastPull).toLocaleDateString() : 'Never'
    const size = this.formatSize(repo.size || 0)
    
    return `
      <div class="repository-row ${statusClass}" data-repo-name="${repo.name}" data-testid="repository-item">
        <div class="repository-row-content">
          <div class="repository-basic-info">
            <div class="repository-name-status">
              <h3 class="repository-title" data-testid="repository-name">${this.escapeHtml(repo.name)}</h3>
              <span class="badge badge-${status}" data-testid="repository-status">${statusText}</span>
            </div>
            ${repo.description ? `
              <div class="repository-description" data-testid="repository-description">
                ${this.escapeHtml(repo.description)}
              </div>
            ` : ''}
          </div>

          <div class="repository-metadata">
            <div class="metadata-column">
              <div class="metadata-item">
                <span class="metadata-label">Git URL:</span>
                <span class="metadata-value" data-testid="repository-git-url">${this.escapeHtml(repo.gitUrl || 'N/A')}</span>
              </div>
              <div class="metadata-item">
                <span class="metadata-label">Size:</span>
                <span class="metadata-value">${size}</span>
              </div>
            </div>
            <div class="metadata-column">
              <div class="metadata-item">
                <span class="metadata-label">Registered:</span>
                <span class="metadata-value">${registeredAt}</span>
              </div>
              <div class="metadata-item">
                <span class="metadata-label">Last Pull:</span>
                <span class="metadata-value" data-testid="repository-last-pull">${lastPull}</span>
              </div>
              ${repo.cidxAware !== undefined ? `
                <div class="metadata-item">
                  <span class="metadata-label">Cidx Aware:</span>
                  <span class="metadata-value cidx-aware-${repo.cidxAware}" data-testid="repository-cidx-aware">
                    ${repo.cidxAware ? '🧠 Yes' : '❌ No'}
                  </span>
                </div>
              ` : ''}
            </div>
          </div>

          ${this.renderGitMetadata(repo)}
        </div>

        <div class="repository-actions">
          <button class="btn btn-sm btn-outline view-repository" data-repo-name="${repo.name}" data-testid="view-repository">
            View Details
          </button>
          ${status === 'ready' ? `
            <button class="btn btn-sm btn-secondary refresh-repository" data-repo-name="${repo.name}" data-testid="refresh-repository">
              Refresh
            </button>
          ` : ''}
          <button class="btn btn-sm btn-error unregister-repository" data-repo-name="${repo.name}" data-testid="unregister-repository">
            Unregister
          </button>
        </div>
      </div>
    `
  }

  renderGitMetadata(repo) {
    if (!repo.currentBranch && !repo.commitHash) {
      return ''
    }

    return `
      <div class="git-metadata">
        ${repo.currentBranch ? `
          <div class="git-item">
            <span class="git-label">Branch:</span>
            <span class="git-value" data-testid="repository-branch">${this.escapeHtml(repo.currentBranch)}</span>
          </div>
        ` : ''}
        ${repo.commitHash ? `
          <div class="git-item">
            <span class="git-label">Commit:</span>
            <span class="git-value git-commit" data-testid="repository-commit">${repo.commitHash.substring(0, 8)}</span>
          </div>
        ` : ''}
        ${repo.commitAuthor ? `
          <div class="git-item">
            <span class="git-label">Author:</span>
            <span class="git-value" data-testid="repository-author">${this.escapeHtml(repo.commitAuthor)}</span>
          </div>
        ` : ''}
        ${repo.commitDate ? `
          <div class="git-item">
            <span class="git-label">Date:</span>
            <span class="git-value">${new Date(repo.commitDate).toLocaleDateString()}</span>
          </div>
        ` : ''}
        ${repo.aheadBehind ? `
          <div class="git-item">
            <span class="git-label">Status:</span>
            <span class="git-value">
              ${repo.aheadBehind.ahead > 0 ? `↑${repo.aheadBehind.ahead}` : ''}
              ${repo.aheadBehind.behind > 0 ? `↓${repo.aheadBehind.behind}` : ''}
              ${repo.aheadBehind.ahead === 0 && repo.aheadBehind.behind === 0 ? '✓ Up to date' : ''}
            </span>
          </div>
        ` : ''}
      </div>
    `
  }

  bindRepositoryEvents() {
    // View repository details
    this.container.querySelectorAll('.view-repository').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const repoName = e.target.dataset.repoName
        if (this.options.onViewRepository) {
          this.options.onViewRepository(repoName)
        }
      })
    })

    // Refresh repository
    this.container.querySelectorAll('.refresh-repository').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const repoName = e.target.dataset.repoName
        this.refreshRepository(repoName)
      })
    })

    // Unregister repository
    this.container.querySelectorAll('.unregister-repository').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const repoName = e.target.dataset.repoName
        this.confirmUnregisterRepository(repoName)
      })
    })

    // Click on repository card to view details
    this.container.querySelectorAll('.repository-card, .repository-row').forEach(card => {
      card.addEventListener('click', (e) => {
        // Don't trigger if clicking on buttons
        if (e.target.closest('button')) return
        
        const repoName = card.dataset.repoName
        if (this.options.onViewRepository) {
          this.options.onViewRepository(repoName)
        }
      })
    })
  }

  async refreshRepository(repoName) {
    try {
      // Show loading state for this specific repository
      const repoElement = this.container.querySelector(`[data-repo-name="${repoName}"]`)
      if (repoElement) {
        repoElement.classList.add('refreshing')
      }

      // Trigger a repository refresh by doing a git pull
      // This would typically be a separate API endpoint
      await this.loadRepositories() // For now, just reload all repositories
      
    } catch (error) {
      console.error('Failed to refresh repository:', error)
      alert('Failed to refresh repository. Please try again.')
    }
  }

  async confirmUnregisterRepository(repoName) {
    if (!confirm(`Are you sure you want to unregister repository "${repoName}"? This will remove it from the server but won't delete the original repository.`)) {
      return
    }

    try {
      await apiClient.deleteRepository(repoName)
      this.loadRepositories() // Refresh the list
    } catch (error) {
      console.error('Failed to unregister repository:', error)
      alert('Failed to unregister repository. Please try again.')
    }
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

  updateStats() {
    const stats = this.calculateStats()
    const statsContainer = this.container.querySelector('#repositoryStats')
    
    statsContainer.innerHTML = `
      <div class="repository-stats-grid">
        <div class="stat-item">
          <span class="stat-value">${stats.total}</span>
          <span class="stat-label">Total Repositories</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-success">${stats.ready}</span>
          <span class="stat-label">Ready</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-warning">${stats.cloning}</span>
          <span class="stat-label">Cloning</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-info">${stats.indexing}</span>
          <span class="stat-label">Indexing</span>
        </div>
        <div class="stat-item">
          <span class="stat-value text-error">${stats.failed}</span>
          <span class="stat-label">Failed</span>
        </div>
        <div class="stat-item">
          <span class="stat-value">${this.formatSize(stats.totalSize)}</span>
          <span class="stat-label">Total Size</span>
        </div>
      </div>
    `
  }

  calculateStats() {
    return {
      total: this.repositories.length,
      ready: this.repositories.filter(r => this.getRepositoryStatus(r) === 'ready').length,
      cloning: this.repositories.filter(r => this.getRepositoryStatus(r) === 'cloning').length,
      indexing: this.repositories.filter(r => this.getRepositoryStatus(r) === 'indexing').length,
      failed: this.repositories.filter(r => this.getRepositoryStatus(r) === 'failed').length,
      totalSize: this.repositories.reduce((sum, r) => sum + (r.size || 0), 0)
    }
  }

  startPolling() {
    // Poll for status updates every 5 seconds for repositories that are cloning or indexing
    this.pollingInterval = setInterval(() => {
      const hasActiveProcessing = this.repositories.some(r => {
        const status = this.getRepositoryStatus(r)
        return status === 'cloning' || status === 'indexing'
      })
      if (hasActiveProcessing) {
        this.loadRepositories()
      }
    }, 5000)
  }

  setLoading(loading) {
    this.isLoading = loading
    const container = this.container.querySelector('#repositoriesContainer')
    
    if (loading && this.repositories.length === 0) {
      container.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showError(message) {
    const container = this.container.querySelector('#repositoriesContainer')
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
    if (this.pollingInterval) {
      clearInterval(this.pollingInterval)
    }
  }
}