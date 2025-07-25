/**
 * Claude Batch Server API Service
 * High-level API methods for all Claude Batch Server endpoints
 * Compatible with job title generation and all CRUD operations
 */

import ApiClient from './api.js';
import AuthService from './auth.js';

class ClaudeApiService {
  constructor() {
    this.api = ApiClient;
  }

  // ==================== AUTHENTICATION ====================

  /**
   * Login with credentials
   * @param {string} username - Username
   * @param {string} password - Password
   * @returns {Promise<Object>} Login response with token
   */
  async login(username, password) {
    const response = await this.api.post('auth/login', {
      username,
      password
    }, {}, false); // No auth required for login

    if (response && response.token) {
      AuthService.setToken(response.token, response.user, response.expires);
    }

    return response;
  }

  /**
   * Logout current user
   * @returns {Promise<Object>} Logout response
   */
  async logout() {
    try {
      const response = await this.api.post('auth/logout', {});
      AuthService.clearSession();
      return response;
    } catch (error) {
      // Clear session even if server logout fails
      AuthService.clearSession();
      throw error;
    }
  }

  // ==================== REPOSITORY MANAGEMENT ====================

  /**
   * Get list of all registered repositories
   * @returns {Promise<Array>} List of repositories with metadata
   */
  async getRepositories() {
    return await this.api.getRepositories();
  }

  /**
   * Get details of a specific repository
   * @param {string} repoName - Repository name
   * @returns {Promise<Object>} Repository details
   */
  async getRepository(repoName) {
    return await this.api.getRepository(repoName);
  }

  /**
   * Register a new repository
   * @param {Object} repoData - Repository registration data
   * @param {string} repoData.name - Repository name
   * @param {string} repoData.gitUrl - Git repository URL
   * @param {string} [repoData.description] - Repository description
   * @returns {Promise<Object>} Registration response
   */
  async registerRepository(repoData) {
    return await this.api.createRepository(repoData);
  }

  /**
   * Unregister a repository
   * @param {string} repoName - Repository name to unregister
   * @returns {Promise<Object>} Unregister response
   */
  async unregisterRepository(repoName) {
    return await this.api.deleteRepository(repoName);
  }

  /**
   * Get files in a repository
   * @param {string} repoName - Repository name
   * @param {string} [path] - Specific path within repository
   * @returns {Promise<Array>} List of files
   */
  async getRepositoryFiles(repoName, path = null) {
    let endpoint = `repositories/${encodeURIComponent(repoName)}/files`;
    if (path) {
      endpoint += `?path=${encodeURIComponent(path)}`;
    }
    return await this.api.get(endpoint);
  }

  /**
   * Get content of a specific file in repository
   * @param {string} repoName - Repository name
   * @param {string} filePath - Path to file
   * @returns {Promise<Object>} File content response
   */
  async getRepositoryFileContent(repoName, filePath) {
    return await this.api.get(
      `repositories/${encodeURIComponent(repoName)}/files/${encodeURIComponent(filePath)}/content`
    );
  }

  // ==================== JOB MANAGEMENT ====================

  /**
   * Create a new job with auto-generated title
   * @param {Object} jobData - Job creation data
   * @param {string} jobData.prompt - Job prompt/description
   * @param {string} jobData.repository - Repository name
   * @param {Array} [jobData.images] - Array of image paths
   * @param {Object} [jobData.options] - Job options (timeout, gitAware, cidxAware)
   * @returns {Promise<Object>} Job creation response with title
   */
  async createJob(jobData) {
    const payload = {
      prompt: jobData.prompt,
      repository: jobData.repository,
      images: [], // Deprecated - all files handled via file upload
      options: {
        timeout: jobData.options?.timeout || 300,
        gitAware: jobData.options?.gitAware ?? true,
        cidxAware: jobData.options?.cidxAware ?? true,
        ...jobData.options
      }
    };

    return await this.api.post('jobs', payload);
  }

  /**
   * Get list of jobs with filtering
   * @param {Object} [filter] - Job filter options
   * @param {string} [filter.repository] - Filter by repository
   * @param {string} [filter.status] - Filter by status
   * @param {string} [filter.user] - Filter by user
   * @param {Date} [filter.createdAfter] - Filter by creation date
   * @param {Date} [filter.createdBefore] - Filter by creation date
   * @param {number} [filter.limit] - Limit number of results
   * @param {number} [filter.skip] - Skip number of results
   * @returns {Promise<Array>} List of jobs
   */
  async getJobs(filter = {}) {
    return await this.api.getJobs(filter);
  }

