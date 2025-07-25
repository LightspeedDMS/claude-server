import { JobService, JobMonitor } from '../services/jobs.js';

/**
 * Job Monitoring Component - displays job status and results
 */
export class JobMonitorComponent {
  constructor(container, jobId = null) {
    this.container = container;
    this.jobId = jobId;
    this.jobService = new JobService();
    this.monitor = null;
    this.job = null;
    
    this.init();
  }

  async init() {
    if (this.jobId) {
      await this.loadJob();
      this.startMonitoring();
    } else {
      await this.loadAllJobs();
    }
    this.render();
  }

  async loadJob() {
    try {
      this.job = await this.jobService.getJob(this.jobId);
    } catch (error) {
      console.error('Failed to load job:', error);
      this.showError('Failed to load job details.');
    }
  }

  async loadAllJobs() {
    try {
      this.jobs = await this.jobService.getJobs();
    } catch (error) {
      console.error('Failed to load jobs:', error);
      this.showError('Failed to load jobs list.');
    }
  }

  startMonitoring() {
    if (!this.jobId || !this.job) return;

    // Don't monitor completed jobs
    const finalStates = ['completed', 'failed', 'timeout', 'cancelled'];
    if (finalStates.includes(this.job.status)) return;

    this.monitor = new JobMonitor(this.jobId, (updatedJob) => {
      this.job = updatedJob;
      this.updateJobDisplay();
    });

    this.monitor.startPolling();
  }

  stopMonitoring() {
    if (this.monitor) {
      this.monitor.stopPolling();
      this.monitor = null;
    }
  }

  render() {
    if (this.jobId && this.job) {
      this.renderJobDetails();
    } else if (this.jobs) {
      this.renderJobsList();
    } else {
      this.renderLoading();
    }
  }

  renderJobDetails() {
    const html = `
      <div class="job-monitor" data-testid="job-monitor">
        <div class="job-header" data-testid="job-header">
          <div class="flex justify-between items-center mb-4">
            <div>
              <h1 class="job-title" data-testid="job-title">${this.job.title || 'Untitled Job'}</h1>
              <div class="job-meta" data-testid="job-meta">
                <span data-testid="job-id">Job ID: ${this.job.jobId}</span>
                <span class="status-badge status-${this.job.status}" data-testid="status-badge">
                  ${this.job.status.replace('_', ' ')}
                </span>
                <span data-testid="job-status" class="hidden">${this.job.status}</span>
                ${this.job.queuePosition > 0 ? `<span>Queue position: ${this.job.queuePosition}</span>` : ''}
                ${this.job.createdAt ? `<span>Created: ${new Date(this.job.createdAt).toLocaleString()}</span>` : ''}
                ${this.job.completedAt ? `<span data-testid="completion-time">Completed: ${new Date(this.job.completedAt).toLocaleString()}</span>` : ''}
                ${this.job.exitCode !== null && this.job.exitCode !== undefined ? `<span data-testid="exit-code">Exit code: ${this.job.exitCode}</span>` : ''}
              </div>
            </div>
            <div class="flex gap-2">
              ${this.job.status === 'running' || this.job.status === 'queued' ? `
                <button 
                  class="btn btn-danger btn-sm" 
                  onclick="jobMonitorComponent.cancelJob()"
                  data-testid="cancel-job-button"
                >
                  Cancel Job
                </button>
              ` : ''}
              ${['completed', 'failed', 'cancelled'].includes(this.job.status) ? `
                <button 
                  class="btn btn-secondary btn-sm" 
                  onclick="jobMonitorComponent.browseFiles()"
                  data-testid="browse-files"
                >
                  Browse Files
                </button>
                <button 
                  class="btn btn-danger btn-sm" 
                  onclick="jobMonitorComponent.deleteJob()"
                  data-testid="delete-job-${this.job.jobId}"
                >
                  Delete Job
                </button>
              ` : ''}
            </div>
          </div>
        </div>

        <div class="job-output" data-testid="job-output">
          <div class="output-header">
            Output ${this.job.status === 'running' ? '(Live)' : ''}
          </div>
          <div 
            class="output-content" 
            id="job-output-content"
          >${this.job.output || (this.job.status === 'created' ? 'Job created. Waiting to start...' : this.job.status === 'queued' ? 'Job queued. Waiting for execution...' : 'No output yet...')}</div>
        </div>

        <!-- Error/Success Messages -->
        <div class="hidden" id="message-container">
          <div class="card mt-4" id="message-card">
            <div class="card-body">
              <p id="message-text"></p>
            </div>
          </div>
        </div>

        <!-- Cancel Confirmation Modal -->
        <div class="hidden" id="cancel-modal" style="position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;">
          <div class="card" style="max-width: 400px; width: 90%;">
            <div class="card-body">
              <h3>Cancel Job</h3>
              <p>Are you sure you want to cancel this job? This action cannot be undone.</p>
              <div class="flex gap-2 justify-end mt-4">
                <button class="btn btn-secondary" onclick="jobMonitorComponent.hideCancelModal()">
                  Cancel
                </button>
                <button class="btn btn-danger" onclick="jobMonitorComponent.confirmCancel()" data-testid="confirm-cancel">
                  Yes, Cancel Job
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    `;

    this.container.innerHTML = html;
  }

