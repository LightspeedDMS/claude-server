import apiClient from '../services/api.js'

/**
 * Repository Browser Component
 * Allows browsing and downloading files from repositories
 */
export class RepositoryBrowserComponent {
  constructor(container, repositoryName, options = {}) {
    this.container = container
    this.repositoryName = repositoryName
    this.options = options
    this.currentPath = ''
    this.fileTree = null
    this.breadcrumbs = []
    this.isLoading = false
    this.selectedFiles = new Set()
    
    this.init()
  }

  async init() {
    this.render()
    this.bindEvents()
    await this.loadFileTree()
  }

  render() {
    this.container.innerHTML = `
      <div class="repository-browser-container">
        <div class="repository-browser-header">
          <button class="btn btn-outline back-button" id="backButton" data-testid="back-button">
            <span>←</span> Back to Repository
          </button>
          <div class="repository-browser-title-section">
            <h1 class="repository-browser-title" data-testid="repository-browser-title">
              Browse ${this.escapeHtml(this.repositoryName)}
            </h1>
            <div class="breadcrumb-container" id="breadcrumbContainer"></div>
          </div>
          <div class="repository-browser-actions">
            <button class="btn btn-secondary" id="refreshBrowser" data-testid="refresh-browser">
              <span>🔄</span> Refresh
            </button>
            <button class="btn btn-primary" id="downloadSelected" data-testid="download-selected" disabled>
              <span>⬇️</span> Download Selected
            </button>
          </div>
        </div>

        <div class="browser-toolbar">
          <div class="browser-filters">
            <input 
              type="text" 
              class="file-search" 
              id="fileSearch" 
              placeholder="Search files..."
              data-testid="file-search"
            />
            <select class="filter-select" id="fileTypeFilter" data-testid="file-type-filter">
              <option value="all">All Files</option>
              <option value="code">Code Files</option>
              <option value="docs">Documentation</option>
              <option value="config">Configuration</option>
              <option value="data">Data Files</option>
            </select>
          </div>
          <div class="browser-view-options">
            <button class="view-mode-btn active" id="treeView" data-testid="tree-view">
              <span>🌳</span> Tree
            </button>
            <button class="view-mode-btn" id="listView" data-testid="list-view">
              <span>📋</span> List
            </button>
          </div>
        </div>

        <div id="browserContent" class="browser-content">
          <div class="loading-spinner"></div>
        </div>
      </div>
    `
  }

  bindEvents() {
    const backButton = this.container.querySelector('#backButton')
    const refreshButton = this.container.querySelector('#refreshBrowser')
    const downloadButton = this.container.querySelector('#downloadSelected')
    const searchInput = this.container.querySelector('#fileSearch')
    const typeFilter = this.container.querySelector('#fileTypeFilter')
    const treeViewBtn = this.container.querySelector('#treeView')
    const listViewBtn = this.container.querySelector('#listView')

    backButton.addEventListener('click', () => {
      if (this.options.onBack) {
        this.options.onBack()
      }
    })

    refreshButton.addEventListener('click', () => {
      this.loadFileTree()
    })

    downloadButton.addEventListener('click', () => {
      this.downloadSelectedFiles()
    })

    searchInput.addEventListener('input', (e) => {
      this.filterFiles(e.target.value)
    })

    typeFilter.addEventListener('change', (e) => {
      this.filterByType(e.target.value)
    })

    treeViewBtn.addEventListener('click', () => {
      this.setViewMode('tree')
    })

    listViewBtn.addEventListener('click', () => {
      this.setViewMode('list')
    })
  }

