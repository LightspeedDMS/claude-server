import apiClient from '../services/api.js'

/**
 * Repository Registration Component
 * Form for registering new Git repositories
 */
export class RepositoryRegisterComponent {
  constructor(container, options = {}) {
    this.container = container
    this.options = options
    this.isSubmitting = false
    
    this.init()
  }

  init() {
    this.render()
    this.bindEvents()
    this.updateSubmitButton()
  }

  render() {
    this.container.innerHTML = `
      <div class="repository-register-container">
        <div class="repository-register-header">
          <button class="btn btn-outline back-button" id="backButton" data-testid="back-button">
            <span>←</span> Back to Repositories
          </button>
          <h1>Register New Repository</h1>
          <p>Add a Git repository to use with Claude Code jobs</p>
        </div>

        <form class="repository-register-form" id="registerForm" data-testid="repository-register-form">
          <div class="form-section">
            <div class="form-group">
              <label class="form-label" for="repository-name">
                Repository Name <span class="required">*</span>
              </label>
              <input 
                type="text" 
                class="form-control" 
                id="repository-name"
                name="name"
                data-testid="repository-name"
                placeholder="my-awesome-project"
                required
                pattern="^[a-zA-Z0-9][a-zA-Z0-9-_]*[a-zA-Z0-9]$"
                title="Repository name must start and end with alphanumeric characters and can contain hyphens and underscores"
              />
              <small class="form-help">
                Used to identify the repository. Must be unique and contain only letters, numbers, hyphens, and underscores.
              </small>
              <div class="form-error" id="name-error"></div>
            </div>

            <div class="form-group">
              <label class="form-label" for="git-url">
                Git URL <span class="required">*</span>
              </label>
              <input 
                type="url" 
                class="form-control" 
                id="git-url"
                name="gitUrl"
                data-testid="git-url"
                placeholder="https://github.com/username/repository.git"
                required
              />
              <small class="form-help">
                HTTPS Git URL of the repository. SSH URLs are not supported in this version.
              </small>
              <div class="form-error" id="git-url-error"></div>
            </div>

            <div class="form-group">
              <label class="form-label" for="description">
                Description <span class="optional">(optional)</span>
              </label>
              <textarea 
                class="form-control" 
                id="description"
                name="description"
                data-testid="description"
                placeholder="Brief description of what this repository contains..."
                rows="3"
                maxlength="500"
              ></textarea>
              <small class="form-help">
                Optional description to help identify the repository's purpose.
              </small>
            </div>
          </div>

          <div class="form-section">
            <div class="info-card">
              <div class="info-card-header">
                <span class="info-icon">ℹ️</span>
                <h3>What happens when you register a repository?</h3>
              </div>
              <div class="info-card-body">
                <ul>
                  <li>The repository will be cloned to the server</li>
                  <li>Claude Code will have access to all files and history</li>
                  <li>The repository is kept up-to-date with the remote</li>
                  <li>You can use this repository in job creation</li>
                  <li>Multiple jobs can use the same repository simultaneously</li>
                </ul>
              </div>
            </div>
          </div>

          <div class="form-actions">
            <button 
              type="button" 
              class="btn btn-secondary" 
              id="cancelButton"
              data-testid="cancel-button"
            >
              Cancel
            </button>
            <button 
              type="submit" 
              class="btn btn-primary" 
              id="submitButton"
              data-testid="submit-button"
              disabled
            >
              <span class="spinner hidden" id="submit-spinner"></span>
              <span id="submit-text">Register Repository</span>
            </button>
          </div>
        </form>

        <!-- Success/Error Messages -->
        <div class="message-container hidden" id="messageContainer">
          <div class="message-card" id="messageCard">
            <div class="message-header">
              <span class="message-icon" id="messageIcon"></span>
              <h3 class="message-title" id="messageTitle"></h3>
            </div>
            <div class="message-body">
              <p id="messageText"></p>
              <div class="message-actions" id="messageActions"></div>
            </div>
          </div>
        </div>
      </div>
    `
  }