  /**
   * Get detailed job status and results
   * @param {string} jobId - Job ID
   * @returns {Promise<Object>} Job status with output and metadata
   */
  async getJobStatus(jobId) {
    return await this.api.getJob(jobId);
  }

  /**
   * Start job execution
   * @param {string} jobId - Job ID to start
   * @returns {Promise<Object>} Start response with queue position
   */
  async startJob(jobId) {
    return await this.api.post(`jobs/${jobId}/start`, {});
  }

  /**
   * Cancel running job
   * @param {string} jobId - Job ID to cancel
   * @returns {Promise<Object>} Cancel response
   */
  async cancelJob(jobId) {
    return await this.api.post(`jobs/${jobId}/cancel`, {});
  }

  /**
   * Delete job and cleanup workspace
   * @param {string} jobId - Job ID to delete
   * @returns {Promise<Object>} Delete response
   */
  async deleteJob(jobId) {
    return await this.api.delete(`jobs/${jobId}`);
  }

  // ==================== FILE OPERATIONS ====================

  /**
   * Upload files to job (fixed implementation)
   */
  async uploadJobFiles(jobId, files, onProgress = null, overwrite = false) {
    // CRITICAL FIX: Server expects single file per request, so upload files individually
    const results = [];
    let totalSize = files.reduce((sum, file) => sum + file.size, 0);
    let uploadedSize = 0;
    
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const formData = new FormData();
      formData.append('file', file, file.name); // FIXED: Use 'file' (singular)
      
      if (overwrite) {
        formData.append('overwrite', 'true');
      }
      
      try {
        // Calculate individual file progress within overall progress
        const fileProgress = onProgress ? (progress) => {
          const overallProgress = Math.round(((uploadedSize + (file.size * progress / 100)) / totalSize) * 100);
          onProgress(overallProgress);
        } : null;
        
        const result = await this.api.uploadWithProgress(
          `jobs/${jobId}/files`,
          formData,
          fileProgress
        );
        
        results.push(result);
        uploadedSize += file.size;
        
        // Update overall progress after file completion
        if (onProgress) {
          const overallProgress = Math.round((uploadedSize / totalSize) * 100);
          onProgress(overallProgress);
        }
      } catch (error) {
        console.error(`Failed to upload file ${file.name}:`, error);
        throw new Error(`Failed to upload file ${file.name}: ${error.message}`);
      }
    }
    
