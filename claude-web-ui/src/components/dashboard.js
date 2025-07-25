import { AuthService } from '../services/auth.js'
import { JobListComponent } from './job-list.js'
import { JobDetailsComponent } from './job-details.js'
import { RepositoryListComponent } from './repository-list.js'
import { RepositoryRegisterComponent } from './repository-register.js'
import { RepositoryDetailsComponent } from './repository-details.js'

/**
 * Dashboard Component
 * Main application interface after authentication
 */
export class DashboardComponent {
  constructor(container, options = {}) {
    this.container = container
    this.options = options
    this.authService = new AuthService()
    this.currentView = 'jobs'
    this.currentComponent = null
    
    this.init()
  }

  init() {
    this.render()
    this.bindEvents()
    this.showJobList()
  }

  render() {
    this.container.innerHTML = `
      <div class="dashboard-layout" data-testid="dashboard">
        <header class="header">
          <div class="container">
            <div class="header-content">
              <a href="#" class="header-brand">
                Claude Batch Server
              </a>
              
              <nav class="header-nav">
                <a href="#" id="navJobs" class="nav-link active" data-testid="jobs-nav">
                  Jobs
                </a>
                <a href="#" id="navRepositories" class="nav-link" data-testid="repositories-nav">
                  Repositories
                </a>
              </nav>
              
              <div class="header-user">
                <div class="user-menu" data-testid="user-menu">
                  <div class="user-menu-trigger" id="userMenuTrigger">
                    <span>👤</span>
                    <span>User</span>
                    <span>▼</span>
                  </div>
                  <div class="user-menu-dropdown hidden" id="userMenuDropdown">
                    <a href="#" class="user-menu-item" id="logoutBtn" data-testid="logout-button">
                      Logout
                    </a>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </header>
        
        <main class="main-content" id="mainContent">
          <!-- Dynamic content will be rendered here -->
        </main>
      </div>
    `
  }

  bindEvents() {
    // Navigation
    const navJobs = this.container.querySelector('#navJobs')
    const navRepos = this.container.querySelector('#navRepositories')
    
    navJobs.addEventListener('click', (e) => {
      e.preventDefault()
      this.navigateTo('jobs')
    })
    
    navRepos.addEventListener('click', (e) => {
      e.preventDefault()
      this.navigateTo('repositories')
    })
    
    // User menu
    const userMenuTrigger = this.container.querySelector('#userMenuTrigger')
    const userMenuDropdown = this.container.querySelector('#userMenuDropdown')
    
    userMenuTrigger.addEventListener('click', (e) => {
      e.preventDefault()
      userMenuDropdown.classList.toggle('hidden')
    })
    
    // Close menu when clicking outside
    document.addEventListener('click', (e) => {
      if (!userMenuTrigger.contains(e.target)) {
        userMenuDropdown.classList.add('hidden')
      }
    })
    
    // Logout
    const logoutBtn = this.container.querySelector('#logoutBtn')
    logoutBtn.addEventListener('click', async (e) => {
      e.preventDefault()
      await this.handleLogout()
    })
  }

  navigateTo(view) {
    // Update active nav link
    this.container.querySelectorAll('.nav-link').forEach(link => {
      link.classList.remove('active')
    })
    
    const activeLink = this.container.querySelector(`#nav${view.charAt(0).toUpperCase() + view.slice(1)}`)
    if (activeLink) {
      activeLink.classList.add('active')
    }
    
    // Show appropriate view
    this.currentView = view
    
    switch (view) {
      case 'jobs':
        this.showJobList()
        break
      case 'repositories':
        this.showRepositories()
        break
      default:
        this.showJobList()
    }
  }

  showJobList() {
    this.destroyCurrentComponent()
    
    const mainContent = this.container.querySelector('#mainContent')
    this.currentComponent = new JobListComponent(mainContent, {
      onViewJob: (jobId) => this.showJobDetails(jobId),
      onCreateJob: () => this.showCreateJob()
    })
  }

  showJobDetails(jobId) {
    this.destroyCurrentComponent()
    
    const mainContent = this.container.querySelector('#mainContent')
    this.currentComponent = new JobDetailsComponent(mainContent, jobId, {
      onBack: () => this.showJobList(),
      onJobDeleted: () => this.showJobList(),
      onShowFiles: (jobId) => this.showFileBrowser(jobId)
    })
  }

  showCreateJob() {
    // For now, show an alert - this would be implemented as a separate component
    alert('Job creation interface coming soon! Use the CLI for now.')
  }

  showRepositories() {
    this.destroyCurrentComponent()
    
    const mainContent = this.container.querySelector('#mainContent')
    this.currentComponent = new RepositoryListComponent(mainContent, {
      onViewRepository: (repoName) => this.showRepositoryDetails(repoName),
      onRegisterRepository: () => this.showRegisterRepository()
    })
  }

  showRegisterRepository() {
    this.destroyCurrentComponent()
    
    const mainContent = this.container.querySelector('#mainContent')
    this.currentComponent = new RepositoryRegisterComponent(mainContent, {
      onSuccess: (response) => this.showRepositories(),
      onCancel: () => this.showRepositories()
    })
  }

  showRepositoryDetails(repositoryName) {
    this.destroyCurrentComponent()
    
    const mainContent = this.container.querySelector('#mainContent')
    this.currentComponent = new RepositoryDetailsComponent(mainContent, repositoryName, {
      onBack: () => this.showRepositories(),
      onCreateJob: (repoName) => this.showCreateJobForRepository(repoName),
      onViewFiles: (repoName) => this.showRepositoryFiles(repoName),
      onUnregistered: () => this.showRepositories()
    })
  }

  showCreateJobForRepository(repositoryName) {
    // For now, show an alert - this would navigate to job creation with pre-selected repo
    alert(`Job creation for repository "${repositoryName}" coming soon! Use the Jobs tab for now.`)
  }

  showRepositoryFiles(repositoryName) {
    // For now, show an alert - this would be implemented as a file browser component
    alert(`File browser for repository "${repositoryName}" coming soon! Files can be accessed via the API.`)
  }

  showFileBrowser(jobId) {
    // For now, show an alert - this would be implemented as a separate component
    alert(`File browser for job ${jobId} coming soon! Files can be accessed via the API.`)
  }

  async handleLogout() {
    try {
      await this.authService.logout()
      
      if (this.options.onLogout) {
        this.options.onLogout()
      }
    } catch (error) {
      console.error('Logout failed:', error)
      // Still redirect even if logout API call fails
      if (this.options.onLogout) {
        this.options.onLogout()
      }
    }
  }

  destroyCurrentComponent() {
    if (this.currentComponent && typeof this.currentComponent.destroy === 'function') {
      this.currentComponent.destroy()
    }
    this.currentComponent = null
  }

  destroy() {
    this.destroyCurrentComponent()
  }
}