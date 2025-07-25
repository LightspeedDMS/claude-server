import { FileBrowserComponent } from './file-browser.js'

/**
 * File Browser Demo Component
 * Standalone demo of the file browser functionality
 */
export class FileBrowserDemoComponent {
  constructor(container, jobId) {
    this.container = container
    this.jobId = jobId
    this.fileBrowser = null
    
    this.init()
  }

  init() {
    this.render()
    this.initFileBrowser()
  }

  render() {
    this.container.innerHTML = `
      <div class="file-browser-demo" data-testid="file-browser-demo">
        <div class="demo-header">
          <h2>Workspace File Browser</h2>
          <p>Browse, preview, and download files from the job workspace</p>
        </div>
        
        <div class="demo-content" id="fileBrowserContainer">
          <!-- File browser will be rendered here -->
        </div>
      </div>
    `
  }

  initFileBrowser() {
    const container = this.container.querySelector('#fileBrowserContainer')
    
    try {
      this.fileBrowser = new FileBrowserComponent(container, this.jobId, {
        onError: (error) => {
          console.error('File browser demo error:', error)
          this.showError(error.message)
        },
        onFileSelect: (file) => {
          console.log('File selected:', file)
        }
      })
    } catch (error) {
      console.error('Failed to initialize file browser demo:', error)
      this.showError(error.message)
    }
  }

  showError(message) {
    const container = this.container.querySelector('#fileBrowserContainer')
    container.innerHTML = `
      <div class="error-state">
        <div class="error-icon">❌</div>
        <h3>File Browser Error</h3>
        <p>${message}</p>
        <button class="btn btn-primary" onclick="location.reload()">
          Reload Page
        </button>
      </div>
    `
  }

  destroy() {
    if (this.fileBrowser) {
      this.fileBrowser.destroy()
      this.fileBrowser = null
    }
  }
}

export default FileBrowserDemoComponent