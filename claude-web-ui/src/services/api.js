/**
 * API Client Service - Lightweight HTTP client with JWT authentication
 * Integrates with the AuthService for token management
 * Follows the exact patterns from the Claude Batch Server API
 * 
 * CRITICAL: Uses /api prefix which gets proxied by Vite to http://localhost:5185
 * DO NOT change baseURL from '/api' or authentication will break!
 */

import authService from './auth.js';

class ApiClient {
  constructor() {
    // CRITICAL: /api prefix is proxied by Vite to http://localhost:5185 (API server)
    // DO NOT CHANGE THIS or all API calls will fail!
    this.baseURL = '/api'; // Use Vite proxy to forward to API server
    this.timeout = 30000; // 30 seconds default timeout
  }

  /**
   * Make an authenticated HTTP request
   * @param {string} endpoint - API endpoint
   * @param {Object} options - Fetch options
   * @param {boolean} requireAuth - Whether authentication is required
   * @returns {Promise<Object>} Response data
   */
  async request(endpoint, options = {}, requireAuth = true) {
    const url = `${this.baseURL}/${endpoint.replace(/^\//, '')}`
    
    // Set up headers
    const headers = {
      'Content-Type': 'application/json',
      ...options.headers
    }

    // Add authentication header if required
    if (requireAuth) {
      const authHeader = authService.getAuthHeader()
      if (!authHeader) {
        throw new AuthenticationError('Authentication token is required but not available')
      }
      headers['Authorization'] = authHeader
    }

    // Create abort controller for timeout
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), this.timeout)

    try {
      const response = await fetch(url, {
        ...options,
        headers,
        signal: controller.signal
      })

      clearTimeout(timeoutId)

      // Handle authentication errors
      if (response.status === 401) {
        authService.handleAuthError()
        throw new AuthenticationError('Authentication failed or token expired')
      }

      // Handle other errors
      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}))
        throw new ApiError(
          errorData.message || `HTTP ${response.status}: ${response.statusText}`,
          response.status,
          errorData
        )
      }

      // Parse and return response
      const contentType = response.headers.get('content-type')
      if (contentType && contentType.includes('application/json')) {
        return await response.json()
      }
      
      return await response.text()
    } catch (error) {
      clearTimeout(timeoutId)
      
      if (error.name === 'AbortError') {
        throw new TimeoutError(`Request timed out after ${this.timeout}ms`)
      }
      
      if (error instanceof AuthenticationError || error instanceof ApiError) {
        throw error
      }
      
      throw new NetworkError(`Network request failed: ${error.message}`)
    }
  }

  // Basic HTTP methods
  async get(endpoint, params = {}, requireAuth = true) {
    const queryString = Object.keys(params).length > 0 ? '?' + new URLSearchParams(params).toString() : '';
    return this.request(`${endpoint}${queryString}`, { method: 'GET' }, requireAuth);
  }

  async post(endpoint, data = {}, params = {}, requireAuth = true) {
    return this.request(endpoint, {
      method: 'POST',
      body: JSON.stringify(data)
    }, requireAuth);
  }

  async put(endpoint, data = {}, params = {}, requireAuth = true) {
    return this.request(endpoint, {
      method: 'PUT',
      body: JSON.stringify(data)
    }, requireAuth);
  }

  async delete(endpoint, params = {}, requireAuth = true) {
    return this.request(endpoint, { method: 'DELETE' }, requireAuth);
  }

  // Authentication endpoints
  async login(credentials) {
    return this.request('auth/login', {
      method: 'POST',
      body: JSON.stringify(credentials)
    }, false)
  }

  async logout() {
    return this.request('auth/logout', {
      method: 'POST'
    })
  }

  // Repository endpoints
  async getRepositories() {
    return this.request('repositories')
  }

  async getRepository(name) {
    return this.request(`repositories/${encodeURIComponent(name)}`)
  }

  async createRepository(data) {
    return this.request('repositories/register', {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async deleteRepository(name) {
    return this.request(`repositories/${encodeURIComponent(name)}`, {
      method: 'DELETE'
    })
  }

  // Repository browsing endpoints
  async browseRepository(name, path = null) {
    const params = path ? `?path=${encodeURIComponent(path)}` : ''
    return this.request(`repositories/${encodeURIComponent(name)}/browse${params}`)
  }

  async getRepositoryFile(name, filePath) {
    const authHeader = authService.getAuthHeader()
    if (!authHeader) {
      throw new AuthenticationError('Authentication required for file download')
    }

    const response = await fetch(`${this.baseURL}/repositories/${encodeURIComponent(name)}/file/${encodeURIComponent(filePath)}`, {
      method: 'GET',
      headers: {
        'Authorization': authHeader
      }
    })

    if (response.status === 401) {
      authService.handleAuthError()
      throw new AuthenticationError('Authentication failed during file download')
    }

    if (!response.ok) {
      throw new ApiError(`File download failed: ${response.status}`, response.status)
    }

    return {
      blob: await response.blob(),
      filename: this.getFilenameFromResponse(response),
      contentType: response.headers.get('content-type')
    }
  }

  async getRepositoryFileInfo(name, filePath) {
    return this.request(`repositories/${encodeURIComponent(name)}/fileinfo/${encodeURIComponent(filePath)}`)
  }

  // Job endpoints
  async getJobs(filter = {}) {
    const params = new URLSearchParams()
    Object.entries(filter).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params.append(key, value)
      }
    })
    
    const queryString = params.toString()
    const endpoint = queryString ? `jobs?${queryString}` : 'jobs'
    return this.request(endpoint)
  }

  async getJob(jobId) {
    return this.request(`jobs/${jobId}`)
  }

  async createJob(data) {
    return this.request('jobs', {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async startJob(jobId) {
    return this.request(`jobs/${jobId}/start`, {
      method: 'POST'
    })
  }

  async cancelJob(jobId) {
    return this.request(`jobs/${jobId}/cancel`, {
      method: 'POST'
    })
  }

  async deleteJob(jobId) {
    return this.request(`jobs/${jobId}`, {
      method: 'DELETE'
    })
  }

  // File operations - Updated for scalable API
  async getJobDirectories(jobId, path = '') {
    const params = path ? `?path=${encodeURIComponent(path)}` : ''
    return this.request(`jobs/${jobId}/files/directories${params}`)
  }

  async getJobFiles(jobId, path = '', mask = null) {
    const params = new URLSearchParams()
    params.append('path', path)
    if (mask) params.append('mask', mask)
    return this.request(`jobs/${jobId}/files/files?${params}`)
  }

  async getJobFileContent(jobId, filePath) {
    return this.request(`jobs/${jobId}/files/content?path=${encodeURIComponent(filePath)}`)
  }

  async downloadJobFileBlob(jobId, filePath) {
    const authHeader = authService.getAuthHeader()
    if (!authHeader) {
      throw new AuthenticationError('Authentication required for file download')
    }

    const response = await fetch(`${this.baseURL}/jobs/${jobId}/files/download?path=${encodeURIComponent(filePath)}`, {
      method: 'GET',
      headers: {
        'Authorization': authHeader
      }
    })

    if (response.status === 401) {
      authService.handleAuthError()
      throw new AuthenticationError('Authentication failed during file download')
    }

    if (!response.ok) {
      throw new ApiError(`Download failed: ${response.status}`, response.status)
    }

    return {
      blob: await response.blob(),
      filename: this.getFilenameFromResponse(response),
      contentType: response.headers.get('content-type')
    }
  }

  async uploadFiles(jobId, files, onProgress = null) {
    const formData = new FormData()
    
    files.forEach(file => {
      formData.append('files', file)
    })

    // Use XMLHttpRequest for progress tracking
    if (onProgress) {
      return this.uploadWithProgress(`jobs/${jobId}/files`, formData, onProgress)
    }

    // Use regular fetch for simple uploads
    const authHeader = authService.getAuthHeader()
    if (!authHeader) {
      throw new AuthenticationError('Authentication required for file upload')
    }

    const response = await fetch(`${this.baseURL}/jobs/${jobId}/files`, {
      method: 'POST',
      headers: {
        'Authorization': authHeader
      },
      body: formData
    })

    if (response.status === 401) {
      authService.handleAuthError()
      throw new AuthenticationError('Authentication failed during file upload')
    }

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}))
      throw new ApiError(
        errorData.message || `Upload failed: ${response.status}`,
        response.status,
        errorData
      )
    }

    return await response.json()
  }

  async downloadJobFile(jobId, fileName) {
    const authHeader = authService.getAuthHeader()
    if (!authHeader) {
      throw new AuthenticationError('Authentication required for file download')
    }

    const response = await fetch(`${this.baseURL}/jobs/${jobId}/files/${encodeURIComponent(fileName)}`, {
      method: 'GET',
      headers: {
        'Authorization': authHeader
      }
    })

    if (response.status === 401) {
      authService.handleAuthError()
      throw new AuthenticationError('Authentication failed during file download')
    }

    if (!response.ok) {
      throw new ApiError(`Download failed: ${response.status}`, response.status)
    }

    return await response.blob()
  }

  /**
   * Upload files with progress tracking using XMLHttpRequest
   */
  async uploadWithProgress(endpoint, formData, onProgress) {
    const url = `${this.baseURL}/${endpoint.replace(/^\//, '')}`
    const authHeader = authService.getAuthHeader()
    
    if (!authHeader) {
      throw new AuthenticationError('Authentication required for file upload')
    }

    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      // Track upload progress
      if (xhr.upload && onProgress) {
        xhr.upload.addEventListener('progress', (e) => {
          if (e.lengthComputable) {
            const percent = Math.round((e.loaded / e.total) * 100)
            onProgress(percent)
          }
        })
      }

      // Handle completion
      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const response = JSON.parse(xhr.responseText)
            resolve(response)
          } catch {
            resolve({ success: true, data: xhr.responseText })
          }
        } else if (xhr.status === 401) {
          authService.handleAuthError()
          reject(new AuthenticationError('Authentication failed during file upload'))
        } else {
          reject(new ApiError(
            `Upload failed: ${xhr.status} ${xhr.statusText}`,
            xhr.status,
            xhr.responseText
          ))
        }
      })

      // Handle errors
      xhr.addEventListener('error', () => {
        reject(new NetworkError('File upload failed due to network error'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new TimeoutError('File upload timed out'))
      })

      // Configure request
      xhr.open('POST', url)
      xhr.setRequestHeader('Authorization', authHeader)
      xhr.timeout = this.timeout

      // Send request
      xhr.send(formData)
    })
  }

  /**
   * Extract filename from response headers or URL
   */
  getFilenameFromResponse(response) {
    const contentDisposition = response.headers.get('content-disposition')
    if (contentDisposition) {
      // First try to extract filename* with UTF-8 encoding
      const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/)
      if (utf8Match) {
        try {
          return decodeURIComponent(utf8Match[1])
        } catch (e) {
          console.warn('Failed to decode UTF-8 filename:', utf8Match[1])
        }
      }
      
      // Fallback to regular filename extraction
      const match = contentDisposition.match(/filename="?([^"]+)"?/)
      if (match) {
        try {
          // Try to decode if it looks like it might be encoded
          return decodeURIComponent(match[1])
        } catch (e) {
          // If decoding fails, return as-is
          return match[1]
        }
      }
    }
    
    // Fallback to extracting from URL path parameter
    const url = response.url
    const pathMatch = url.match(/[?&]path=([^&]+)/)
    if (pathMatch) {
      try {
        const decodedPath = decodeURIComponent(pathMatch[1])
        const filename = decodedPath.split('/').pop()
        if (filename) return filename
      } catch (e) {
        console.warn('Failed to decode filename from URL path:', pathMatch[1])
      }
    }
    
    // Final fallback
    return 'download'
  }

  /**
   * Health check endpoint
   */
  async isServerHealthy() {
    try {
      await this.request('health', {}, false)
      return true
    } catch {
      return false
    }
  }
}

// Custom error classes
class ApiError extends Error {
  constructor(message, status = 0, data = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.data = data
  }
}

class AuthenticationError extends Error {
  constructor(message) {
    super(message)
    this.name = 'AuthenticationError'
  }
}

class NetworkError extends Error {
  constructor(message) {
    super(message)
    this.name = 'NetworkError'
  }
}

class TimeoutError extends Error {
  constructor(message) {
    super(message)
    this.name = 'TimeoutError'
  }
}

// Export singleton instance and error classes
const apiClient = new ApiClient()

export default apiClient
export { ApiError, AuthenticationError, NetworkError, TimeoutError }