    return results;
  }

  // uploadJobImages method removed - all files use uploadJobFiles

  /**
   * Get directories in job workspace
   * @param {string} jobId - Job ID
   * @param {string} path - Directory path
   * @returns {Promise<Array>} List of directories
   */
  async getJobDirectories(jobId, path = '') {
    const params = path ? `?path=${encodeURIComponent(path)}` : '';
    return await this.api.get(`jobs/${jobId}/files/directories${params}`);
  }

  /**
   * Get files in job workspace directory
   * @param {string} jobId - Job ID
   * @param {string} path - Directory path
   * @param {string} mask - File mask filter
   * @returns {Promise<Array>} List of files
   */
  async getJobFiles(jobId, path = '', mask = null) {
    const params = new URLSearchParams();
    params.append('path', path);
    if (mask) params.append('mask', mask);
    return await this.api.get(`jobs/${jobId}/files/files?${params}`);
  }

  /**
   * Download a specific file from job workspace
   * @param {string} jobId - Job ID
   * @param {string} fileName - File name to download
   * @returns {Promise<Blob>} File blob for download
   */
  async downloadJobFile(jobId, fileName) {
    return await this.api.downloadFile(`jobs/${jobId}/files/${encodeURIComponent(fileName)}`);
  }

  /**
   * Get file content as text
   * @param {string} jobId - Job ID
   * @param {string} fileName - File name to read
   * @returns {Promise<Object>} File content response
   */
  async getJobFileContent(jobId, fileName) {
    return await this.api.get(`jobs/${jobId}/files/${encodeURIComponent(fileName)}/content`);
  }

  // ==================== JOB MONITORING ====================

  /**
   * Poll job status until completion or failure
   * @param {string} jobId - Job ID to monitor
   * @param {Function} onStatusChange - Callback for status updates
   * @param {number} [interval] - Polling interval in milliseconds (default: 2000)
   * @param {number} [timeout] - Maximum polling time in milliseconds (default: 600000)
   * @returns {Promise<Object>} Final job status
   */
  async monitorJob(jobId, onStatusChange, interval = 2000, timeout = 600000) {
    const startTime = Date.now();
    let polling = true;

    const poll = async () => {
      try {
        const status = await this.getJobStatus(jobId);
        onStatusChange(status);

        // Check if job is in final state
        const finalStates = ['completed', 'failed', 'timeout', 'cancelled'];
        if (finalStates.includes(status.status)) {
          polling = false;
          return status;
        }

        // Check for timeout
        if (Date.now() - startTime > timeout) {
          polling = false;
          throw new Error(`Job monitoring timed out after ${timeout}ms`);
        }

        // Continue polling if still running
        if (polling) {
          setTimeout(poll, interval);
        }
      } catch (error) {
        polling = false;
        throw error;
      }
    };

    // Start polling
    return new Promise((resolve, reject) => {
      poll().then(resolve).catch(reject);
    });
  }

  /**
   * Create and start a job in one operation
   * @param {Object} jobData - Job creation data (same as createJob)
   * @returns {Promise<Object>} Started job response
   */
  async createAndStartJob(jobData) {
    const createResponse = await this.createJob(jobData);
    const startResponse = await this.startJob(createResponse.jobId);
    
    return {
      ...createResponse,
      ...startResponse,
      created: true,
      started: true
    };
  }

  // ==================== UTILITY METHODS ====================

  /**
   * Check server health
   * @returns {Promise<boolean>} Server health status
   */
  async isServerHealthy() {
    return await this.api.isServerHealthy();
  }

  /**
   * Get current user info (if authenticated)
   * @returns {Object} User session info
   */
  getCurrentUser() {
    return AuthService.getSessionInfo();
  }

  /**
   * Check if user is authenticated
   * @returns {boolean} Authentication status
   */
  isAuthenticated() {
    return AuthService.isAuthenticated();
  }

  /**
   * Handle file download with proper naming
   * @param {Blob} blob - File blob
   * @param {string} fileName - Suggested file name
   */
  downloadBlob(blob, fileName) {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.style.display = 'none';
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  }

  /**
   * Format job status for display
   * @param {string} status - Raw job status
   * @returns {string} Formatted status
   */
  formatJobStatus(status) {
    const statusMap = {
      'created': 'Created',
      'queued': 'Queued',
      'git_pulling': 'Pulling Git Repository',
      'cidx_indexing': 'Indexing with Cidx',
      'cidx_ready': 'Cidx Ready',
      'running': 'Running',
      'completed': 'Completed',
      'failed': 'Failed',
      'timeout': 'Timed Out',
      'cancelled': 'Cancelled'
    };

    return statusMap[status] || status.charAt(0).toUpperCase() + status.slice(1);
  }

  /**
   * Get status badge class for UI styling
   * @param {string} status - Job status
   * @returns {string} CSS class name
   */
  getStatusBadgeClass(status) {
    const classMap = {
      'created': 'status-created',
      'queued': 'status-queued',
      'git_pulling': 'status-git-pulling',
      'cidx_indexing': 'status-cidx-indexing',
      'cidx_ready': 'status-cidx-ready',
      'running': 'status-running',
      'completed': 'status-completed',
      'failed': 'status-failed',
      'timeout': 'status-timeout',
      'cancelled': 'status-cancelled'
    };

    return classMap[status] || 'status-unknown';
  }

  /**
   * Check if job is in a final state
   * @param {string} status - Job status
   * @returns {boolean} True if job is finished
   */
  isJobFinished(status) {
    const finalStates = ['completed', 'failed', 'timeout', 'cancelled'];
    return finalStates.includes(status);
  }

  /**
   * Calculate job duration
   * @param {string} createdAt - Job creation time ISO string
   * @param {string} [completedAt] - Job completion time ISO string
   * @returns {string} Human-readable duration
   */
  calculateJobDuration(createdAt, completedAt = null) {
    const start = new Date(createdAt);
    const end = completedAt ? new Date(completedAt) : new Date();
    const diff = end - start;

    const hours = Math.floor(diff / (1000 * 60 * 60));
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
    const seconds = Math.floor((diff % (1000 * 60)) / 1000);

    if (hours > 0) {
      return `${hours}h ${minutes}m ${seconds}s`;
    } else if (minutes > 0) {
      return `${minutes}m ${seconds}s`;
    } else {
      return `${seconds}s`;
    }
  }
}

// Export singleton instance
export default new ClaudeApiService();