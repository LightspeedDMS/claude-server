/**
 * Modern Dashboard Application
 * Component-based architecture with routing support
 */

import { router } from './services/router.js'
import { RepositoryListComponent } from './components/repository-list.js'
import { RepositoryDetailsComponent } from './components/repository-details.js'
import { RepositoryBrowserComponent } from './components/repository-browser.js'
import { FileViewerComponent } from './components/file-viewer.js'
import { RepositoryRegisterComponent } from './components/repository-register.js'

export class ModernDashboard {
  constructor() {
    this.currentComponent = null
    this.container = document.getElementById('main-content')
    
    this.init()
  }

  init() {
    // Set up routes
    router.addRoute('/repositories', () => this.showRepositories())
    router.addRoute('/repositories/register', () => this.showRegisterRepository())
    router.addRoute('/repositories/:name', (state) => this.showRepositoryDetails(state.name))
    router.addRoute('/repositories/:name/browse', (state) => this.showRepositoryBrowser(state.name, state.path))
    router.addRoute('/repositories/:name/file/:path', (state) => this.showFileViewer(state.name, state.path))
    
    // Initialize router
    router.init()
    
    // Set up navigation
    this.setupNavigation()
  }

  setupNavigation() {
    // Update existing navigation to use router
    const navRepositories = document.getElementById('nav-repositories')
    if (navRepositories) {
      navRepositories.addEventListener('click', () => {
        router.navigate('/repositories')
      })
    }
  }

  showRepositories() {
    this.clearCurrentComponent()
    
    this.currentComponent = new RepositoryListComponent(this.container, {
      onRegisterRepository: () => {
        router.navigate('/repositories/register')
      },
      onViewRepository: (repoName) => {
        router.navigate(`/repositories/${repoName}`)
      }
    })
    
    this.updateActiveNav('repositories')
  }

  showRegisterRepository() {
    this.clearCurrentComponent()
    
    this.currentComponent = new RepositoryRegisterComponent(this.container, {
      onCancel: () => {
        router.navigate('/repositories')
      },
      onSuccess: (repoName) => {
        router.navigate(`/repositories/${repoName}`)
      }
    })
    
    this.updateActiveNav('repositories')
  }

  showRepositoryDetails(repoName) {
    this.clearCurrentComponent()
    
    this.currentComponent = new RepositoryDetailsComponent(this.container, repoName, {
      onBack: () => {
        router.navigate('/repositories')
      },
      onBrowseFiles: (repoName) => {
        router.navigate(`/repositories/${repoName}/browse`)
      },
      onCreateJob: (repoName) => {
        // Navigate to job creation with pre-selected repository
        router.navigate('/jobs/create', { repository: repoName })
      },
      onUnregistered: (repoName) => {
        router.navigate('/repositories')
      }
    })
    
    this.updateActiveNav('repositories')
  }

  showRepositoryBrowser(repoName, path = '') {
    this.clearCurrentComponent()
    
    this.currentComponent = new RepositoryBrowserComponent(this.container, repoName, {
      onBack: () => {
        router.navigate(`/repositories/${repoName}`)
      },
      onViewFile: (repoName, filePath) => {
        router.navigate(`/repositories/${repoName}/file/${encodeURIComponent(filePath)}`)
      },
      initialPath: path
    })
    
    this.updateActiveNav('repositories')
  }

  showFileViewer(repoName, filePath) {
    this.clearCurrentComponent()
    
    const decodedPath = decodeURIComponent(filePath)
    
    this.currentComponent = new FileViewerComponent(this.container, repoName, decodedPath, {
      onBack: () => {
        router.navigate(`/repositories/${repoName}/browse`)
      }
    })
    
    this.updateActiveNav('repositories')
  }

  clearCurrentComponent() {
    if (this.currentComponent && typeof this.currentComponent.destroy === 'function') {
      this.currentComponent.destroy()
    }
    this.currentComponent = null
    
    // Clear container
    if (this.container) {
      this.container.innerHTML = ''
    }
  }

  updateActiveNav(section) {
    // Update navigation active state
    document.querySelectorAll('.nav-item').forEach(item => {
      item.classList.remove('active')
    })
    
    const activeNav = document.getElementById(`nav-${section}`)
    if (activeNav) {
      activeNav.classList.add('active')
    }
    
    // Hide all views
    document.querySelectorAll('.view').forEach(view => {
      view.classList.remove('active')
    })
  }

  destroy() {
    this.clearCurrentComponent()
  }
}

// Export for global use
export default ModernDashboard