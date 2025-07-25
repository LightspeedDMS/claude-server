import apiClient from '../services/api.js'

/**
 * File Viewer Component
 * Displays file contents with syntax highlighting and metadata
 */
export class FileViewerComponent {
  constructor(container, repositoryName, filePath, options = {}) {
    this.container = container
    this.repositoryName = repositoryName
    this.filePath = filePath
    this.options = options
    this.fileMetadata = null
    this.fileContent = null
    this.isLoading = false
    this.viewMode = 'content' // 'content', 'raw', 'preview'
    
    this.init()
  }

  async init() {
    this.render()
    this.bindEvents()
    await this.loadFileInfo()
    await this.loadFileContent()
  }

  render() {
    this.container.innerHTML = `
      <div class="file-viewer-container">
        <div class="file-viewer-header">
          <button class="btn btn-outline back-button" id="backButton" data-testid="back-button">
            <span>←</span> Back to Browser
          </button>
          <div class="file-viewer-title-section">
            <h1 class="file-viewer-title" data-testid="file-viewer-title">
              ${this.escapeHtml(this.getFileName())}
            </h1>
            <div class="file-path" data-testid="file-path">
              ${this.escapeHtml(this.repositoryName)} / ${this.escapeHtml(this.filePath)}
            </div>
          </div>
          <div class="file-viewer-actions">
            <button class="btn btn-secondary" id="downloadFile" data-testid="download-file">
              <span>⬇️</span> Download
            </button>
            <button class="btn btn-outline" id="copyPath" data-testid="copy-path">
              <span>📋</span> Copy Path
            </button>
          </div>
        </div>

        <div class="file-viewer-toolbar">
          <div class="file-info" id="fileInfo">
            <div class="loading-spinner-small"></div>
          </div>
          <div class="view-mode-toggle">
            <button class="view-mode-btn active" id="contentView" data-testid="content-view">
              Content
            </button>
            <button class="view-mode-btn" id="rawView" data-testid="raw-view">
              Raw
            </button>
            <button class="view-mode-btn" id="previewView" data-testid="preview-view">
              Preview
            </button>
          </div>
        </div>

        <div id="fileContent" class="file-content">
          <div class="loading-spinner"></div>
        </div>
      </div>
    `
  }

  bindEvents() {
    const backButton = this.container.querySelector('#backButton')
    const downloadButton = this.container.querySelector('#downloadFile')
    const copyPathButton = this.container.querySelector('#copyPath')
    const contentViewBtn = this.container.querySelector('#contentView')
    const rawViewBtn = this.container.querySelector('#rawView')
    const previewViewBtn = this.container.querySelector('#previewView')

    backButton.addEventListener('click', () => {
      if (this.options.onBack) {
        this.options.onBack()
      }
    })

    downloadButton.addEventListener('click', () => {
      this.downloadFile()
    })

    copyPathButton.addEventListener('click', () => {
      this.copyPathToClipboard()
    })

    contentViewBtn.addEventListener('click', () => {
      this.setViewMode('content')
    })

    rawViewBtn.addEventListener('click', () => {
      this.setViewMode('raw')
    })

    previewViewBtn.addEventListener('click', () => {
      this.setViewMode('preview')
    })
  }

  async loadFileInfo() {
    try {
      this.fileMetadata = await apiClient.getRepositoryFileInfo(this.repositoryName, this.filePath)
      this.renderFileInfo()
    } catch (error) {
      console.error('Failed to load file info:', error)
      this.showFileInfoError('Failed to load file information')
    }
  }

  async loadFileContent() {
    this.setLoading(true)
    
    try {
      // For text files, we'll get the content as text
      // For binary files, we'll show a preview or download option
      if (this.isTextFile()) {
        const result = await apiClient.getRepositoryFile(this.repositoryName, this.filePath)
        this.fileContent = await result.blob.text()
        this.renderFileContent()
      } else {
        this.renderBinaryFileContent()
      }
    } catch (error) {
      console.error('Failed to load file content:', error)
      this.showError('Failed to load file content. The file may be too large or binary.')
    } finally {
      this.setLoading(false)
    }
  }