  async loadFileTree(path = '') {
    this.setLoading(true)
    
    try {
      this.fileTree = await apiClient.browseRepository(this.repositoryName, path || null)
      this.currentPath = path
      this.updateBreadcrumbs()
      this.renderFileTree()
    } catch (error) {
      console.error('Failed to load file tree:', error)
      this.showError('Failed to load repository files. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  updateBreadcrumbs() {
    const breadcrumbContainer = this.container.querySelector('#breadcrumbContainer')
    
    if (!this.currentPath) {
      breadcrumbContainer.innerHTML = `
        <div class="breadcrumb">
          <span class="breadcrumb-item active">📁 Root</span>
        </div>
      `
      return
    }

    const pathParts = this.currentPath.split('/').filter(part => part)
    let currentPath = ''
    
    const breadcrumbItems = [`
      <button class="breadcrumb-item breadcrumb-link" data-path="" data-testid="breadcrumb-root">
        📁 Root
      </button>
    `]

    pathParts.forEach((part, index) => {
      currentPath += (currentPath ? '/' : '') + part
      const isLast = index === pathParts.length - 1
      
      if (isLast) {
        breadcrumbItems.push(`
          <span class="breadcrumb-item active" data-testid="breadcrumb-current">
            📁 ${this.escapeHtml(part)}
          </span>
        `)
      } else {
        breadcrumbItems.push(`
          <button class="breadcrumb-item breadcrumb-link" data-path="${currentPath}" data-testid="breadcrumb-link">
            📁 ${this.escapeHtml(part)}
          </button>
        `)
      }
    })

    breadcrumbContainer.innerHTML = `
      <div class="breadcrumb">
        ${breadcrumbItems.join('<span class="breadcrumb-separator">/</span>')}
      </div>
    `

    // Bind breadcrumb events
    breadcrumbContainer.querySelectorAll('.breadcrumb-link').forEach(link => {
      link.addEventListener('click', (e) => {
        const path = e.target.dataset.path
        this.loadFileTree(path)
      })
    })
  }

  renderFileTree() {
    if (!this.fileTree) return

    const contentContainer = this.container.querySelector('#browserContent')
    
    if (!this.fileTree.children || this.fileTree.children.length === 0) {
      contentContainer.innerHTML = `
        <div class="empty-state">
          <h3>Empty Directory</h3>
          <p>This directory doesn't contain any files or folders.</p>
        </div>
      `
      return
    }

    const filesHtml = this.fileTree.children.map(item => 
      this.renderFileItem(item)
    ).join('')

    contentContainer.innerHTML = `
      <div class="file-tree" data-testid="file-tree">
        ${filesHtml}
      </div>
    `

    this.bindFileEvents()
  }

  renderFileItem(item) {
    const isFolder = item.isDirectory
    const icon = this.getFileIcon(item)
    const size = isFolder ? '' : this.formatSize(item.size || 0)
    const lastModified = item.lastModified ? new Date(item.lastModified).toLocaleDateString() : ''
    
    return `
      <div class="file-item ${isFolder ? 'file-item-folder' : 'file-item-file'}" 
           data-path="${this.escapeHtml(item.path)}" 
           data-name="${this.escapeHtml(item.name)}"
           data-is-directory="${isFolder}"
           data-testid="file-item">
        <div class="file-item-content">
          <div class="file-item-main">
            <div class="file-item-selection">
              ${!isFolder ? `
                <input type="checkbox" class="file-checkbox" data-path="${this.escapeHtml(item.path)}" data-testid="file-checkbox">
              ` : ''}
            </div>
            <div class="file-item-icon">
              ${icon}
            </div>
            <div class="file-item-info">
              <div class="file-item-name" data-testid="file-name">
                ${this.escapeHtml(item.name)}
              </div>
              <div class="file-item-meta">
                ${size ? `<span class="file-size">${size}</span>` : ''}
                ${lastModified ? `<span class="file-date">${lastModified}</span>` : ''}
                ${item.mimeType ? `<span class="file-type">${item.mimeType}</span>` : ''}
              </div>
            </div>
          </div>
          <div class="file-item-actions">
            ${isFolder ? `
              <button class="btn btn-sm btn-outline browse-folder" data-path="${this.escapeHtml(item.path)}" data-testid="browse-folder">
                Open
              </button>
            ` : `
              <button class="btn btn-sm btn-outline view-file" data-path="${this.escapeHtml(item.path)}" data-testid="view-file">
                View
              </button>
              <button class="btn btn-sm btn-primary download-file" data-path="${this.escapeHtml(item.path)}" data-testid="download-file">
                Download
              </button>
            `}
          </div>
        </div>
      </div>
    `
  }

  bindFileEvents() {
    // Folder browsing
    this.container.querySelectorAll('.browse-folder').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const path = e.target.dataset.path
        this.loadFileTree(path)
      })
    })

    // File viewing
    this.container.querySelectorAll('.view-file').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const path = e.target.dataset.path
        if (this.options.onViewFile) {
          this.options.onViewFile(this.repositoryName, path)
        }
      })
    })

    // File downloading
    this.container.querySelectorAll('.download-file').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const path = e.target.dataset.path
        this.downloadFile(path)
      })
    })

    // File selection
    this.container.querySelectorAll('.file-checkbox').forEach(checkbox => {
      checkbox.addEventListener('change', (e) => {
        const path = e.target.dataset.path
        if (e.target.checked) {
          this.selectedFiles.add(path)
        } else {
          this.selectedFiles.delete(path)
        }
        this.updateDownloadButton()
      })
    })

    // Double-click to open folders or view files
    this.container.querySelectorAll('.file-item').forEach(item => {
      item.addEventListener('dblclick', (e) => {
        const path = item.dataset.path
        const isDirectory = item.dataset.isDirectory === 'true'
        
        if (isDirectory) {
          this.loadFileTree(path)
        } else {
          if (this.options.onViewFile) {
            this.options.onViewFile(this.repositoryName, path)
          }
        }
      })
    })
  }

  async downloadFile(filePath) {
    try {
      const result = await apiClient.getRepositoryFile(this.repositoryName, filePath)
      
      // Create download link
      const url = URL.createObjectURL(result.blob)
      const a = document.createElement('a')
      a.href = url
      a.download = result.filename
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
      
    } catch (error) {
      console.error('Failed to download file:', error)
      alert(`Failed to download file: ${error.message}`)
    }
  }

  async downloadSelectedFiles() {
    if (this.selectedFiles.size === 0) return

    // For now, download files one by one
    // In a real implementation, you might want to create a zip file
    for (const filePath of this.selectedFiles) {
      try {
        await this.downloadFile(filePath)
      } catch (error) {
        console.error(`Failed to download ${filePath}:`, error)
      }
    }

    // Clear selection
    this.selectedFiles.clear()
    this.container.querySelectorAll('.file-checkbox').forEach(checkbox => {
      checkbox.checked = false
    })
    this.updateDownloadButton()
  }

  updateDownloadButton() {
    const downloadBtn = this.container.querySelector('#downloadSelected')
    downloadBtn.disabled = this.selectedFiles.size === 0
    downloadBtn.innerHTML = `
      <span>⬇️</span> Download Selected ${this.selectedFiles.size > 0 ? `(${this.selectedFiles.size})` : ''}
    `
  }

  getFileIcon(item) {
    if (item.isDirectory) {
      return '📁'
    }

    const extension = item.name.split('.').pop()?.toLowerCase()
    const iconMap = {
      // Code files
      'js': '🟨',
      'ts': '🔷',
      'jsx': '⚛️',
      'tsx': '⚛️',
      'py': '🐍',
      'java': '☕',
      'cs': '🔷',
      'cpp': '⚙️',
      'c': '⚙️',
      'go': '🐹',
      'rs': '🦀',
      'php': '🐘',
      'rb': '💎',
      
      // Web files
      'html': '🌐',
      'css': '🎨',
      'scss': '🎨',
      'sass': '🎨',
      
      // Data files
      'json': '📋',
      'xml': '📋',
      'yaml': '📋',
      'yml': '📋',
      'csv': '📊',
      
      // Documentation
      'md': '📝',
      'txt': '📄',
      'pdf': '📕',
      'doc': '📘',
      'docx': '📘',
      
      // Images
      'png': '🖼️',
      'jpg': '🖼️',
      'jpeg': '🖼️',
      'gif': '🖼️',
      'svg': '🖼️',
      
      // Config
      'config': '⚙️',
      'conf': '⚙️',
      'ini': '⚙️',
      'env': '⚙️',
      
      // Archives
      'zip': '📦',
      'tar': '📦',
      'gz': '📦',
      '7z': '📦'
    }

    return iconMap[extension] || '📄'
  }

  formatSize(bytes) {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  filterFiles(searchTerm) {
    const items = this.container.querySelectorAll('.file-item')
    const term = searchTerm.toLowerCase()

    items.forEach(item => {
      const name = item.dataset.name.toLowerCase()
      const matches = !term || name.includes(term)
      item.style.display = matches ? 'block' : 'none'
    })
  }

  filterByType(type) {
    const items = this.container.querySelectorAll('.file-item')
    
    items.forEach(item => {
      const name = item.dataset.name.toLowerCase()
      const isDirectory = item.dataset.isDirectory === 'true'
      let matches = true

      if (type !== 'all' && !isDirectory) {
        const extension = name.split('.').pop()
        
        switch (type) {
          case 'code':
            matches = ['js', 'ts', 'jsx', 'tsx', 'py', 'java', 'cs', 'cpp', 'c', 'go', 'rs', 'php', 'rb'].includes(extension)
            break
          case 'docs':
            matches = ['md', 'txt', 'pdf', 'doc', 'docx', 'html'].includes(extension)
            break
          case 'config':
            matches = ['json', 'xml', 'yaml', 'yml', 'config', 'conf', 'ini', 'env'].includes(extension)
            break
          case 'data':
            matches = ['csv', 'sql', 'db', 'sqlite'].includes(extension)
            break
        }
      }

      item.style.display = matches ? 'block' : 'none'
    })
  }

  setViewMode(mode) {
    const treeBtn = this.container.querySelector('#treeView')
    const listBtn = this.container.querySelector('#listView')
    
    treeBtn.classList.toggle('active', mode === 'tree')
    listBtn.classList.toggle('active', mode === 'list')
    
    // For now, we only have tree view implemented
    // List view would show files in a table format
  }

  setLoading(loading) {
    this.isLoading = loading
    const contentContainer = this.container.querySelector('#browserContent')
    
    if (loading) {
      contentContainer.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showError(message) {
    const contentContainer = this.container.querySelector('#browserContent')
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
    // Clean up event listeners and resources
    this.selectedFiles.clear()
  }
}