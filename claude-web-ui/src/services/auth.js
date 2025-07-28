/**
 * Authentication Service
 * Handles user authentication, JWT token management, and localStorage integration
 * Follows the exact patterns from the C# CLI AuthService
 */
export class AuthService {
  constructor() {
    this.token = null
    this.user = null
    this.tokenKey = 'claude_token'
    
    // Load token from localStorage on initialization
    this.loadTokenFromStorage()
  }

  /**
   * Load token from localStorage and validate it
   */
  loadTokenFromStorage() {
    const storedToken = localStorage.getItem(this.tokenKey)
    if (storedToken && this.isTokenValid(storedToken)) {
      this.token = storedToken
      this.user = this.extractUserFromToken(storedToken)
    } else if (storedToken) {
      // Token is invalid, clear it
      this.clearToken()
    }
  }

  /**
   * Check if user is authenticated
   */
  async isAuthenticated() {
    if (!this.token) {
      return false
    }

    // First check if token is still valid (not expired)
    if (!this.isTokenValid(this.token)) {
      this.clearToken()
      return false
    }

    return true
  }

  /**
   * Login with credentials
   * IMPORTANT: Must use /api prefix for Vite proxy to forward to API server on port 5185
   */
  async login(username, password) {
    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ username, password })
      })

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}))
        throw new Error(errorData.message || `HTTP ${response.status}: ${response.statusText}`)
      }

      const data = await response.json()
      
      if (!data.token) {
        throw new Error('No authentication token received')
      }

      // Store token and extract user info
      this.setToken(data.token)
      this.user = this.extractUserFromToken(data.token)
      
      return {
        success: true,
        user: this.user
      }
    } catch (error) {
      console.error('Login failed:', error)
      throw error
    }
  }

  /**
   * Logout current user
   * IMPORTANT: Must use /api prefix for Vite proxy to forward to API server on port 5185
   */
  async logout() {
    try {
      // Notify server about logout if we have a token
      if (this.token) {
        await fetch('/api/auth/logout', {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${this.token}`,
            'Content-Type': 'application/json'
          }
        })
      }
    } catch (error) {
      console.warn('Server logout notification failed:', error)
      // Continue with local logout even if server call fails
    }
    
    // Clear local authentication state
    this.clearToken()
  }

  /**
   * Set authentication token and store in localStorage
   */
  setToken(token) {
    this.token = token
    localStorage.setItem(this.tokenKey, token)
  }

  /**
   * Get current authentication token
   */
  getToken() {
    return this.token
  }

  /**
   * Clear authentication token and user data
   */
  clearToken() {
    this.token = null
    this.user = null
    localStorage.removeItem(this.tokenKey)
  }

  /**
   * Get current user information
   */
  getCurrentUser() {
    if (!this.user && this.token) {
      this.user = this.extractUserFromToken(this.token)
    }
    return this.user
  }

  /**
   * Get authorization header value
   */
  getAuthHeader() {
    return this.token ? `Bearer ${this.token}` : null
  }

  /**
   * Check if JWT token is valid (not expired)
   * @param {string} token - JWT token to validate
   * @returns {boolean} - True if token is valid
   */
  isTokenValid(token) {
    try {
      const payload = this.parseJWTPayload(token)
      if (!payload || !payload.exp) {
        return false
      }

      // Check if token expires within the next minute (1 minute buffer)
      const expirationTime = payload.exp * 1000 // Convert to milliseconds
      const currentTime = Date.now()
      const bufferTime = 60 * 1000 // 1 minute buffer

      return expirationTime > (currentTime + bufferTime)
    } catch (error) {
      console.warn('Token validation failed:', error)
      return false
    }
  }

  /**
   * Extract user information from JWT token
   * @param {string} token - JWT token
   * @returns {Object|null} - User information or null
   */
  extractUserFromToken(token) {
    try {
      const payload = this.parseJWTPayload(token)
      if (!payload) return null

      // Extract username from standard JWT claims
      const username = payload.sub || payload.name || payload.unique_name || payload.username
      
      if (!username) {
        console.warn('No username found in JWT token')
        return null
      }

      return {
        username,
        // Include any other relevant user data from the token
        exp: payload.exp,
        iat: payload.iat
      }
    } catch (error) {
      console.warn('Failed to extract user from token:', error)
      return null
    }
  }

  /**
   * Parse JWT payload without signature verification (client-side utility)
   * @param {string} token - JWT token
   * @returns {Object|null} - Parsed payload or null
   */
  parseJWTPayload(token) {
    try {
      const parts = token.split('.')
      if (parts.length !== 3) {
        throw new Error('Invalid JWT format')
      }

      // Decode base64url payload
      const payload = parts[1]
      const decoded = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
      return JSON.parse(decoded)
    } catch (error) {
      console.warn('Failed to parse JWT payload:', error)
      return null
    }
  }

  /**
   * Handle authentication error (token expired, etc.)
   */
  handleAuthError() {
    console.warn('Authentication error detected, clearing local token')
    this.clearToken()
    
    // Trigger a page reload to show login form
    // This ensures the app goes back to unauthenticated state
    if (typeof window !== 'undefined') {
      window.location.reload()
    }
  }
}

// Export singleton instance
export const authService = new AuthService()
export default authService