import apiClient from '../services/api.js'

/**
 * File Browser Component
 * Provides a comprehensive workspace file browser with tree navigation
 */
class FileBrowserComponent {
  constructor(container, jobId, options = {}) {
    this.container = container
    this.jobId = jobId
    this.options = options
    this.currentPath = ''
    this.files = []
    this.filteredFiles = []
    this.sortBy = 'name'
    this.sortOrder = 'asc'
    this.searchTerm = ''
    this.selectedFile = null
    this.isLoading = false
    this.filePreviewCache = new Map()
    
    this.init()
  }

  async init() {
    this.render()
    await this.loadFiles()
    this.bindEvents()
  }

  render() {
    this.container.innerHTML = `
      <div style="display: flex; height: 650px; border: 1px solid #ddd; background: white; font-family: Arial, sans-serif; width: 100%; min-width: 900px;">
        
        <!-- LEFT PANEL: Directory Tree -->
        <div style="width: 300px; min-width: 300px; border-right: 1px solid #ddd; display: flex; flex-direction: column;">
          <div style="padding: 15px; background: #f8f9fa; border-bottom: 1px solid #ddd;">
            <h4 style="margin: 0; font-size: 14px; font-weight: 600; color: #333;">📁 Directories</h4>
          </div>
          <div id="fileTree" style="flex: 1; overflow-y: auto; padding: 10px;">
            <div id="treeLoader" style="color: #666; font-size: 12px;">Loading directories...</div>
          </div>
        </div>

        <!-- RIGHT PANEL: Files -->
        <div style="flex: 1; min-width: 0; display: flex; flex-direction: column;">
          <!-- Header -->
          <div style="padding: 15px; background: #f8f9fa; border-bottom: 1px solid #ddd; display: flex; justify-content: space-between; align-items: center;">
            <div style="flex: 1; min-width: 0;">
              <h4 style="margin: 0; font-size: 14px; font-weight: 600; color: #333;">📄 Files</h4>
              <div id="breadcrumbPath" style="font-size: 12px; color: #666; margin-top: 3px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">Root directory</div>
            </div>
            <div style="display: flex; gap: 10px; align-items: center; flex-shrink: 0;">
              <input 
                type="text" 
                id="fileSearchInput"
                placeholder="Search files..." 
                style="padding: 6px 10px; border: 1px solid #ddd; border-radius: 4px; font-size: 12px; width: 120px;"
              />
              <button id="refreshFiles" style="padding: 6px 12px; background: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 12px;">
                🔄
              </button>
            </div>
          </div>

          <!-- File Count -->
          <div style="padding: 10px 15px; background: #fff; border-bottom: 1px solid #eee;">
            <div id="fileCount" style="font-size: 12px; color: #666;">
              <span class="count-text">Loading files...</span>
            </div>
          </div>

          <!-- File List -->
          <div style="flex: 1; overflow-y: auto; min-width: 0;">
            <div id="fileList" style="min-height: 100%;">
              <div style="padding: 20px; text-align: center; color: #666; font-size: 12px;">Loading files...</div>
            </div>
          </div>
        </div>
      </div>
    `
  }

  async loadFiles(path = '') {
    this.setLoading(true)
    this.currentPath = path
    
    try {
      // Load directories and files separately using the new scalable API
      const [directoriesResponse, filesResponse] = await Promise.all([
        apiClient.getJobDirectories(this.jobId, path),
        apiClient.getJobFiles(this.jobId, path)
      ])
      
      // Combine directories and files into a single array
      const directories = (directoriesResponse || []).map(dir => ({
        ...dir,
        type: 'directory'
      }))
      
      const files = (filesResponse || []).map(file => ({
        ...file,
        type: 'file'
      }))
      
      this.files = [...directories, ...files]
      this.updateBreadcrumb(path)
      this.updateFileTree()
      this.filterAndSortFiles()
      this.renderFileList()
      this.updateFileCount()
    } catch (error) {
      console.error('Failed to load files:', error)
      this.showError('Failed to load workspace files. Please try again.')
    } finally {
      this.setLoading(false)
    }
  }