  renderFileInfo() {
    if (!this.fileMetadata) return

    const fileInfoContainer = this.container.querySelector('#fileInfo')
    const size = this.formatSize(this.fileMetadata.size || 0)
    const lastModified = this.fileMetadata.lastModified ? 
      new Date(this.fileMetadata.lastModified).toLocaleString() : 'Unknown'

    fileInfoContainer.innerHTML = `
      <div class="file-metadata">
        <div class="metadata-item">
          <span class="metadata-label">Size:</span>
          <span class="metadata-value" data-testid="file-size">${size}</span>
        </div>
        <div class="metadata-item">
          <span class="metadata-label">Type:</span>
          <span class="metadata-value" data-testid="file-type">${this.fileMetadata.mimeType || 'Unknown'}</span>
        </div>
        <div class="metadata-item">
          <span class="metadata-label">Modified:</span>
          <span class="metadata-value" data-testid="file-modified">${lastModified}</span>
        </div>
        ${this.fileMetadata.encoding ? `
          <div class="metadata-item">
            <span class="metadata-label">Encoding:</span>
            <span class="metadata-value">${this.fileMetadata.encoding}</span>
          </div>
        ` : ''}
      </div>
    `
  }

  renderFileContent() {
    if (!this.fileContent) return

    const contentContainer = this.container.querySelector('#fileContent')
    
    switch (this.viewMode) {
      case 'content':
        this.renderContentView(contentContainer)
        break
      case 'raw':
        this.renderRawView(contentContainer)
        break
      case 'preview':
        this.renderPreviewView(contentContainer)
        break
    }
  }

  renderContentView(container) {
    const language = this.detectLanguage()
    const lineNumbers = this.generateLineNumbers()
    
    container.innerHTML = `
      <div class="code-viewer" data-testid="code-viewer">
        <div class="code-header">
          <div class="code-language">${language}</div>
          <div class="code-stats">
            ${this.fileContent.split('\n').length} lines
          </div>
        </div>
        <div class="code-content">
          <div class="line-numbers">${lineNumbers}</div>
          <pre class="code-text"><code class="language-${language.toLowerCase()}">${this.escapeHtml(this.fileContent)}</code></pre>
        </div>
      </div>
    `
  }

  renderRawView(container) {
    container.innerHTML = `
      <div class="raw-viewer" data-testid="raw-viewer">
        <div class="raw-content">
          <pre class="raw-text">${this.escapeHtml(this.fileContent)}</pre>
        </div>
      </div>
    `
  }

  renderPreviewView(container) {
    const extension = this.getFileExtension().toLowerCase()
    
    if (extension === 'md') {
      // Render markdown (basic implementation)
      container.innerHTML = `
        <div class="preview-viewer markdown-preview" data-testid="preview-viewer">
          <div class="preview-content">
            ${this.renderMarkdown(this.fileContent)}
          </div>
        </div>
      `
    } else if (['html', 'htm'].includes(extension)) {
      // Render HTML preview
      container.innerHTML = `
        <div class="preview-viewer html-preview" data-testid="preview-viewer">
          <div class="preview-content">
            <iframe srcdoc="${this.escapeHtml(this.fileContent)}" class="html-preview-frame"></iframe>
          </div>
        </div>
      `
    } else if (['json'].includes(extension)) {
      // Render formatted JSON
      try {
        const jsonData = JSON.parse(this.fileContent)
        container.innerHTML = `
          <div class="preview-viewer json-preview" data-testid="preview-viewer">
            <div class="preview-content">
              <pre class="json-formatted">${JSON.stringify(jsonData, null, 2)}</pre>
            </div>
          </div>
        `
      } catch {
        this.renderContentView(container)
      }
    } else {
      // Fallback to content view
      this.renderContentView(container)
    }
  }

  renderBinaryFileContent() {
    const contentContainer = this.container.querySelector('#fileContent')
    const extension = this.getFileExtension().toLowerCase()
    
    if (['png', 'jpg', 'jpeg', 'gif', 'svg', 'webp'].includes(extension)) {
      // Show image preview
      contentContainer.innerHTML = `
        <div class="binary-viewer image-viewer" data-testid="binary-viewer">
          <div class="binary-message">
            <h3>Image File</h3>
            <p>This is an image file. Click download to view it.</p>
            <button class="btn btn-primary" onclick="document.getElementById('downloadFile').click()">
              Download Image
            </button>
          </div>
        </div>
      `
    } else {
      // Show binary file message
      contentContainer.innerHTML = `
        <div class="binary-viewer" data-testid="binary-viewer">
          <div class="binary-message">
            <h3>Binary File</h3>
            <p>This file contains binary data and cannot be displayed as text.</p>
            <button class="btn btn-primary" onclick="document.getElementById('downloadFile').click()">
              Download File
            </button>
          </div>
        </div>
      `
    }
  }

