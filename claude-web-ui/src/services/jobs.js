import { api } from './api.js';

/**
 * Job management service
 */
export class JobService {
  /**
   * Get list of user's jobs
   */
  async getJobs() {
    return await api.request('/jobs');
  }

  /**
   * Get specific job status and details
   */
  async getJob(jobId) {
    return await api.request(`/jobs/${jobId}`);
  }

  /**
   * Create a new job
   */
  async createJob(jobData) {
    return await api.request('/jobs', {
      method: 'POST',
      body: JSON.stringify({
        prompt: jobData.prompt,
        repository: jobData.repository,
        images: [], // Deprecated - all files handled via file upload
        options: {
          timeout: jobData.timeout || 300,
          gitAware: jobData.gitAware !== false,
          cidxAware: jobData.cidxAware !== false,
        },
      }),
    });
  }

  /**
   * Upload file to job
   */
  async uploadFile(jobId, file, onProgress = null) {
    return new Promise((resolve, reject) => {
      const formData = new FormData();
      formData.append('file', file);

      const xhr = new XMLHttpRequest();

      // Track upload progress
      if (onProgress) {
        xhr.upload.addEventListener('progress', (e) => {
          if (e.lengthComputable) {
            const percentComplete = Math.round((e.loaded / e.total) * 100);
            onProgress(percentComplete);
          }
        });
      }

      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const response = JSON.parse(xhr.responseText);
            resolve(response);
          } catch (error) {
            reject(new Error('Invalid response format'));
          }
        } else {
          reject(new Error(`Upload failed: ${xhr.status} ${xhr.statusText}`));
        }
      };

      xhr.onerror = () => reject(new Error('Upload failed: Network error'));

      // Set authorization header
      const token = api.getToken();
      if (token) {
        xhr.setRequestHeader('Authorization', `Bearer ${token}`);
      }

      xhr.open('POST', `${api.baseURL}/jobs/${jobId}/files`);
      xhr.send(formData);
    });
  }

  /**
   * Start job execution
   */
  async startJob(jobId) {
    return await api.request(`/jobs/${jobId}/start`, {
      method: 'POST',
    });
  }

  /**
   * Cancel running job
   */
  async cancelJob(jobId) {
    return await api.request(`/jobs/${jobId}/cancel`, {
      method: 'POST',
    });
  }

  /**
   * Delete job and cleanup workspace
   */
  async deleteJob(jobId) {
    return await api.request(`/jobs/${jobId}`, {
      method: 'DELETE',
    });
  }

  /**
   * Get job workspace directories
   */
  async getJobDirectories(jobId, path = '') {
    const params = path ? `?path=${encodeURIComponent(path)}` : '';
    return await api.request(`/jobs/${jobId}/files/directories${params}`);
  }

  /**
   * Get job workspace files
   */
  async getJobFiles(jobId, path = '', mask = null) {
    const params = new URLSearchParams();
    params.append('path', path);
    if (mask) params.append('mask', mask);
    return await api.request(`/jobs/${jobId}/files/files?${params}`);
  }

  /**
   * Download file from job workspace
   */
  async downloadFile(jobId, filePath) {
    const response = await api.request(`/jobs/${jobId}/files/download?path=${encodeURIComponent(filePath)}`, {
      headers: {}, // Remove Content-Type for file download
    });
    return response;
  }

  /**
   * Get file content as text
   */
  async getFileContent(jobId, filePath) {
    return await api.request(`/jobs/${jobId}/files/content?path=${encodeURIComponent(filePath)}`);
  }
}

/**
 * Job status monitor with polling
 */
export class JobMonitor {
  constructor(jobId, onStatusUpdate = null) {
    this.jobId = jobId;
    this.onStatusUpdate = onStatusUpdate;
    this.polling = false;
    this.pollInterval = null;
  }

  /**
   * Start polling for job status updates
   */
  startPolling(interval = 2000) {
    if (this.polling) return;

    this.polling = true;
    
    const poll = async () => {
      if (!this.polling) return;

      try {
        const jobService = new JobService();
        const status = await jobService.getJob(this.jobId);
        
        if (this.onStatusUpdate) {
          this.onStatusUpdate(status);
        }

        // Stop polling if job is in final state
        const finalStates = ['completed', 'failed', 'timeout', 'cancelled'];
        if (finalStates.includes(status.status)) {
          this.stopPolling();
          return;
        }

        // Continue polling
        this.pollInterval = setTimeout(poll, interval);
      } catch (error) {
        console.error('Job status polling error:', error);
        // Retry with backoff on error
        this.pollInterval = setTimeout(poll, interval * 2);
      }
    };

    // Start immediately
    poll();
  }

  /**
   * Stop polling
   */
  stopPolling() {
    this.polling = false;
    if (this.pollInterval) {
      clearTimeout(this.pollInterval);
      this.pollInterval = null;
    }
  }
}

// Create singleton instance
export const jobService = new JobService();