  updateBreadcrumb(path) {
    const breadcrumbPath = this.container.querySelector('#breadcrumbPath')
    if (!path) {
      breadcrumbPath.innerHTML = ''
      return
    }

    const parts = path.split('/').filter(Boolean)
    let currentPath = ''
    
    const breadcrumbs = parts.map(part => {
      currentPath += (currentPath ? '/' : '') + part
      return `
        <span class="breadcrumb-separator">→</span>
        <span class="breadcrumb-item" data-path="${currentPath}">
          ${this.escapeHtml(part)}
        </span>
      `
    }).join('')
    
    breadcrumbPath.innerHTML = breadcrumbs
  }

  updateFileTree() {
    const fileTree = this.container.querySelector('#fileTree')
    
    // Get directories from the root level for the tree
    // We need to show all available directories, not just current path subdirectories
    this.loadDirectoryTree().then(allDirectories => {
      if (allDirectories.length === 0) {
        fileTree.innerHTML = '<div style="color: #999; padding: 15px; text-align: center; font-size: 12px;">No directories found</div>'
        return
      }
      
      let html = `
        <div onclick="window.fileBrowser.handleDirectoryClick('')" 
             style="padding: 8px 12px; cursor: pointer; font-size: 13px; font-weight: 500; color: #333; border-radius: 4px; margin-bottom: 5px; ${this.currentPath === '' ? 'background: #e3f2fd; color: #1976d2;' : ''}"
             onmouseover="if(this.style.background !== 'rgb(227, 242, 253)') this.style.background='#f5f5f5'" 
             onmouseout="if(this.style.color !== 'rgb(25, 118, 210)') this.style.background='transparent'">
          <span style="margin-right: 8px;">🏠</span>
          <span>Root</span>
        </div>
      `
      
      allDirectories.forEach(dir => {
        const isSelected = this.currentPath === dir.path
        html += `
          <div onclick="window.fileBrowser.handleDirectoryClick('${dir.path}')" 
               style="padding: 8px 12px; cursor: pointer; font-size: 13px; color: #333; border-radius: 4px; margin-bottom: 2px; ${isSelected ? 'background: #e3f2fd; color: #1976d2; font-weight: 500;' : ''}"
               onmouseover="if(this.style.background !== 'rgb(227, 242, 253)') this.style.background='#f5f5f5'" 
               onmouseout="if(this.style.color !== 'rgb(25, 118, 210)') this.style.background='transparent'">
            <span style="margin-right: 8px;">📁</span>
            <span>${dir.name}</span>
          </div>
        `
      })
      
      fileTree.innerHTML = html
      
      // Make the file browser instance available globally for onclick handlers
      window.fileBrowser = this
    }).catch(error => {
      console.error('Failed to load directory tree:', error)
      fileTree.innerHTML = '<div style="color: #999; padding: 15px; text-align: center; font-size: 12px;">Error loading directories</div>'
    })
  }

  buildTreeStructure(files) {
    const tree = {}
    
    files.forEach(file => {
      if (file.type === 'directory') {
        const relativePath = file.path.replace(this.currentPath, '').replace(/^\//, '')
        if (relativePath && !relativePath.includes('/')) {
          tree[file.name] = {
            type: 'directory',
            path: file.path,
            children: {}
          }
        }
      }
    })
    
    return tree
  }

  renderTreeNodes(nodes, basePath = '') {
    return Object.entries(nodes).map(([name, node]) => `
      <div class="tree-node ${node.path === this.currentPath ? 'active' : ''}" data-path="${node.path}">
        <div class="tree-item">
          <span class="tree-icon">${node.type === 'directory' ? '📁' : '📄'}</span>
          <span class="tree-label">${this.escapeHtml(name)}</span>
        </div>
        ${Object.keys(node.children || {}).length > 0 ? 
          `<div class="tree-children">${this.renderTreeNodes(node.children, node.path)}</div>` : 
          ''
        }
      </div>
    `).join('')
  }

  filterAndSortFiles() {
    let filtered = [...this.files]
    
    // Apply search filter
    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase()
      filtered = filtered.filter(file => 
        file.name.toLowerCase().includes(term) ||
        (file.path && file.path.toLowerCase().includes(term))
      )
    }
    
    // Apply sorting
    const [sortBy, order] = this.sortBy.split('-')
    filtered.sort((a, b) => {
      let aVal, bVal
      
      switch (sortBy) {
        case 'name':
          aVal = a.name.toLowerCase()
          bVal = b.name.toLowerCase()
          break
        case 'size':
          aVal = a.size || 0
          bVal = b.size || 0
          break
        case 'modified':
          aVal = new Date(a.modified || 0)
          bVal = new Date(b.modified || 0)
          break
        case 'type':
          aVal = a.type || ''
          bVal = b.type || ''
          break
        default:
          return 0
      }
      
      if (aVal < bVal) return order === 'asc' ? -1 : 1
      if (aVal > bVal) return order === 'asc' ? 1 : -1
      return 0
    })
    
    // Keep directories first for better UX
    this.filteredFiles = [
      ...filtered.filter(f => f.type === 'directory'),
      ...filtered.filter(f => f.type !== 'directory')
    ]
  }