  generateLineNumbers() {
    const lineCount = this.fileContent.split('\n').length
    return Array.from({ length: lineCount }, (_, i) => i + 1)
      .map(num => `<div class="line-number">${num}</div>`)
      .join('')
  }

  detectLanguage() {
    const extension = this.getFileExtension().toLowerCase()
    const languageMap = {
      'js': 'JavaScript',
      'ts': 'TypeScript',
      'jsx': 'React',
      'tsx': 'React TypeScript',
      'py': 'Python',
      'java': 'Java',
      'cs': 'C#',
      'cpp': 'C++',
      'c': 'C',
      'go': 'Go',
      'rs': 'Rust',
      'php': 'PHP',
      'rb': 'Ruby',
      'html': 'HTML',
      'css': 'CSS',
      'scss': 'SCSS',
      'sass': 'Sass',
      'json': 'JSON',
      'xml': 'XML',
      'yaml': 'YAML',
      'yml': 'YAML',
      'md': 'Markdown',
      'txt': 'Text',
      'sh': 'Shell',
      'bash': 'Bash',
      'sql': 'SQL'
    }

    return languageMap[extension] || 'Text'
  }

  renderMarkdown(content) {
    // Basic markdown rendering (you might want to use a proper markdown library)
    return content
      .replace(/^### (.*$)/gim, '<h3>$1</h3>')
      .replace(/^## (.*$)/gim, '<h2>$1</h2>')
      .replace(/^# (.*$)/gim, '<h1>$1</h1>')
      .replace(/\*\*(.*)\*\*/gim, '<strong>$1</strong>')
      .replace(/\*(.*)\*/gim, '<em>$1</em>')
      .replace(/\`(.*)\`/gim, '<code>$1</code>')
      .replace(/\n/gim, '<br>')
  }

  async downloadFile() {
    try {
      const result = await apiClient.getRepositoryFile(this.repositoryName, this.filePath)
      
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

  async copyPathToClipboard() {
    try {
      await navigator.clipboard.writeText(this.filePath)
      
      // Show temporary feedback
      const button = this.container.querySelector('#copyPath')
      const originalText = button.innerHTML
      button.innerHTML = '<span>✓</span> Copied!'
      setTimeout(() => {
        button.innerHTML = originalText
      }, 2000)
      
    } catch (error) {
      console.error('Failed to copy path:', error)
      alert('Failed to copy path to clipboard')
    }
  }

  setViewMode(mode) {
    this.viewMode = mode
    
    // Update button states
    const buttons = this.container.querySelectorAll('.view-mode-btn')
    buttons.forEach(btn => btn.classList.remove('active'))
    this.container.querySelector(`#${mode}View`).classList.add('active')
    
    // Re-render content
    this.renderFileContent()
  }

  isTextFile() {
    if (!this.fileMetadata) {
      // Fallback to extension-based detection
      const extension = this.getFileExtension().toLowerCase()
      const textExtensions = [
        'txt', 'md', 'js', 'ts', 'jsx', 'tsx', 'py', 'java', 'cs', 'cpp', 'c', 
        'go', 'rs', 'php', 'rb', 'html', 'css', 'scss', 'sass', 'json', 'xml', 
        'yaml', 'yml', 'sh', 'bash', 'sql', 'config', 'conf', 'ini', 'env'
      ]
      return textExtensions.includes(extension)
    }

    const mimeType = this.fileMetadata.mimeType || ''
    return mimeType.startsWith('text/') || 
           mimeType === 'application/json' ||
           mimeType === 'application/xml' ||
           mimeType === 'application/javascript'
  }

  getFileName() {
    return this.filePath.split('/').pop() || 'Unknown'
  }

  getFileExtension() {
    const fileName = this.getFileName()
    return fileName.split('.').pop() || ''
  }

  formatSize(bytes) {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  setLoading(loading) {
    this.isLoading = loading
    const contentContainer = this.container.querySelector('#fileContent')
    
    if (loading) {
      contentContainer.innerHTML = '<div class="loading-spinner"></div>'
    }
  }

  showFileInfoError(message) {
    const fileInfoContainer = this.container.querySelector('#fileInfo')
    fileInfoContainer.innerHTML = `
      <div class="error-message">
        <span class="error-icon">⚠️</span>
        <span class="error-text">${message}</span>
      </div>
    `
  }

  showError(message) {
    const contentContainer = this.container.querySelector('#fileContent')
    contentContainer.innerHTML = `
      <div class="error-state">
        <h3>Error</h3>
        <p>${message}</p>
        <button class="btn btn-secondary" onclick="location.reload()">
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
  }
}