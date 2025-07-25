import { api } from '../services/api.js';

/**
 * Authentication Component
 */
export class AuthComponent {
  constructor(container) {
    this.container = container;
    this.isLoading = false;
    
    this.render();
    this.attachEventListeners();
  }

  render() {
    const html = `
      <div class="container" style="max-width: 400px; margin: 4rem auto; padding: 2rem;">
        <div class="card">
          <div class="card-body">
            <div class="text-center mb-4">
              <h1 style="color: var(--color-primary); margin-bottom: 0.5rem;">Claude Web UI</h1>
              <p class="text-muted">Sign in to access your Claude Code jobs</p>
            </div>

            <form id="login-form" data-testid="login-form">
              <div class="form-group">
                <label class="form-label" for="username">Username</label>
                <input 
                  type="text" 
                  class="form-control" 
                  id="username" 
                  data-testid="username"
                  required 
                  autocomplete="username"
                />
              </div>

              <div class="form-group">
                <label class="form-label" for="password">Password</label>
                <input 
                  type="password" 
                  class="form-control" 
                  id="password" 
                  data-testid="password"
                  required 
                  autocomplete="current-password"
                />
              </div>

              <div class="form-group">
                <button 
                  type="submit" 
                  class="btn btn-primary" 
                  style="width: 100%;"
                  id="login-button"
                  data-testid="login-button"
                  disabled
                >
                  <span class="spinner hidden" id="login-spinner"></span>
                  <span id="login-text">Sign In</span>
                </button>
              </div>

              <div class="hidden" id="error-message" data-testid="error-message">
                <div style="background-color: rgb(239 68 68 / 0.1); border: 1px solid var(--color-error); color: var(--color-error); padding: 0.75rem; border-radius: var(--radius); margin-top: 1rem;">
                  <span id="error-text"></span>
                </div>
              </div>
            </form>
          </div>
        </div>
      </div>
    `;

    this.container.innerHTML = html;
  }

  attachEventListeners() {
    const form = document.getElementById('login-form');
    const usernameInput = document.getElementById('username');
    const passwordInput = document.getElementById('password');
    const loginButton = document.getElementById('login-button');

    // Form validation
    const validateForm = () => {
      const isValid = usernameInput.value.trim() && passwordInput.value && !this.isLoading;
      loginButton.disabled = !isValid;
    };

    usernameInput.addEventListener('input', validateForm);
    passwordInput.addEventListener('input', validateForm);

    // Form submission
    form.addEventListener('submit', this.handleLogin.bind(this));

    // Initial validation
    validateForm();
  }

  async handleLogin(e) {
    e.preventDefault();
    
    if (this.isLoading) return;

    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;

    if (!username || !password) {
      this.showError('Please enter both username and password.');
      return;
    }

    this.isLoading = true;
    this.showLoading(true);
    this.hideError();

    try {
      await api.login(username, password);
      
      // Redirect to dashboard
      window.location.href = '/';
      
    } catch (error) {
      console.error('Login failed:', error);
      this.showError(error.message || 'Login failed. Please check your credentials.');
      this.isLoading = false;
      this.showLoading(false);
    }
  }

  showLoading(loading) {
    const spinner = document.getElementById('login-spinner');
    const text = document.getElementById('login-text');
    const button = document.getElementById('login-button');
    
    if (loading) {
      spinner.classList.remove('hidden');
      text.textContent = 'Signing In...';
      button.disabled = true;
    } else {
      spinner.classList.add('hidden');
      text.textContent = 'Sign In';
      // Re-validate form
      const username = document.getElementById('username').value.trim();
      const password = document.getElementById('password').value;
      button.disabled = !(username && password && !this.isLoading);
    }
  }

  showError(message) {
    const errorContainer = document.getElementById('error-message');
    const errorText = document.getElementById('error-text');
    
    errorText.textContent = message;
    errorContainer.classList.remove('hidden');
  }

  hideError() {
    const errorContainer = document.getElementById('error-message');
    errorContainer.classList.add('hidden');
  }
}