  renderFileList() {
    const fileList = this.container.querySelector('#fileList')
    
    // Show only files in the right panel (directories are in the left panel)
    const files = this.filteredFiles.filter(f => f.type === 'file')
    
    if (files.length === 0) {
      fileList.innerHTML = `
        <div style="padding: 40px; text-align: center; color: #999;">
          <div style="font-size: 48px; margin-bottom: 15px; opacity: 0.5;">📄</div>
          <div style="font-size: 14px; font-weight: 500; margin-bottom: 5px;">No files found</div>
          <div style="font-size: 12px;">This directory is empty or contains only subdirectories</div>
        </div>
      `
      return
    }
    
    let html = ''
    files.forEach(file => {
      const size = file.size ? this.formatFileSize(file.size) : ''
      const modified = file.modified ? new Date(file.modified).toLocaleDateString() : ''
      const icon = this.getFileIcon(file)
      
      html += `
        <div style="padding: 10px 15px; border-bottom: 1px solid #f0f0f0; display: flex; align-items: center; min-width: 0;"
             onmouseover="this.style.background='#f8f9fa'" 
             onmouseout="this.style.background='transparent'">
          
          <div style="margin-right: 10px; font-size: 16px; opacity: 0.8; flex-shrink: 0;">${icon}</div>
          
          <div style="flex: 1; min-width: 0; margin-right: 10px;">
            <div style="font-size: 13px; font-weight: 500; color: #333; margin-bottom: 3px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${file.name}">${file.name}</div>
            <div style="font-size: 11px; color: #666; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
              ${size}${size && modified ? ' • ' : ''}${modified}
            </div>
          </div>
          
          <div style="display: flex; gap: 4px; flex-shrink: 0;">
            <button onclick="window.fileBrowser.downloadFile('${file.path}', '${file.name}')"
                    style="padding: 6px 12px; font-size: 11px; background: #007bff; color: white; border: none; cursor: pointer; border-radius: 4px; font-weight: 500;">
              📥 Download
            </button>
          </div>
        </div>
      `
    })
    
    fileList.innerHTML = html
  }

  renderFileItem(file) {
    const icon = this.getFileIcon(file)
    const size = file.size ? this.formatFileSize(file.size) : ''
    const modified = file.modified ? new Date(file.modified).toLocaleDateString() : ''
    const isSelected = this.selectedFile && this.selectedFile.path === file.path
    
    return `
      <div class="file-item ${isSelected ? 'selected' : ''}" 
           data-path="${file.path}" 
           data-type="${file.type}"
           data-testid="file-item">
        <div class="file-select">
          <input type="checkbox" class="file-checkbox" data-path="${file.path}">
        </div>
        
        <div class="file-icon">
          ${icon}
        </div>
        
        <div class="file-details">
          <div class="file-name" data-testid="file-name">${this.escapeHtml(file.name)}</div>
          <div class="file-meta">
            ${size ? `<span class="file-size">${size}</span>` : ''}
            ${modified ? `<span class="file-date">${modified}</span>` : ''}
          </div>
        </div>
        
        <div class="file-actions">
          ${file.type === 'directory' ? `
            <button class="action-btn" data-action="open" title="Open directory">
              📂
            </button>
          ` : `
            <button class="action-btn" data-action="preview" title="Preview file" data-testid="preview-file">
              👁
            </button>
            <button class="action-btn" data-action="download" title="Download file" data-testid="download-file">
              📥
            </button>
            <button class="action-btn" data-action="open-tab" title="Open in new tab">
              🔗
            </button>
          `}
        </div>
      </div>
    `
  }

