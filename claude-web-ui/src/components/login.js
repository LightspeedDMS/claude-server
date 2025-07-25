/**
 * Login Component
 * Clean, elegant login form with validation and error handling
 * Claude-inspired design with proper data-testid attributes for E2E testing
 */

import authService from '../services/auth.js'

export class LoginComponent {
  constructor(container, options = {}) {
    this.container = container
    this.options = options
    this.isLoading = false
    this.render()
    this.bindEvents()
  }

  render() {
    this.container.innerHTML = `
      <div class="login-container" data-testid="login-container">
        <div class="login-card">
          <div class="login-header">
            <h1 class="login-title">Claude Batch Server</h1>
            <p class="login-subtitle">Sign in to access your workspace</p>
          </div>

          <form class="login-form" data-testid="login-form">
            <div class="form-group">
              <label for="username" class="form-label">Username</label>
              <input 
                type="text" 
                id="username" 
                name="username"
                class="form-input" 
                data-testid="username"
                placeholder="Enter your username"
                required
                autocomplete="username"
              />
            </div>

            <div class="form-group">
              <label for="password" class="form-label">Password</label>
              <input 
                type="password" 
                id="password" 
                name="password"
                class="form-input" 
                data-testid="password"
                placeholder="Enter your password"
                required
                autocomplete="current-password"
              />
            </div>

            <div class="form-group error-container" style="display: none;">
              <div class="error-message" data-testid="error-message"></div>
            </div>

            <div class="form-actions">
              <button 
                type="submit" 
                class="login-button" 
                data-testid="login-button"
              >
                <span class="button-text">Sign In</span>
                <div class="loading-spinner" style="display: none;">
                  <div class="spinner"></div>
                </div>
              </button>
            </div>
          </form>

          <div class="login-footer">
            <p class="footer-text">
              Need help? Contact your system administrator
            </p>
          </div>
        </div>
      </div>
    `
  }

  bindEvents() {
    const form = this.container.querySelector('.login-form')
    const usernameInput = this.container.querySelector('#username')
    const passwordInput = this.container.querySelector('#password')

    // Form submission
    form.addEventListener('submit', async (e) => {
      e.preventDefault()
      await this.handleLogin()
    })

    // Clear errors on input
    usernameInput.addEventListener('input', () => this.clearError())
    passwordInput.addEventListener('input', () => this.clearError())

    // Focus on username field
    usernameInput.focus()
  }

  async handleLogin() {
    if (this.isLoading) return

    const form = this.container.querySelector('.login-form')
    const formData = new FormData(form)
    const username = formData.get('username')?.trim()
    const password = formData.get('password')

    // Validate inputs
    if (!username || !password) {
      this.showError('Please enter both username and password')
      return
    }

    this.setLoading(true)
    this.clearError()

    try {
      const result = await authService.login(username, password)
      
      if (result.success) {
        // Login successful - call callback or redirect
        if (this.options.onLogin) {
          this.options.onLogin(result.user)
        } else {
          // Default redirect to dashboard
          window.location.href = '/dashboard'
        }
      } else {
        this.showError('Login failed. Please check your credentials.')
      }
    } catch (error) {
      console.error('Login error:', error)
      
      // Show user-friendly error message
      let errorMessage = 'Login failed. Please try again.'
      
      if (error.message) {
        if (error.message.includes('401') || error.message.includes('Unauthorized')) {
          errorMessage = 'Invalid username or password'
        } else if (error.message.includes('Network')) {
          errorMessage = 'Network error. Please check your connection.'
        } else if (error.message.includes('timeout')) {
          errorMessage = 'Request timed out. Please try again.'
        }
      }
      
      this.showError(errorMessage)
    } finally {
      this.setLoading(false)
    }
  }

  setLoading(loading) {
    this.isLoading = loading
    const button = this.container.querySelector('.login-button')
    const buttonText = this.container.querySelector('.button-text')
    const spinner = this.container.querySelector('.loading-spinner')
    const inputs = this.container.querySelectorAll('.form-input')

    if (loading) {
      button.disabled = true
      button.classList.add('loading')
      buttonText.style.display = 'none'
      spinner.style.display = 'flex'
      
      inputs.forEach(input => {
        input.disabled = true
      })
    } else {
      button.disabled = false
      button.classList.remove('loading')
      buttonText.style.display = 'block'
      spinner.style.display = 'none'
      
      inputs.forEach(input => {
        input.disabled = false
      })
    }
  }

  showError(message) {
    const errorContainer = this.container.querySelector('.error-container')
    const errorMessage = this.container.querySelector('.error-message')
    
    errorMessage.textContent = message
    errorContainer.style.display = 'block'
    
    // Add error styling to form
    const form = this.container.querySelector('.login-form')
    form.classList.add('has-error')
    
    // Focus on first input for retry
    setTimeout(() => {
      const firstInput = this.container.querySelector('.form-input')
      if (firstInput && !firstInput.disabled) {
        firstInput.focus()
        firstInput.select()
      }
    }, 100)
  }

  clearError() {
    const errorContainer = this.container.querySelector('.error-container')
    const form = this.container.querySelector('.login-form')
    
    errorContainer.style.display = 'none'
    form.classList.remove('has-error')
  }

  destroy() {
    // Clean up any event listeners if needed
    this.container.innerHTML = ''
  }
}