  bindEvents() {
    const form = this.container.querySelector('#registerForm')
    const backButton = this.container.querySelector('#backButton')
    const cancelButton = this.container.querySelector('#cancelButton')
    const nameInput = this.container.querySelector('#repository-name')
    const gitUrlInput = this.container.querySelector('#git-url')

    // Form submission
    form.addEventListener('submit', this.handleSubmit.bind(this))

    // Back/Cancel buttons
    backButton.addEventListener('click', () => {
      if (this.options.onCancel) {
        this.options.onCancel()
      }
    })

    cancelButton.addEventListener('click', () => {
      if (this.options.onCancel) {
        this.options.onCancel()
      }
    })

    // Input validation
    nameInput.addEventListener('input', () => {
      this.validateName()
      this.updateSubmitButton()
    })

    nameInput.addEventListener('blur', () => {
      this.validateName()
    })

    gitUrlInput.addEventListener('input', () => {
      this.validateGitUrl()
      this.updateSubmitButton()
    })

    gitUrlInput.addEventListener('blur', () => {
      this.validateGitUrl()
    })

    // Real-time form validation
    form.addEventListener('input', () => {
      this.updateSubmitButton()
    })
  }

  validateName() {
    const nameInput = this.container.querySelector('#repository-name')
    const errorElement = this.container.querySelector('#name-error')
    const name = nameInput.value.trim()

    errorElement.textContent = ''
    nameInput.classList.remove('error')

    if (!name) {
      return false
    }

    // Check pattern
    const pattern = /^[a-zA-Z0-9][a-zA-Z0-9-_]*[a-zA-Z0-9]$/
    if (name.length === 1) {
      // Single character is ok if alphanumeric
      if (!/^[a-zA-Z0-9]$/.test(name)) {
        errorElement.textContent = 'Repository name must contain only letters, numbers, hyphens, and underscores'
        nameInput.classList.add('error')
        return false
      }
    } else if (!pattern.test(name)) {
      errorElement.textContent = 'Repository name must start and end with alphanumeric characters'
      nameInput.classList.add('error')
      return false
    }

    if (name.length < 1 || name.length > 100) {
      errorElement.textContent = 'Repository name must be between 1 and 100 characters'
      nameInput.classList.add('error')
      return false
    }

    return true
  }

  validateGitUrl() {
    const gitUrlInput = this.container.querySelector('#git-url')
    const errorElement = this.container.querySelector('#git-url-error')
    const url = gitUrlInput.value.trim()

    errorElement.textContent = ''
    gitUrlInput.classList.remove('error')

    if (!url) {
      return false
    }

    try {
      const urlObj = new URL(url)
      
      // Check if it's HTTPS
      if (urlObj.protocol !== 'https:') {
        errorElement.textContent = 'Only HTTPS Git URLs are supported'
        gitUrlInput.classList.add('error')
        return false
      }

      // Check if it looks like a Git URL
      if (!url.endsWith('.git')) {
        errorElement.textContent = 'Git URL should end with .git'
        gitUrlInput.classList.add('error')
        return false
      }

      // Check for common Git hosting patterns
      const validHosts = ['github.com', 'gitlab.com', 'bitbucket.org']
      const isKnownHost = validHosts.some(host => urlObj.hostname === host || urlObj.hostname.endsWith('.' + host))
      
      if (!isKnownHost && !urlObj.hostname.includes('.')) {
        errorElement.textContent = 'Please enter a valid Git repository URL'
        gitUrlInput.classList.add('error')
        return false
      }

      return true
    } catch (e) {
      errorElement.textContent = 'Please enter a valid URL'
      gitUrlInput.classList.add('error')
      return false
    }
  }

  updateSubmitButton() {
    const nameInput = this.container.querySelector('#repository-name')
    const gitUrlInput = this.container.querySelector('#git-url')
    const submitButton = this.container.querySelector('#submitButton')
    
    const nameValid = nameInput.value.trim() && this.validateName()
    const gitUrlValid = gitUrlInput.value.trim() && this.validateGitUrl()
    const isValid = nameValid && gitUrlValid && !this.isSubmitting
    
    submitButton.disabled = !isValid
  }