  getFileIcon(file) {
    if (file.type === 'directory') return '📁'
    
    const ext = file.name.split('.').pop().toLowerCase()
    const iconMap = {
      // Code files
      'js': '📜', 'ts': '📜', 'jsx': '⚛️', 'tsx': '⚛️',
      'py': '🐍', 'java': '☕', 'cpp': '🔧', 'c': '🔧',
      'html': '🌐', 'css': '🎨', 'scss': '🎨', 'sass': '🎨',
      'json': '📋', 'xml': '📋', 'yaml': '📋', 'yml': '📋',
      
      // Images
      'png': '🖼️', 'jpg': '🖼️', 'jpeg': '🖼️', 'gif': '🖼️', 'svg': '🖼️',
      
      // Documents
      'pdf': '📄', 'doc': '📝', 'docx': '📝', 'txt': '📄', 'md': '📖',
      
      // Archives
      'zip': '🗜️', 'tar': '🗜️', 'gz': '🗜️', 'rar': '🗜️',
      
      // Config
      'env': '⚙️', 'config': '⚙️', 'conf': '⚙️'
    }
    
    return iconMap[ext] || '📄'
  }

  formatFileSize(bytes) {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
  }

  async previewFile(filePath) {
    const previewPanel = this.container.querySelector('#filePreview')
    
    // Check cache first
    if (this.filePreviewCache.has(filePath)) {
      previewPanel.innerHTML = this.filePreviewCache.get(filePath)
      return
    }
    
    previewPanel.innerHTML = `
      <div class="preview-loading">
        <div class="loading-spinner"></div>
        <p>Loading file preview...</p>
      </div>
    `
    
    try {
      const file = this.files.find(f => f.path === filePath)
      if (!file) throw new Error('File not found')
      
      const ext = file.name.split('.').pop().toLowerCase()
      
      // Handle different file types
      if (this.isImageFile(ext)) {
        await this.previewImage(filePath, file)
      } else if (this.isTextFile(ext)) {
        await this.previewTextFile(filePath, file)
      } else {
        this.showBinaryFilePreview(file)
      }
    } catch (error) {
      console.error('Failed to preview file:', error)
      previewPanel.innerHTML = `
        <div class="preview-error">
          <div class="error-icon">❌</div>
          <h3>Preview Error</h3>
          <p>Unable to preview this file: ${error.message}</p>
          <button class="btn btn-primary" onclick="this.closest('.file-browser-container').querySelector('[data-action=download]').click()">
            Download Instead
          </button>
        </div>
      `
    }
  }

  async previewTextFile(filePath, file) {
    try {
      const content = await apiClient.getJobFileContent(this.jobId, filePath)
      const ext = file.name.split('.').pop().toLowerCase()
      
      const previewHtml = `
        <div class="text-file-preview">
          <div class="preview-header">
            <h3>${this.escapeHtml(file.name)}</h3>
            <div class="file-info">
              <span>Size: ${this.formatFileSize(file.size || 0)}</span>
              ${file.modified ? `<span>Modified: ${new Date(file.modified).toLocaleString()}</span>` : ''}
            </div>
          </div>
          <div class="preview-content">
            <pre class="code-preview language-${ext}"><code>${this.escapeHtml(content)}</code></pre>
          </div>
          <div class="preview-actions">
            <button class="btn btn-outline" data-action="download">📥 Download</button>
            <button class="btn btn-outline" data-action="copy-content">📋 Copy Content</button>
          </div>
        </div>
      `
      
      this.filePreviewCache.set(filePath, previewHtml)
      this.container.querySelector('#filePreview').innerHTML = previewHtml
      
      // Apply syntax highlighting if available
      this.applySyntaxHighlighting()
    } catch (error) {
      throw new Error(`Failed to load file content: ${error.message}`)
    }
  }

  async previewImage(filePath, file) {
    try {
      const { blob } = await apiClient.downloadJobFileBlob(this.jobId, filePath)
      const imageUrl = URL.createObjectURL(blob)
      
      const previewHtml = `
        <div class="image-file-preview">
          <div class="preview-header">
            <h3>${this.escapeHtml(file.name)}</h3>
            <div class="file-info">
              <span>Size: ${this.formatFileSize(file.size || 0)}</span>
              ${file.modified ? `<span>Modified: ${new Date(file.modified).toLocaleString()}</span>` : ''}
            </div>
          </div>
          <div class="preview-content">
            <img src="${imageUrl}" alt="${this.escapeHtml(file.name)}" class="preview-image">
          </div>
          <div class="preview-actions">
            <button class="btn btn-outline" data-action="download">📥 Download</button>
            <button class="btn btn-outline" data-action="open-tab">🔗 Open in New Tab</button>
          </div>
        </div>
      `
      
      this.filePreviewCache.set(filePath, previewHtml)
      this.container.querySelector('#filePreview').innerHTML = previewHtml
    } catch (error) {
      throw new Error(`Failed to load image: ${error.message}`)
    }
  }