  renderJobsList() {
    const html = `
      <div class="container">
        <div class="flex justify-between items-center mb-4" data-testid="dashboard">
          <h1>Your Jobs</h1>
          <a href="/create" class="btn btn-primary" data-testid="create-job-button">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            Create New Job
          </a>
        </div>

        <div class="card">
          <div class="card-body">
            ${this.jobs.length === 0 ? `
              <div class="text-center p-4">
                <p class="text-muted">No jobs yet. Create your first job to get started!</p>
                <a href="/create" class="btn btn-primary mt-2">Create Job</a>
              </div>
            ` : `
              <div data-testid="job-list">
                ${this.jobs.map(job => `
                  <div class="flex justify-between items-center p-3 border-bottom">
                    <div>
                      <h4>
                        <a href="/jobs/${job.jobId}" style="text-decoration: none; color: var(--color-text);">
                          ${job.title || 'Untitled Job'}
                        </a>
                      </h4>
                      <p class="text-muted text-sm">
                        Repository: ${job.repository} • 
                        Created: ${new Date(job.started).toLocaleString()}
                      </p>
                    </div>
                    <div class="flex items-center gap-2">
                      <span class="status-badge status-${job.status}">
                        ${job.status.replace('_', ' ')}
                      </span>
                    </div>
                  </div>
                `).join('')}
              </div>
            `}
          </div>
        </div>
      </div>
    `;

    this.container.innerHTML = html;
  }

  renderLoading() {
    this.container.innerHTML = `
      <div class="container text-center" style="margin-top: 4rem;">
        <div class="spinner" style="width: 2rem; height: 2rem; margin: 0 auto 1rem;"></div>
        <p>Loading...</p>
      </div>
    `;
  }

  updateJobDisplay() {
    if (!this.job) return;

    // Update status badge
    const statusBadge = document.querySelector('[data-testid="status-badge"]');
    if (statusBadge) {
      statusBadge.className = `status-badge status-${this.job.status}`;
      statusBadge.textContent = this.job.status.replace('_', ' ');
    }

    // Update hidden status for testing
    const hiddenStatus = document.querySelector('[data-testid="job-status"]');
    if (hiddenStatus) {
      hiddenStatus.textContent = this.job.status;
    }

    // Update output
    const outputContent = document.getElementById('job-output-content');
    if (outputContent) {
      outputContent.textContent = this.job.output || 
        (this.job.status === 'created' ? 'Job created. Waiting to start...' : 
         this.job.status === 'queued' ? 'Job queued. Waiting for execution...' : 
         'No output yet...');
      
      // Auto-scroll to bottom for new content
      outputContent.scrollTop = outputContent.scrollHeight;
    }

    // Update metadata
    const jobMeta = document.querySelector('[data-testid="job-meta"]');
    if (jobMeta && this.job.completedAt) {
      // Re-render metadata to include completion time
      this.render();
    }
  }

  async cancelJob() {
    document.getElementById('cancel-modal').classList.remove('hidden');
  }

  hideCancelModal() {
    document.getElementById('cancel-modal').classList.add('hidden');
  }

  async confirmCancel() {
    try {
      await this.jobService.cancelJob(this.jobId);
      this.hideCancelModal();
      this.showMessage('Job cancellation requested.', 'success');
      
      // Refresh job status
      setTimeout(() => this.loadJob(), 1000);
      
    } catch (error) {
      console.error('Failed to cancel job:', error);
      this.showError(error.message || 'Failed to cancel job.');
      this.hideCancelModal();
    }
  }

  async deleteJob() {
    if (!confirm('Are you sure you want to delete this job? This will remove all job data and cannot be undone.')) {
      return;
    }

    try {
      await this.jobService.deleteJob(this.jobId);
      this.showMessage('Job deleted successfully.', 'success');
      
      // Navigate back to jobs list
      setTimeout(() => {
        window.location.href = '/jobs';
      }, 1500);
      
    } catch (error) {
      console.error('Failed to delete job:', error);
      this.showError(error.message || 'Failed to delete job.');
    }
  }

  browseFiles() {
    // Navigate to file browser
    window.location.href = `/jobs/${this.jobId}/files`;
  }

  showMessage(message, type = 'info') {
    const container = document.getElementById('message-container');
    const card = document.getElementById('message-card');
    const text = document.getElementById('message-text');
    
    text.textContent = message;
    
    if (type === 'success') {
      card.style.borderColor = 'var(--color-success)';
      card.style.backgroundColor = 'rgb(5 150 105 / 0.05)';
    } else if (type === 'error') {
      card.style.borderColor = 'var(--color-error)';
      card.style.backgroundColor = 'rgb(239 68 68 / 0.05)';
    }
    
    container.classList.remove('hidden');
    
    // Auto-hide after 3 seconds for success messages
    if (type === 'success') {
      setTimeout(() => {
        container.classList.add('hidden');
      }, 3000);
    }
  }

  showError(message) {
    this.showMessage(message, 'error');
  }

  destroy() {
    this.stopMonitoring();
  }
}

// Global reference for event handlers
window.jobMonitorComponent = null;