  async handleSubmit(e) {
    e.preventDefault()
    
    if (this.isSubmitting) return

    const formData = new FormData(e.target)
    const data = {
      name: formData.get('name').trim(),
      gitUrl: formData.get('gitUrl').trim(),
      description: formData.get('description').trim()
    }

    // Final validation
    if (!this.validateName() || !this.validateGitUrl()) {
      return
    }

    this.isSubmitting = true
    this.updateSubmitButton()
    this.showSubmitLoading(true)
    this.hideMessage()

    try {
      const response = await apiClient.createRepository(data)
      
      this.showSuccess(
        'Repository Registered Successfully!',
        `Repository "${data.name}" has been registered and is being cloned. You can now use it in job creation.`,
        [
          {
            text: 'View Repositories',
            action: () => {
              if (this.options.onSuccess) {
                this.options.onSuccess(response)
              }
            },
            primary: true
          },
          {
            text: 'Register Another',
            action: () => {
              this.resetForm()
            }
          }
        ]
      )

    } catch (error) {
      console.error('Repository registration failed:', error)
      
      let errorMessage = 'Failed to register repository. Please try again.'
      
      if (error.status === 409) {
        errorMessage = `Repository "${data.name}" already exists. Please choose a different name.`
      } else if (error.status === 400) {
        errorMessage = error.message || 'Invalid repository data. Please check your input.'
      } else if (error.message) {
        errorMessage = error.message
      }

      this.showError('Registration Failed', errorMessage)
      
      this.isSubmitting = false
      this.updateSubmitButton()
      this.showSubmitLoading(false)
    }
  }

  showSubmitLoading(loading) {
    const spinner = this.container.querySelector('#submit-spinner')
    const text = this.container.querySelector('#submit-text')
    
    if (loading) {
      spinner.classList.remove('hidden')
      text.textContent = 'Registering Repository...'
    } else {
      spinner.classList.add('hidden')
      text.textContent = 'Register Repository'
    }
  }

  showSuccess(title, message, actions = []) {
    const container = this.container.querySelector('#messageContainer')
    const card = this.container.querySelector('#messageCard')
    const icon = this.container.querySelector('#messageIcon')
    const titleElement = this.container.querySelector('#messageTitle')
    const text = this.container.querySelector('#messageText')
    const actionsContainer = this.container.querySelector('#messageActions')
    
    icon.textContent = '✅'
    titleElement.textContent = title
    text.textContent = message
    
    card.className = 'message-card success'
    container.classList.remove('hidden')
    
    // Add action buttons
    actionsContainer.innerHTML = actions.map(action => `
      <button class="btn ${action.primary ? 'btn-primary' : 'btn-secondary'}" data-action="${actions.indexOf(action)}">
        ${action.text}
      </button>
    `).join('')

    // Bind action events
    actionsContainer.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', () => {
        const actionIndex = parseInt(btn.dataset.action)
        actions[actionIndex].action()
      })
    })

    // Scroll to message
    container.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }

  showError(title, message) {
    const container = this.container.querySelector('#messageContainer')
    const card = this.container.querySelector('#messageCard')
    const icon = this.container.querySelector('#messageIcon')
    const titleElement = this.container.querySelector('#messageTitle')
    const text = this.container.querySelector('#messageText')
    const actionsContainer = this.container.querySelector('#messageActions')
    
    icon.textContent = '❌'
    titleElement.textContent = title
    text.textContent = message
    
    card.className = 'message-card error'
    container.classList.remove('hidden')
    
    // Add retry button
    actionsContainer.innerHTML = `
      <button class="btn btn-primary" onclick="this.closest('.message-container').classList.add('hidden')">
        Try Again
      </button>
    `

    // Scroll to message
    container.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }

  hideMessage() {
    const container = this.container.querySelector('#messageContainer')
    container.classList.add('hidden')
  }

  resetForm() {
    const form = this.container.querySelector('#registerForm')
    form.reset()
    
    // Clear validation errors
    this.container.querySelectorAll('.form-error').forEach(error => {
      error.textContent = ''
    })
    
    this.container.querySelectorAll('.form-control').forEach(input => {
      input.classList.remove('error')
    })
    
    this.isSubmitting = false
    this.updateSubmitButton()
    this.showSubmitLoading(false)
    this.hideMessage()
    
    // Focus on first input
    this.container.querySelector('#repository-name').focus()
  }

  destroy() {
    // Cleanup if needed
  }
}