  showBinaryFilePreview(file) {
    const ext = file.name.split('.').pop().toLowerCase()
    const previewHtml = `
      <div class="binary-file-preview">
        <div class="preview-header">
          <h3>${this.escapeHtml(file.name)}</h3>
          <div class="file-info">
            <span>Type: ${ext.toUpperCase()} File</span>
            <span>Size: ${this.formatFileSize(file.size || 0)}</span>
            ${file.modified ? `<span>Modified: ${new Date(file.modified).toLocaleString()}</span>` : ''}
          </div>
        </div>
        <div class="preview-content">
          <div class="binary-file-icon">${this.getFileIcon(file)}</div>
          <p>This is a binary file that cannot be previewed directly.</p>
          <p>File type: <strong>${ext.toUpperCase()}</strong></p>
        </div>
        <div class="preview-actions">
          <button class="btn btn-primary" data-action="download">📥 Download File</button>
        </div>
      </div>
    `
    
    this.container.querySelector('#filePreview').innerHTML = previewHtml
  }

  isImageFile(ext) {
    return ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'svg', 'webp'].includes(ext)
  }

  isTextFile(ext) {
    const textExts = [
      'txt', 'md', 'json', 'xml', 'html', 'css', 'js', 'ts', 'jsx', 'tsx',
      'py', 'java', 'cpp', 'c', 'h', 'cs', 'php', 'rb', 'go', 'rs',
      'yaml', 'yml', 'toml', 'ini', 'conf', 'config', 'env', 'gitignore',
      'dockerfile', 'makefile', 'readme', 'license', 'changelog'
    ]
    return textExts.includes(ext) || ext === ''
  }

  applySyntaxHighlighting() {
    // Basic syntax highlighting - can be enhanced with external libraries
    const codeElements = this.container.querySelectorAll('pre code')
    codeElements.forEach(element => {
      // Add basic syntax highlighting classes
      element.classList.add('syntax-highlighted')
    })
  }

  async downloadFile(filePath) {
    try {
      const { blob, filename } = await apiClient.downloadJobFileBlob(this.jobId, filePath)
      
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (error) {
      console.error('Failed to download file:', error)
      alert(`Failed to download file: ${error.message}`)
    }
  }

  openFileInNewTab(filePath) {
    const url = `/api/jobs/${this.jobId}/files/download?path=${encodeURIComponent(filePath)}`
    window.open(url, '_blank')
  }

  updateFileCount() {
    const fileCount = this.container.querySelector('#fileCount .count-text')
    const total = this.filteredFiles.length
    const files = this.filteredFiles.filter(f => f.type !== 'directory').length
    const dirs = this.filteredFiles.filter(f => f.type === 'directory').length
    
    let text = `${total} items`
    if (dirs > 0 && files > 0) {
      text = `${dirs} folders, ${files} files`
    } else if (dirs > 0) {
      text = `${dirs} folders`
    } else if (files > 0) {
      text = `${files} files`
    }
    
    if (this.searchTerm) {
      text += ` (filtered)`
    }
    
    fileCount.textContent = text
  }

  bindEvents() {
    // Search functionality
    const searchInput = this.container.querySelector('#fileSearchInput')
    const clearSearch = this.container.querySelector('#clearSearch')
    
    searchInput.addEventListener('input', (e) => {
      this.searchTerm = e.target.value
      this.filterAndSortFiles()
      this.renderFileList()
      this.updateFileCount()
      clearSearch.style.display = this.searchTerm ? 'block' : 'none'
    })
    
    clearSearch.addEventListener('click', () => {
      searchInput.value = ''
      this.searchTerm = ''
      this.filterAndSortFiles()
      this.renderFileList()
      this.updateFileCount()
      clearSearch.style.display = 'none'
    })
    
    // Sorting
    const sortSelect = this.container.querySelector('#sortSelect')
    sortSelect.addEventListener('change', (e) => {
      this.sortBy = e.target.value
      this.filterAndSortFiles()
      this.renderFileList()
    })
    
    // Refresh files
    const refreshBtn = this.container.querySelector('#refreshFiles')
    refreshBtn.addEventListener('click', () => {
      this.filePreviewCache.clear()
      this.loadFiles(this.currentPath)
    })
    
    // Breadcrumb navigation
    this.container.addEventListener('click', (e) => {
      if (e.target.matches('.breadcrumb-item')) {
        const path = e.target.dataset.path
        this.loadFiles(path)
      }
    })
    
    // File tree navigation
    this.container.addEventListener('click', (e) => {
      if (e.target.closest('.tree-node')) {
        const treeNode = e.target.closest('.tree-node')
        const path = treeNode.dataset.path
        if (path !== this.currentPath) {
          this.loadFiles(path)
        }
      }
    })
    
    // File list interactions
    this.container.addEventListener('click', (e) => {
      const fileItem = e.target.closest('.file-item')
      if (!fileItem) return
      
      const filePath = fileItem.dataset.path
      const fileType = fileItem.dataset.type
      
      // Handle action buttons
      if (e.target.matches('[data-action]')) {
        const action = e.target.dataset.action
        
        switch (action) {
          case 'open':
            if (fileType === 'directory') {
              this.loadFiles(filePath)
            }
            break
          case 'preview':
            this.selectedFile = this.files.find(f => f.path === filePath)
            this.renderFileList() // Re-render to show selection
            this.previewFile(filePath)
            break
          case 'download':
            this.downloadFile(filePath)
            break
          case 'open-tab':
            this.openFileInNewTab(filePath)
            break
        }
        return
      }
      
      // Handle file selection (click anywhere on file item)
      if (fileType === 'directory') {
        this.loadFiles(filePath)
      } else {
        this.selectedFile = this.files.find(f => f.path === filePath)
        this.renderFileList() // Re-render to show selection
        this.previewFile(filePath)
      }
    })
    
    // Double-click to open
    this.container.addEventListener('dblclick', (e) => {
      const fileItem = e.target.closest('.file-item')
      if (!fileItem) return
      
      const filePath = fileItem.dataset.path
      const fileType = fileItem.dataset.type
      
      if (fileType === 'directory') {
        this.loadFiles(filePath)
      } else {
        this.downloadFile(filePath)
      }
    })
    
    // Select all functionality
    const selectAllBtn = this.container.querySelector('#selectAll')
    const downloadSelectedBtn = this.container.querySelector('#downloadSelected')
    
    selectAllBtn.addEventListener('click', () => {
      const checkboxes = this.container.querySelectorAll('.file-checkbox')
      const allChecked = Array.from(checkboxes).every(cb => cb.checked)
      
      checkboxes.forEach(cb => cb.checked = !allChecked)
      this.updateSelectedActions()
      selectAllBtn.textContent = allChecked ? 'Select All' : 'Deselect All'
    })
    
    // Handle individual checkbox changes
    this.container.addEventListener('change', (e) => {
      if (e.target.matches('.file-checkbox')) {
        this.updateSelectedActions()
        
        const checkboxes = this.container.querySelectorAll('.file-checkbox')
        const checkedCount = Array.from(checkboxes).filter(cb => cb.checked).length
        const allChecked = checkedCount === checkboxes.length
        const noneChecked = checkedCount === 0
        
        selectAllBtn.textContent = allChecked ? 'Deselect All' : 'Select All'
      }
    })
    
    downloadSelectedBtn.addEventListener('click', () => {
      this.downloadSelectedFiles()
    })
    
    // Preview panel actions
    this.container.addEventListener('click', (e) => {
      if (e.target.matches('[data-action="copy-content"]')) {
        this.copyPreviewContent()
      }
    })
  }

  updateSelectedActions() {
    const checkboxes = this.container.querySelectorAll('.file-checkbox:checked')
    const downloadSelectedBtn = this.container.querySelector('#downloadSelected')
    
    downloadSelectedBtn.disabled = checkboxes.length === 0
    downloadSelectedBtn.textContent = checkboxes.length > 0 
      ? `📥 Download Selected (${checkboxes.length})` 
      : '📥 Download Selected'
  }

  async downloadSelectedFiles() {
    const checkboxes = this.container.querySelectorAll('.file-checkbox:checked')
    const selectedPaths = Array.from(checkboxes).map(cb => cb.dataset.path)
    
    if (selectedPaths.length === 0) return
    
    // Download files sequentially to avoid overwhelming the server
    for (const path of selectedPaths) {
      try {
        await this.downloadFile(path)
        // Small delay between downloads
        await new Promise(resolve => setTimeout(resolve, 100))
      } catch (error) {
        console.error(`Failed to download ${path}:`, error)
      }
    }
  }

  async copyPreviewContent() {
    try {
      const codeElement = this.container.querySelector('.code-preview code')
      if (codeElement) {
        await navigator.clipboard.writeText(codeElement.textContent)
        
        // Show feedback
        const btn = this.container.querySelector('[data-action="copy-content"]')
        const originalText = btn.textContent
        btn.textContent = '✅ Copied!'
        setTimeout(() => {
          btn.textContent = originalText
        }, 2000)
      }
    } catch (error) {
      console.error('Failed to copy content:', error)
      alert('Failed to copy content to clipboard.')
    }
  }

  setLoading(loading) {
    this.isLoading = loading
    
    const treeLoader = this.container.querySelector('#treeLoader')
    const fileList = this.container.querySelector('#fileList')
    
    if (loading) {
      if (treeLoader) treeLoader.style.display = 'block'
      if (fileList && !fileList.querySelector('.file-items')) {
        fileList.innerHTML = '<div class="loading-spinner"></div>'
      }
    } else {
      if (treeLoader) treeLoader.style.display = 'none'
    }
  }

  showError(message) {
    const fileList = this.container.querySelector('#fileList')
    fileList.innerHTML = `
      <div class="error-state" data-testid="error-state">
        <div class="error-icon">❌</div>
        <h3>Error</h3>
        <p>${message}</p>
        <button class="btn btn-primary" onclick="this.closest('.file-browser-container').querySelector('#refreshFiles').click()">
          Try Again
        </button>
      </div>
    `
  }

  handleDirectoryClick(path) {
    console.log('Directory clicked:', path, 'Current path:', this.currentPath)
    this.loadFiles(path)
  }

  async loadDirectoryTree() {
    try {
      // Load all directories from root to build complete tree
      const allDirectories = await apiClient.getJobDirectories(this.jobId, '')
      return allDirectories || []
    } catch (error) {
      console.error('Failed to load directory tree:', error)
      return []
    }
  }

  async viewFile(filePath, fileName) {
    try {
      const content = await apiClient.getJobFileContent(this.jobId, filePath)
      
      // Create a modal or new window to show file content
      const modal = document.createElement('div')
      modal.style.cssText = `
        position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; 
        background: rgba(0,0,0,0.8); z-index: 10000; display: block; 
        padding: 10px; box-sizing: border-box;
      `
      
      modal.innerHTML = `
        <div style="background: white; width: calc(100vw - 20px); height: calc(100vh - 20px); padding: 30px; overflow: auto; border-radius: 8px; box-sizing: border-box;">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; border-bottom: 2px solid #eee; padding-bottom: 15px;">
            <h3 style="margin: 0; font-size: 18px; color: #333;">${fileName}</h3>
            <button onclick="this.closest('[style*=\"position: fixed\"]').remove()" 
                    style="background: #dc3545; color: white; border: none; padding: 10px 20px; border-radius: 6px; cursor: pointer; font-size: 14px; font-weight: 500;">
              ✕ Close
            </button>
          </div>
          <pre style="background: #f8f9fa; padding: 20px; border-radius: 6px; overflow: auto; white-space: pre-wrap; font-size: 13px; line-height: 1.4; border: 1px solid #e9ecef;"><code>${this.escapeHtml(content)}</code></pre>
        </div>
      `
      
      document.body.appendChild(modal)
    } catch (error) {
      console.error('Failed to view file:', error)
      alert(`Failed to view file: ${error.message}`)
    }
  }

  async downloadFile(filePath, fileName) {
    try {
      const { blob, filename } = await apiClient.downloadJobFileBlob(this.jobId, filePath)
      
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename || fileName
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (error) {
      console.error('Failed to download file:', error)
      alert(`Failed to download file: ${error.message}`)
    }
  }

  escapeHtml(text) {
    const div = document.createElement('div')
    div.textContent = text
    return div.innerHTML
  }

  destroy() {
    // Clear any cached URLs to prevent memory leaks
    this.filePreviewCache.clear()
    
    // Clear any object URLs
    const images = this.container.querySelectorAll('.preview-image')
    images.forEach(img => {
      if (img.src.startsWith('blob:')) {
        URL.revokeObjectURL(img.src)
      }
    })
  }
}

// Export for use in other components
export default FileBrowserComponent