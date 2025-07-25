/**
 * Simple client-side router with authentication guards
 * Handles navigation between authenticated and unauthenticated views
 */

import authService from './auth.js'

export class AppRouter {
  constructor() {
    this.routes = new Map()
    this.currentRoute = null
    this.beforeNavigateCallbacks = []
    this.afterNavigateCallbacks = []
  }

  /**
   * Initialize the router
   */
  init() {
    // Handle browser navigation
    window.addEventListener('popstate', (event) => {
      this.handleLocationChange(event.state)
    })

    // Handle initial route
    this.handleLocationChange()
  }

  /**
   * Add a route
   * @param {string} path - Route path
   * @param {Function} handler - Route handler function
   * @param {boolean} requireAuth - Whether authentication is required
   */
  addRoute(path, handler, requireAuth = true) {
    this.routes.set(path, { handler, requireAuth })
  }

  /**
   * Navigate to a route
   * @param {string} path - Route path to navigate to
   * @param {Object} state - Optional state to pass
   */
  navigate(path, state = {}) {
    // Call before navigate callbacks
    for (const callback of this.beforeNavigateCallbacks) {
      const result = callback(path, state)
      if (result === false) {
        return // Navigation cancelled
      }
    }

    // Update browser history
    window.history.pushState(state, '', path)
    
    // Handle the route change
    this.handleLocationChange(state)
  }

  /**
   * Replace current route without adding to history
   * @param {string} path - Route path
   * @param {Object} state - Optional state
   */
  replace(path, state = {}) {
    window.history.replaceState(state, '', path)
    this.handleLocationChange(state)
  }

  /**
   * Go back in history
   */
  goBack() {
    window.history.back()
  }

  /**
   * Handle location change
   * @param {Object} state - History state
   */
  async handleLocationChange(state = {}) {
    const path = window.location.pathname
    const route = this.routes.get(path)

    // Check authentication if route requires it
    if (route && route.requireAuth) {
      const isAuthenticated = await authService.isAuthenticated()
      if (!isAuthenticated) {
        // Redirect to login
        this.replace('/login')
        return
      }
    }

    // Handle route
    if (route) {
      this.currentRoute = path
      route.handler(state)
    } else {
      // Handle unknown routes - redirect based on auth status
      const isAuthenticated = await authService.isAuthenticated()
      if (isAuthenticated) {
        this.replace('/dashboard')
      } else {
        this.replace('/login')
      }
    }

    // Call after navigate callbacks
    for (const callback of this.afterNavigateCallbacks) {
      callback(path, state)
    }
  }

  /**
   * Add before navigate callback
   * @param {Function} callback - Callback function
   */
  beforeNavigate(callback) {
    this.beforeNavigateCallbacks.push(callback)
  }

  /**
   * Add after navigate callback
   * @param {Function} callback - Callback function
   */
  afterNavigate(callback) {
    this.afterNavigateCallbacks.push(callback)
  }

  /**
   * Get current route
   */
  getCurrentRoute() {
    return this.currentRoute
  }

  /**
   * Check if current route requires authentication
   */
  currentRouteRequiresAuth() {
    const route = this.routes.get(this.currentRoute)
    return route ? route.requireAuth : false
  }
}

// Export singleton instance
export const router = new AppRouter()
export default router