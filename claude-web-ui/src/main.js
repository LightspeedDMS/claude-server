/**
 * Claude Web UI - Main Application
 * Demonstrates the API client and authentication services integration
 */

import AuthService from './services/auth.js';
import ClaudeApi from './services/claude-api.js';
import { MarkdownParser } from './utils/markdown.js';

class ClaudeWebUI {
  constructor() {
    this.currentView = 'jobs';
    this.uploadedFiles = [];
    this.jobPollingIntervals = new Map();
    this.repositoryPollingInterval = null;
    this.cachedRepositories = new Map(); // Cache for smart updates
    this.cachedJobs = []; // Cache for job data to support output expansion
    this.repositoryPollStartTime = null; // Track when polling started
    this.maxPollingDuration = 10 * 60 * 1000; // 10 minutes max polling
    
    this.initializeApp();
  }

  async initializeApp() {
    console.log('Initializing Claude Web UI...');
    
    // Check authentication status
    if (await AuthService.isAuthenticated()) {
      console.log('User is authenticated, loading dashboard...');
      await this.showDashboard();
    } else {
      console.log('User not authenticated, showing login...');
      this.showLogin();
    }

    this.setupEventListeners();
  }

  setupEventListeners() {
    // Login form
    const loginForm = document.getElementById('login-form');
    if (loginForm) {
      loginForm.addEventListener('submit', this.handleLogin.bind(this));
    }

    // Logout button
    const logoutButton = document.getElementById('logout-button');
    if (logoutButton) {
      logoutButton.addEventListener('click', this.handleLogout.bind(this));
    }

    // Navigation
    document.getElementById('nav-jobs')?.addEventListener('click', () => this.showView('jobs'));
    document.getElementById('nav-repositories')?.addEventListener('click', () => this.showView('repositories'));

    // Action buttons
    document.getElementById('create-job-button')?.addEventListener('click', () => this.showView('create-job'));
    document.getElementById('refresh-jobs')?.addEventListener('click', () => this.loadJobs());
    document.getElementById('refresh-repositories')?.addEventListener('click', () => this.loadRepositories());
    document.getElementById('register-repo-button')?.addEventListener('click', () => this.showRegisterRepository());

    // Create job form
    const createJobForm = document.getElementById('create-job-form');
    if (createJobForm) {
      createJobForm.addEventListener('submit', this.handleCreateJob.bind(this));
    }

    document.getElementById('cancel-job-creation')?.addEventListener('click', () => this.showView('jobs'));

    // File upload
    this.setupFileUpload();
  }

  setupRepositorySelectionHandler() {
    const repositorySelect = document.getElementById('job-repository');
    const cidxCheckbox = document.getElementById('cidx-aware');
    const cidxLabel = document.querySelector('label[for="cidx-aware"]');
    
    if (!repositorySelect || !cidxCheckbox) return;
    
    // Add change event listener to repository selection
    repositorySelect.addEventListener('change', (e) => {
      const selectedOption = e.target.selectedOptions[0];
      
      if (!selectedOption || !selectedOption.value) {
        // No repository selected - disable cidx option
        cidxCheckbox.disabled = true;
        cidxCheckbox.checked = false;
        if (cidxLabel) {
          cidxLabel.style.opacity = '0.6';
          cidxLabel.title = 'Select a repository first';
        }
        return;
      }
      
      const isCidxAware = selectedOption.dataset.cidxAware === 'true';
      const cloneStatus = selectedOption.dataset.cloneStatus;
      const isCompleted = cloneStatus === 'completed';
      
      if (isCidxAware && isCompleted) {
        // Repository is cidx-aware and ready - enable cidx option
        cidxCheckbox.disabled = false;
        if (cidxLabel) {
          cidxLabel.style.opacity = '1';
          cidxLabel.title = 'Enable cidx semantic search for this job';
        }
      } else {
        // Repository is not cidx-aware or not ready - disable cidx option
        cidxCheckbox.disabled = true;
        cidxCheckbox.checked = false;
        if (cidxLabel) {
          cidxLabel.style.opacity = '0.6';
          if (!isCidxAware) {
            cidxLabel.title = 'This repository was not registered with cidx-aware enabled';
          } else if (!isCompleted) {
            cidxLabel.title = `Repository status is '${cloneStatus}' - cidx not ready yet`;
          }
        }
      }
    });
    
    // Trigger initial state based on current selection
    if (repositorySelect.value) {
      repositorySelect.dispatchEvent(new Event('change'));
    }
  }

  setupFileUpload() {
    const fileUploadArea = document.getElementById('file-upload-area');
    const fileInput = document.getElementById('file-upload');

    if (!fileUploadArea || !fileInput) return;

    // Click to select files - CRITICAL FIX: Only trigger on the upload area itself, not child elements
    fileUploadArea.addEventListener('click', (e) => {
      // Don't trigger file dialog if clicking on remove buttons or other interactive elements
      if (e.target.classList.contains('remove-file-btn') || 
          e.target.closest('.remove-file-btn') ||
          e.target.closest('.uploaded-file')) {
        return;
      }
      fileInput.click();
    });

    // File selection
    fileInput.addEventListener('change', (e) => {
      this.handleFileSelection(Array.from(e.target.files));
    });

    // Drag and drop
    fileUploadArea.addEventListener('dragover', (e) => {
      e.preventDefault();
      fileUploadArea.classList.add('dragover');
    });

    fileUploadArea.addEventListener('dragleave', () => {
      fileUploadArea.classList.remove('dragover');
    });

    fileUploadArea.addEventListener('drop', (e) => {
      e.preventDefault();
      fileUploadArea.classList.remove('dragover');
      this.handleFileSelection(Array.from(e.dataTransfer.files));
    });
  }

  handleFileSelection(files) {
    this.uploadedFiles = [...this.uploadedFiles, ...files];
    this.displayUploadedFiles();
    this.showToast('Files selected successfully', 'success');
  }

  resetFileUploadState() {
    // Clear uploaded files array
    this.uploadedFiles = [];
    
    // Clear the file input
    const fileInput = document.getElementById('file-upload');
    if (fileInput) {
      fileInput.value = '';
    }
    
    // Clear any progress indicators or uploaded files display
    this.displayUploadedFiles();
    
    // Reset any progress indicators in the upload area
    const uploadProgress = document.getElementById('upload-progress');
    if (uploadProgress) {
      uploadProgress.style.display = 'none';
    }
    
    // Reset progress bar to 0%
    const progressFill = document.querySelector('[data-testid="upload-progress-bar"]');
    if (progressFill) {
      progressFill.style.width = '0%';
    }
    
    // Reset progress text
    const progressText = document.querySelector('.progress-text');
    if (progressText) {
      progressText.textContent = '0%';
    }
  }

  displayUploadedFiles() {
    const container = document.getElementById('uploaded-files');
    if (!container) return;

    container.innerHTML = '';
    
    this.uploadedFiles.forEach((file, index) => {
      const fileDiv = document.createElement('div');
      fileDiv.className = 'uploaded-file';
      fileDiv.innerHTML = `
        <div>
          <span class="file-name">${file.name}</span>
          <span class="file-size">(${this.formatFileSize(file.size)})</span>
        </div>
        <button type="button" class="btn btn-secondary remove-file-btn" data-file-index="${index}">
          Remove
        </button>
      `;
      
      // CRITICAL FIX: Add proper event listener to prevent bubbling to file upload area
      const removeButton = fileDiv.querySelector('.remove-file-btn');
      removeButton.addEventListener('click', (event) => {
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
        this.removeFile(index);
      });
      
      container.appendChild(fileDiv);
    });
  }

  removeFile(index) {
    this.uploadedFiles.splice(index, 1);
    this.displayUploadedFiles();
  }

  formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  formatClaudeOutput(output) {
    if (!output) return '';
    
    // Show first 500 characters initially for truncated view
    const truncated = output.length > 500 ? output.substring(0, 500) + '...' : output;
    
    // Parse markdown for proper formatting
    return MarkdownParser.parse(truncated);
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  toggleFullOutput(jobId) {
    const outputElement = document.querySelector(`[data-job-id="${jobId}"]`);
    const button = outputElement.parentElement.querySelector('.toggle-output');
    
    if (!outputElement || !button) return;

    if (button.textContent.includes('Show Full')) {
      // Show full output - fetch the full job data
      this.showFullClaudeOutput(jobId, outputElement, button);
    } else {
      // Show truncated output
      this.showTruncatedOutput(jobId, outputElement, button);
    }
  }

  async showFullClaudeOutput(jobId, outputElement, button) {
    try {
      button.textContent = 'Loading...';
      const jobData = await ClaudeApi.getJobStatus(jobId);
      
      if (jobData.output) {
        outputElement.innerHTML = MarkdownParser.parse(jobData.output);
        button.textContent = 'Show Less';
      }
    } catch (error) {
      console.error('Error loading full output:', error);
      button.textContent = 'Show Full Result';
    }
  }

  showTruncatedOutput(jobId, outputElement, button) {
    // We need to get the original job data to show truncated version
    const jobCards = document.querySelectorAll('.job-card');
    for (const card of jobCards) {
      const cardJobId = card.getAttribute('data-job-id');
      if (cardJobId === jobId) {
        // Find the job data from our cached jobs
        const job = this.cachedJobs?.find(j => j.jobId === jobId);
        if (job) {
          outputElement.innerHTML = MarkdownParser.parse(job.output);
          button.textContent = 'Show Full Result';
        }
        break;
      }
    }
  }

  getRepositoryStatus(repo) {
    if (repo.cloneStatus) {
      const status = repo.cloneStatus.toLowerCase().trim();
      switch (status) {
        case 'completed':
        case 'ready':
          return 'ready';
        case 'cloning':
        case 'git_pulling':
          return 'cloning';
        case 'cidx_indexing':
          return 'indexing';
        case 'failed':
        case 'git_failed':
        case 'cidx_failed':
          return 'failed';
        default:
          console.warn('Unknown repository status for', repo.name + ':', status);
          return 'unknown';
      }
    }
    
    // If no cloneStatus, assume it's ready (for existing repos)
    return 'ready';
  }

  formatStatus(status) {
    const statusMap = {
      'ready': 'Ready',
      'cloning': 'Cloning',
      'indexing': 'Indexing', 
      'failed': 'Failed',
      'unknown': 'Unknown'
    };
    
    return statusMap[status] || status;
  }

  async handleLogin(e) {
    e.preventDefault();
    
    const username = document.getElementById('username').value;
    const password = document.getElementById('password').value;
    const errorElement = document.getElementById('login-error');
    const loginButton = document.getElementById('login-button');

    // Clear previous errors
    errorElement.style.display = 'none';
    loginButton.disabled = true;
    loginButton.textContent = 'Signing In...';

    try {
      const result = await AuthService.login(username, password);
      const success = result.success;
      
      if (success) {
        console.log('Login successful');
        await this.showDashboard();
      } else {
        this.showError(errorElement, 'Invalid username or password');
      }
    } catch (error) {
      console.error('Login error:', error);
      this.showError(errorElement, 'Login failed. Please try again.');
    } finally {
      loginButton.disabled = false;
      loginButton.textContent = 'Sign In';
    }
  }

  async handleLogout() {
    try {
      await AuthService.logout();
      this.showLogin();
      this.showToast('Logged out successfully', 'success');
    } catch (error) {
      console.error('Logout error:', error);
      this.showToast('Logout failed', 'error');
    }
  }

  async handleCreateJob(e) {
    e.preventDefault();

    const formData = {
      repository: document.getElementById('job-repository').value,
      prompt: document.getElementById('job-prompt').value,
      options: {
        timeout: parseInt(document.getElementById('job-timeout').value),
        gitAware: document.getElementById('git-aware').checked,
        cidxAware: document.getElementById('cidx-aware').checked
      }
    };

    const submitButton = document.getElementById('submit-job');
    submitButton.disabled = true;
    submitButton.textContent = 'Creating Job...';

    try {
      console.log('Creating job with data:', formData);
      
      // Create job
      const jobResponse = await ClaudeApi.createJob(formData);
      console.log('Job created successfully:', jobResponse);

      // Upload files if any
      if (this.uploadedFiles.length > 0) {
        console.log('Uploading files:', this.uploadedFiles.length);
        const uploadProgress = document.getElementById('upload-progress');
        const progressBar = document.querySelector('.progress-fill');
        const progressText = document.querySelector('.progress-text');
        
        uploadProgress.style.display = 'block';

        // Use FIXED ClaudeApi.uploadJobFiles with correct field name
        await ClaudeApi.uploadJobFiles(
          jobResponse.jobId,
          this.uploadedFiles,
          (percent) => {
            progressBar.style.width = `${percent}%`;
            progressText.textContent = `${percent}%`;
          }
        );

        uploadProgress.style.display = 'none';
        console.log('Files uploaded successfully');
      }

      // Start the job
      console.log('Starting job:', jobResponse.jobId);
      await ClaudeApi.startJob(jobResponse.jobId);
      console.log('Job started successfully');

      // Clear form
      document.getElementById('create-job-form').reset();
      this.uploadedFiles = [];
      this.displayUploadedFiles();

      // Show success and navigate to jobs
      const jobTitle = jobResponse.title || jobResponse.jobId || 'Untitled Job';
      this.showToast(`Job "${jobTitle}" created successfully`, 'success');
      this.showView('jobs');
      
      // Refresh jobs list
      await this.loadJobs();

    } catch (error) {
      console.error('Create job error details:', {
        error: error,
        message: error.message,
        stack: error.stack,
        name: error.name,
        type: typeof error
      });
      
      const errorMessage = error.message || 'Unknown error occurred';
      this.showToast('Failed to create job: ' + errorMessage, 'error');
    } finally {
      submitButton.disabled = false;
      submitButton.textContent = 'Create Job';
    }
  }

  showLogin() {
    document.getElementById('login-container').style.display = 'block';
    document.getElementById('dashboard-container').style.display = 'none';
    
    // Clear any stored data and polling intervals
    this.jobPollingIntervals.forEach(interval => clearInterval(interval));
    this.jobPollingIntervals.clear();
    this.cachedRepositories.clear();
    this.stopRepositoryPolling();
  }

  async showDashboard() {
    document.getElementById('login-container').style.display = 'none';
    document.getElementById('dashboard-container').style.display = 'block';
    
    // Update user display
    const user = AuthService.getCurrentUser();
    document.getElementById('username-display').textContent = user?.username || 'User';

    // Load initial data
    await this.showView(this.currentView);
  }

  async showView(viewName) {
    this.currentView = viewName;

    // Update navigation
    document.querySelectorAll('.nav-item').forEach(item => item.classList.remove('active'));
    document.getElementById(`nav-${viewName}`)?.classList.add('active');

    // Update views
    document.querySelectorAll('.view').forEach(view => view.classList.remove('active'));
    document.getElementById(`${viewName}-view`)?.classList.add('active');

    // Stop repository polling when switching away from repositories view
    if (viewName !== 'repositories') {
      this.stopRepositoryPolling();
    } else {
      // Clear cache when entering repositories view to force fresh load
      this.cachedRepositories.clear();
    }

    // Load view-specific data
    switch (viewName) {
      case 'jobs':
        await this.loadJobs();
        break;
      case 'repositories':
        await this.loadRepositories();
        break;
      case 'create-job':
        await this.loadRepositoriesForJobCreation();
        break;
    }
  }

  async loadJobs() {
    const container = document.getElementById('jobs-list');
    container.innerHTML = '<div class="loading">Loading jobs...</div>';

    try {
      const jobs = await ClaudeApi.getJobs();
      
      // CRITICAL FIX: Fetch detailed job data for completed jobs to get output
      for (let i = 0; i < jobs.length; i++) {
        const job = jobs[i];
        if (job.status === 'completed' || job.status === 'failed') {
          try {
            const detailedJob = await ClaudeApi.getJobStatus(job.jobId);
            jobs[i] = { ...job, ...detailedJob };
          } catch (error) {
            console.error('Failed to load details for job', job.jobId, error);
          }
        }
      }
      
      this.cachedJobs = jobs; // Cache job data for output expansion
      this.displayJobs(jobs);
    } catch (error) {
      console.error('Load jobs error:', error);
      container.innerHTML = `<div class="error-message">Failed to load jobs: ${error.message}</div>`;
    }
  }

  displayJobs(jobs) {
    const container = document.getElementById('jobs-list');
    
    if (jobs.length === 0) {
      container.innerHTML = '<div class="loading">No jobs found. Create your first job!</div>';
      return;
    }

    container.innerHTML = '';
    
    jobs.forEach(job => {
      const jobCard = document.createElement('div');
      const statusClass = ClaudeApi.getStatusBadgeClass(job.status);
      const formattedStatus = ClaudeApi.formatJobStatus(job.status);
      const isFinished = ClaudeApi.isJobFinished(job.status);
      jobCard.className = `job-card ${statusClass}`;
      jobCard.setAttribute('data-job-id', job.jobId);
      
      jobCard.innerHTML = `
        <div class="job-card-content">
          <div class="job-header">
            <div class="job-title-section">
              <h3 class="job-title" data-testid="job-title">${job.title || 'Untitled Job'}</h3>
              <span class="status-badge ${statusClass}" data-testid="status-badge">${formattedStatus}</span>
            </div>
            ${(!isFinished && (job.status === 'running' || job.status === 'queued')) ? `
              <div class="progress-indicator">
                <div class="spinner"></div>
                <span class="progress-text">Processing...</span>
              </div>
            ` : ''}
          </div>

          <div class="job-details">
            <div class="detail-row">
              <span class="detail-label">Job ID:</span>
              <span class="detail-value" data-testid="job-id">${job.jobId}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Repository:</span>
              <span class="detail-value">${job.repository}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Created:</span>
              <span class="detail-value">${new Date(job.createdAt || job.started).toLocaleString()}</span>
            </div>
            ${job.startedAt ? `
              <div class="detail-row">
                <span class="detail-label">Started:</span>
                <span class="detail-value">${new Date(job.startedAt).toLocaleString()}</span>
              </div>
            ` : ''}
            ${job.completedAt ? `
              <div class="detail-row">
                <span class="detail-label">Completed:</span>
                <span class="detail-value">${new Date(job.completedAt).toLocaleString()}</span>
              </div>
            ` : ''}
            ${job.options ? `
              <div class="detail-row">
                <span class="detail-label">Git Aware:</span>
                <span class="detail-value">${job.options.gitAware ? '🌿 Yes' : '❌ No'}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">Cidx Aware:</span>
                <span class="detail-value">${job.options.cidxAware ? '🧠 Yes' : '❌ No'}</span>
              </div>
            ` : ''}
            ${job.prompt ? `
              <div class="detail-row">
                <span class="detail-label">Prompt:</span>
                <span class="detail-value job-prompt">${job.prompt.length > 100 ? job.prompt.substring(0, 100) + '...' : job.prompt}</span>
              </div>
            ` : ''}
            ${(job.output && job.status === 'completed') ? `
              <div class="detail-row">
                <span class="detail-label">Claude Code Result:</span>
                <div class="detail-value claude-result">
                  <div class="claude-output-content" data-job-id="${job.jobId}">
                    ${this.formatClaudeOutput(job.output)}
                  </div>
                  ${job.output.length > 500 ? `
                    <button class="btn btn-secondary btn-sm toggle-output" onclick="claudeUI.toggleFullOutput('${job.jobId}')">
                      Show Full Result
                    </button>
                  ` : ''}
                </div>
              </div>
            ` : ''}
            ${(job.error && job.status === 'failed') ? `
              <div class="detail-row">
                <span class="detail-label">Error:</span>
                <span class="detail-value job-error">${job.error}</span>
              </div>
            ` : ''}
          </div>

          <div class="job-actions">
            ${isFinished ? 
              `<button class="btn btn-primary" onclick="claudeUI.browseJobFiles('${job.jobId}')" data-testid="browse-job-files">
                📁 Browse Files
              </button>` : 
              `<button class="btn btn-danger" onclick="claudeUI.cancelJob('${job.jobId}')">Cancel</button>`
            }
            <button class="btn btn-danger" onclick="claudeUI.deleteJob('${job.jobId}')">
              Delete
            </button>
          </div>
        </div>
      `;
      
      container.appendChild(jobCard);

      // Start polling for running jobs
      if (!isFinished && !this.jobPollingIntervals.has(job.jobId)) {
        this.startJobPolling(job.jobId);
      }
    });
  }

  startJobPolling(jobId) {
    const interval = setInterval(async () => {
      try {
        const status = await ClaudeApi.getJobStatus(jobId);
        
        if (ClaudeApi.isJobFinished(status.status)) {
          clearInterval(interval);
          this.jobPollingIntervals.delete(jobId);
          await this.loadJobs(); // Refresh the jobs list
          
          // Show completion notification
          const message = status.status === 'completed' ? 
            'Job completed successfully' : 
            `Job ${status.status}`;
          this.showToast(message, status.status === 'completed' ? 'success' : 'warning');
        }
      } catch (error) {
        console.error('Job polling error:', error);
        // Continue polling despite errors
      }
    }, 2000); // Poll every 2 seconds

    this.jobPollingIntervals.set(jobId, interval);
  }


  async cancelJob(jobId) {
    if (!confirm('Are you sure you want to cancel this job?')) return;

    try {
      await ClaudeApi.cancelJob(jobId);
      this.showToast('Job cancelled successfully', 'success');
      await this.loadJobs();
    } catch (error) {
      console.error('Cancel job error:', error);
      this.showToast('Failed to cancel job', 'error');
    }
  }

  async deleteJob(jobId) {
    if (!confirm('Are you sure you want to delete this job? This action cannot be undone.')) return;

    try {
      await ClaudeApi.deleteJob(jobId);
      this.showToast('Job deleted successfully', 'success');
      
      // Clear polling if exists
      if (this.jobPollingIntervals.has(jobId)) {
        clearInterval(this.jobPollingIntervals.get(jobId));
        this.jobPollingIntervals.delete(jobId);
      }
      
      await this.loadJobs();
    } catch (error) {
      console.error('Delete job error:', error);
      this.showToast('Failed to delete job', 'error');
    }
  }

  async browseJobFiles(jobId) {
    try {
      // Create a simple job file browser modal
      this.showJobFileBrowser(jobId);
    } catch (error) {
      console.error('Browse job files error:', error);
      this.showToast('Failed to load job files', 'error');
    }
  }

  async showJobFileBrowser(jobId) {
    // Create modal for job file browsing
    const modalHtml = `
      <div class="modal-overlay" id="job-files-modal">
        <div class="modal-dialog modal-large">
          <div class="modal-header">
            <h2>Job Files - ${jobId}</h2>
            <button class="modal-close" id="job-files-close">&times;</button>
          </div>
          <div class="modal-body">
            <div id="job-files-content" class="job-files-container">
              <div class="loading-spinner"></div>
              <p>Loading job files...</p>
            </div>
          </div>
        </div>
      </div>
    `;

    document.body.insertAdjacentHTML('beforeend', modalHtml);
    
    const modal = document.getElementById('job-files-modal');
    const closeBtn = document.getElementById('job-files-close');
    
    // Close modal handlers
    const closeModal = () => modal.remove();
    closeBtn.addEventListener('click', closeModal);
    modal.addEventListener('click', (e) => {
      if (e.target === modal) closeModal();
    });

    // Initialize the proper file browser component
    try {
      const contentContainer = document.getElementById('job-files-content');
      
      // Import and initialize the file browser component
      const FileBrowserComponent = (await import('./components/file-browser.js')).default;
      this.fileBrowser = new FileBrowserComponent(contentContainer, jobId);
    } catch (error) {
      console.error('Failed to load file browser:', error);
      document.getElementById('job-files-content').innerHTML = `
        <div class="error-state">
          <h3>Error Loading Files</h3>
          <p>Failed to load file browser: ${error.message}</p>
        </div>
      `;
    }
  }


  getFileIcon(fileName) {
    const ext = fileName.split('.').pop()?.toLowerCase();
    const iconMap = {
      'js': '🟨', 'ts': '🔷', 'py': '🐍', 'java': '☕', 'cs': '🔷',
      'html': '🌐', 'css': '🎨', 'json': '📋', 'xml': '📋',
      'md': '📝', 'txt': '📄', 'pdf': '📕',
      'png': '🖼️', 'jpg': '🖼️', 'gif': '🖼️', 'svg': '🖼️',
      'zip': '📦', 'tar': '📦', 'gz': '📦'
    };
    return iconMap[ext] || '📄';
  }

  async viewJobFile(jobId, filePath) {
    try {
      const content = await ClaudeApi.getJobFileContent(jobId, filePath);
      
      // Create file viewer modal
      const viewerHtml = `
        <div class="modal-overlay" id="file-viewer-modal">
          <div class="modal-dialog modal-fullscreen">
            <div class="modal-header">
              <h2>File: ${filePath}</h2>
              <button class="modal-close" id="file-viewer-close">&times;</button>
            </div>
            <div class="modal-body">
              <div class="file-viewer-content">
                <pre class="file-content"><code>${this.escapeHtml(content)}</code></pre>
              </div>
            </div>
          </div>
        </div>
      `;

      document.body.insertAdjacentHTML('beforeend', viewerHtml);
      
      const modal = document.getElementById('file-viewer-modal');
      const closeBtn = document.getElementById('file-viewer-close');
      
      const closeModal = () => modal.remove();
      closeBtn.addEventListener('click', closeModal);
      modal.addEventListener('click', (e) => {
        if (e.target === modal) closeModal();
      });
      
    } catch (error) {
      console.error('Failed to view file:', error);
      this.showToast('Failed to load file content', 'error');
    }
  }

  async downloadJobFile(jobId, filePath) {
    try {
      const blob = await ClaudeApi.downloadJobFile(jobId, filePath);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filePath.split('/').pop();
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Failed to download file:', error);
      this.showToast('Failed to download file', 'error');
    }
  }

  async loadRepositories() {
    const container = document.getElementById('repositories-list');
    
    // Only show loading if container is empty or only has loading/error messages (initial load)
    const hasOnlyMessages = container.children.length === 0 || 
      (container.children.length === 1 && (
        container.querySelector('.loading') || 
        container.querySelector('.error-message')
      ));
    
    if (hasOnlyMessages) {
      container.innerHTML = '<div class="loading">Loading repositories...</div>';
    }

    try {
      const repositories = await ClaudeApi.getRepositories();
      this.displayRepositories(repositories);
    } catch (error) {
      console.error('Load repositories error:', error);
      container.innerHTML = '<div class="error-message">Failed to load repositories</div>';
    }
  }

  displayRepositories(repositories) {
    const container = document.getElementById('repositories-list');
    
    if (repositories.length === 0) {
      container.innerHTML = '<div class="loading">No repositories found. Register your first repository!</div>';
      this.cachedRepositories.clear();
      return;
    }

    // Remove loading message if it exists
    const loadingElement = container.querySelector('.loading');
    if (loadingElement) {
      loadingElement.remove();
    }

    // Create map of current repositories for comparison
    const currentRepos = new Map(repositories.map(repo => [repo.name, repo]));
    
    // Remove repositories that no longer exist
    this.cachedRepositories.forEach((cachedRepo, repoName) => {
      if (!currentRepos.has(repoName)) {
        const repoElement = container.querySelector(`[data-repo-name="${repoName}"]`);
        if (repoElement) {
          repoElement.remove();
        }
        this.cachedRepositories.delete(repoName);
      }
    });

    // Add or update repositories
    repositories.forEach(repo => {
      const cachedRepo = this.cachedRepositories.get(repo.name);
      let repoElement = container.querySelector(`[data-repo-name="${repo.name}"]`);
      
      if (!repoElement) {
        // Create new repository card
        console.log('Creating new repository card for:', repo.name);
        repoElement = this.createRepositoryCard(repo);
        container.appendChild(repoElement);
        this.cachedRepositories.set(repo.name, { ...repo });
      } else if (!cachedRepo || this.hasRepositoryChanged(cachedRepo, repo)) {
        // Update existing repository card only if data has changed
        console.log('Updating repository card for:', repo.name, 'hasCache:', !!cachedRepo);
        this.updateRepositoryCard(repoElement, repo, cachedRepo);
        this.cachedRepositories.set(repo.name, { ...repo });
      } else {
        console.log('No changes detected for repository:', repo.name);
      }
    });

    // Start or continue real-time polling for repositories that are processing
    this.startRepositoryPolling(repositories);
  }

  createRepositoryCard(repo) {
    const status = this.getRepositoryStatus(repo);
    const statusClass = `status-${status}`;
    const repoCard = document.createElement('div');
    repoCard.className = `repository-card ${statusClass}`;
    repoCard.setAttribute('data-repo-name', repo.name);
    
    repoCard.innerHTML = this.getRepositoryCardHTML(repo);
    return repoCard;
  }

  updateRepositoryCard(repoElement, newRepo, cachedRepo) {
    const newStatus = this.getRepositoryStatus(newRepo);
    const oldStatus = cachedRepo ? this.getRepositoryStatus(cachedRepo) : null;
    
    // Update status class if changed
    if (!cachedRepo || oldStatus !== newStatus) {
      repoElement.className = `repository-card status-${newStatus}`;
    }
    
    // Update status badge
    const statusBadge = repoElement.querySelector('[data-testid="repository-status"]');
    if (statusBadge) {
      const newStatusText = this.formatStatus(newStatus);
      if (statusBadge.textContent !== newStatusText) {
        statusBadge.textContent = newStatusText;
        statusBadge.className = `badge badge-${newStatus}`;
      }
    }
    
    // Update progress indicator
    const progressContainer = repoElement.querySelector('.repository-header');
    const existingProgress = progressContainer.querySelector('.progress-indicator');
    const shouldShowProgress = newStatus === 'cloning' || newStatus === 'indexing';
    
    if (shouldShowProgress && !existingProgress) {
      // Add progress indicator
      const progressHTML = `
        <div class="progress-indicator">
          <div class="spinner"></div>
          <span class="progress-text">${newStatus === 'cloning' ? 'Cloning repository...' : 'Building semantic index...'}</span>
        </div>
      `;
      progressContainer.insertAdjacentHTML('beforeend', progressHTML);
    } else if (!shouldShowProgress && existingProgress) {
      // Remove progress indicator
      existingProgress.remove();
    } else if (shouldShowProgress && existingProgress) {
      // Update progress text if status changed
      const progressText = existingProgress.querySelector('.progress-text');
      const newText = newStatus === 'cloning' ? 'Cloning repository...' : 'Building semantic index...';
      if (progressText && progressText.textContent !== newText) {
        progressText.textContent = newText;
      }
    }
    
    // Update dynamic fields that might change
    this.updateRepositoryField(repoElement, 'size', this.formatFileSize(newRepo.size), cachedRepo ? this.formatFileSize(cachedRepo.size) : null);
    this.updateRepositoryField(repoElement, 'last-updated', new Date(newRepo.lastModified).toLocaleString(), cachedRepo ? new Date(cachedRepo.lastModified).toLocaleString() : null);
    
    // Update git metadata if available
    if (newRepo.currentBranch !== (cachedRepo?.currentBranch)) {
      this.updateGitMetadata(repoElement, newRepo, cachedRepo);
    }
  }

  updateRepositoryField(repoElement, fieldClass, newValue, oldValue) {
    if (newValue !== oldValue) {
      const fieldElement = repoElement.querySelector(`[data-field="${fieldClass}"]`);
      if (fieldElement) {
        fieldElement.textContent = newValue;
      }
    }
  }

  updateGitMetadata(repoElement, newRepo, cachedRepo) {
    // Update branch info
    let branchRow = repoElement.querySelector('[data-field="branch-row"]');
    if (newRepo.currentBranch && !branchRow) {
      // Add branch row
      const detailsContainer = repoElement.querySelector('.repository-details');
      const branchHTML = `
        <div class="detail-row" data-field="branch-row">
          <span class="detail-label">Branch:</span>
          <span class="detail-value" data-field="branch">🌿 ${newRepo.currentBranch}</span>
        </div>
      `;
      detailsContainer.insertAdjacentHTML('beforeend', branchHTML);
    } else if (newRepo.currentBranch && branchRow) {
      // Update existing branch
      const branchValue = branchRow.querySelector('[data-field="branch"]');
      if (branchValue) {
        branchValue.textContent = `🌿 ${newRepo.currentBranch}`;
      }
    } else if (!newRepo.currentBranch && branchRow) {
      // Remove branch row
      branchRow.remove();
    }

    // Update commit info
    let commitRow = repoElement.querySelector('[data-field="commit-row"]');
    if (newRepo.commitHash && !commitRow) {
      // Add commit row
      const detailsContainer = repoElement.querySelector('.repository-details');
      const commitHTML = `
        <div class="detail-row" data-field="commit-row">
          <span class="detail-label">Latest Commit:</span>
          <span class="detail-value" data-field="commit">
            <code>${newRepo.commitHash.substring(0, 8)}</code>
            ${newRepo.commitMessage ? ` - ${newRepo.commitMessage}` : ''}
          </span>
        </div>
      `;
      detailsContainer.insertAdjacentHTML('beforeend', commitHTML);
    } else if (newRepo.commitHash && commitRow && 
               (newRepo.commitHash !== cachedRepo?.commitHash || newRepo.commitMessage !== cachedRepo?.commitMessage)) {
      // Update existing commit
      const commitValue = commitRow.querySelector('[data-field="commit"]');
      if (commitValue) {
        commitValue.innerHTML = `
          <code>${newRepo.commitHash.substring(0, 8)}</code>
          ${newRepo.commitMessage ? ` - ${newRepo.commitMessage}` : ''}
        `;
      }
    } else if (!newRepo.commitHash && commitRow) {
      // Remove commit row
      commitRow.remove();
    }
  }

  getRepositoryCardHTML(repo) {
    const status = this.getRepositoryStatus(repo);
    
    return `
      <div class="repository-card-content">
        <div class="repository-header">
          <div class="repository-title">
            <h3>${repo.name}</h3>
            <span class="badge badge-${status}" data-testid="repository-status">${this.formatStatus(status)}</span>
          </div>
          ${status === 'cloning' || status === 'indexing' ? `
            <div class="progress-indicator">
              <div class="spinner"></div>
              <span class="progress-text">${status === 'cloning' ? 'Cloning repository...' : 'Building semantic index...'}</span>
            </div>
          ` : ''}
        </div>
        
        <div class="repository-details">
          <div class="detail-row">
            <span class="detail-label">Description:</span>
            <span class="detail-value">${repo.description || 'No description'}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Git URL:</span>
            <span class="detail-value git-url">${repo.gitUrl || 'N/A'}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Type:</span>
            <span class="detail-value">${repo.type}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Size:</span>
            <span class="detail-value" data-field="size">${this.formatFileSize(repo.size)}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Cidx Aware:</span>
            <span class="detail-value cidx-aware-${repo.cidxAware}">
              ${repo.cidxAware ? (this.getRepositoryStatus(repo) === 'failed' ? '❌ Failed' : '🧠 Yes') : '❌ No'}
            </span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Registered:</span>
            <span class="detail-value">${repo.registeredAt ? new Date(repo.registeredAt).toLocaleString() : 'N/A'}</span>
          </div>
          <div class="detail-row">
            <span class="detail-label">Last Updated:</span>
            <span class="detail-value" data-field="last-updated">${new Date(repo.lastModified).toLocaleString()}</span>
          </div>
          ${repo.currentBranch ? `
            <div class="detail-row" data-field="branch-row">
              <span class="detail-label">Branch:</span>
              <span class="detail-value" data-field="branch">🌿 ${repo.currentBranch}</span>
            </div>
          ` : ''}
          ${repo.commitHash ? `
            <div class="detail-row" data-field="commit-row">
              <span class="detail-label">Latest Commit:</span>
              <span class="detail-value" data-field="commit">
                <code>${repo.commitHash.substring(0, 8)}</code>
                ${repo.commitMessage ? ` - ${repo.commitMessage}` : ''}
              </span>
            </div>
          ` : ''}
        </div>
        
        <div class="repository-actions">
          ${status === 'ready' ? `
            <button class="btn btn-primary" onclick="claudeUI.browseRepository('${repo.name}')" data-testid="browse-files">
              📁 Browse Files
            </button>
          ` : ''}
          <button class="btn btn-danger" onclick="claudeUI.deleteRepository('${repo.name}')">
            Unregister
          </button>
        </div>
      </div>
    `;
  }

  hasRepositoryChanged(cachedRepo, newRepo) {
    // Check key fields that might change
    const changed = (
      cachedRepo.cloneStatus !== newRepo.cloneStatus ||
      cachedRepo.size !== newRepo.size ||
      cachedRepo.lastModified !== newRepo.lastModified ||
      cachedRepo.currentBranch !== newRepo.currentBranch ||
      cachedRepo.commitHash !== newRepo.commitHash ||
      cachedRepo.commitMessage !== newRepo.commitMessage
    );
    
    if (changed) {
      console.log('Repository changed:', newRepo.name, {
        cloneStatus: `${cachedRepo.cloneStatus} → ${newRepo.cloneStatus}`,
        size: cachedRepo.size !== newRepo.size ? `${cachedRepo.size} → ${newRepo.size}` : 'unchanged',
        lastModified: cachedRepo.lastModified !== newRepo.lastModified ? 'changed' : 'unchanged'
      });
    }
    
    return changed;
  }

  startRepositoryPolling(repositories) {
    // Clear existing polling interval if any
    if (this.repositoryPollingInterval) {
      clearInterval(this.repositoryPollingInterval);
      this.repositoryPollingInterval = null;
    }

    // Check if any repositories are processing
    const processingRepos = repositories.filter(repo => {
      const status = this.getRepositoryStatus(repo);
      return status === 'cloning' || status === 'indexing';
    });

    console.log('Repository polling check:', { 
      processingCount: processingRepos.length, 
      totalCount: repositories.length,
      processingRepos: processingRepos.map(r => ({ name: r.name, status: this.getRepositoryStatus(r) }))
    });

    if (processingRepos.length > 0) {
      // Start polling timer if not already started
      if (!this.repositoryPollStartTime) {
        this.repositoryPollStartTime = Date.now();
        console.log('Starting repository polling timer...');
      }

      // Check if we've been polling too long
      const pollingDuration = Date.now() - this.repositoryPollStartTime;
      if (pollingDuration > this.maxPollingDuration) {
        console.warn('Repository polling timeout reached, stopping polling');
        this.stopRepositoryPolling();
        this.showToast('Repository processing is taking longer than expected. Please refresh manually or check server logs.', 'warning');
        return;
      }

      console.log(`Repository polling active (${Math.round(pollingDuration / 1000)}s elapsed)`);
      // Poll every 8 seconds for updates (less aggressive)
      this.repositoryPollingInterval = setInterval(async () => {
        try {
          await this.loadRepositories();
        } catch (error) {
          console.error('Repository polling error:', error);
          this.stopRepositoryPolling();
          this.showToast('Error polling repositories. Please refresh manually.', 'error');
        }
      }, 8000);
    } else {
      console.log('No repositories processing, stopping polling');
      this.stopRepositoryPolling();
    }
  }

  stopRepositoryPolling() {
    if (this.repositoryPollingInterval) {
      console.log('Stopping repository polling');
      clearInterval(this.repositoryPollingInterval);
      this.repositoryPollingInterval = null;
    }
    // Reset polling timer
    this.repositoryPollStartTime = null;
  }

  async deleteRepository(repoName) {
    if (!confirm(`Are you sure you want to unregister repository "${repoName}"?`)) return;

    try {
      await ClaudeApi.unregisterRepository(repoName);
      this.showToast('Repository unregistered successfully', 'success');
      await this.loadRepositories();
    } catch (error) {
      console.error('Delete repository error:', error);
      this.showToast('Failed to unregister repository', 'error');
    }
  }

  async loadRepositoriesForJobCreation() {
    const select = document.getElementById('job-repository');
    if (!select) return;

    // Reset file upload state when entering job creation view
    this.resetFileUploadState();

    select.innerHTML = '<option value="">Loading repositories...</option>';

    try {
      const repositories = await ClaudeApi.getRepositories();
      
      // Store repositories data for dynamic cidx checkbox control
      this.repositoriesForJobCreation = repositories;
      
      select.innerHTML = '<option value="">Select a repository...</option>';
      
      repositories.forEach(repo => {
        const option = document.createElement('option');
        option.value = repo.name;
        option.textContent = `${repo.name} - ${repo.description || 'No description'}`;
        // Store cidx-aware status in option data
        option.dataset.cidxAware = repo.cidxAware ? 'true' : 'false';
        option.dataset.cloneStatus = repo.cloneStatus || 'unknown';
        select.appendChild(option);
      });

      if (repositories.length === 0) {
        select.innerHTML = '<option value="">No repositories available</option>';
      }
      
      // Set up repository selection change handler for cidx checkbox control
      this.setupRepositorySelectionHandler();
      
    } catch (error) {
      console.error('Load repositories for job creation error:', error);
      select.innerHTML = '<option value="">Failed to load repositories</option>';
    }
  }

  showError(element, message) {
    element.textContent = message;
    element.style.display = 'block';
  }

  showToast(message, type = 'info') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    
    container.appendChild(toast);
    
    // Auto remove after 5 seconds
    setTimeout(() => {
      if (toast.parentNode) {
        toast.parentNode.removeChild(toast);
      }
    }, 5000);
  }

  showRegisterRepository() {
    this.showRepositoryModal();
  }

  showRepositoryModal() {
    // Create modal HTML
    const modalHtml = `
      <div class="modal-overlay" id="repo-modal-overlay">
        <div class="modal-dialog">
          <div class="modal-header">
            <h2>Register Repository</h2>
            <button class="modal-close" id="repo-modal-close">&times;</button>
          </div>
          <div class="modal-body">
            <form id="repo-register-form">
              <div class="form-group">
                <label for="repo-url">Git URL or Local Path</label>
                <input 
                  type="text" 
                  id="repo-url" 
                  class="form-input" 
                  placeholder="https://github.com/user/repo.git or /path/to/local/repo"
                  required
                />
                <small class="form-help">Enter a Git repository URL to clone, or a local path to an existing repository</small>
              </div>
              
              <div class="form-group">
                <label for="repo-name">Repository Name</label>
                <input 
                  type="text" 
                  id="repo-name" 
                  class="form-input" 
                  placeholder="Auto-generated from URL"
                />
                <small class="form-help">Will be auto-filled based on URL, but you can customize it</small>
              </div>
              
              <div class="form-group">
                <label for="repo-description">Description (Optional)</label>
                <textarea 
                  id="repo-description" 
                  class="form-input" 
                  rows="3"
                  placeholder="Brief description of this repository"
                ></textarea>
              </div>
              
              <div class="form-group">
                <div class="checkbox-group">
                  <input 
                    type="checkbox" 
                    id="repo-cidx-aware" 
                    data-testid="repo-cidx-aware"
                    checked
                  />
                  <label for="repo-cidx-aware">Enable semantic indexing (cidx)</label>
                </div>
                <small class="form-help">Enable cidx indexing for semantic search capabilities. This may take a long time for large repositories.</small>
              </div>
              
              <div class="form-actions">
                <button type="button" class="btn btn-secondary" id="repo-cancel">Cancel</button>
                <button type="submit" class="btn btn-primary" id="repo-submit">
                  <span id="repo-submit-text">Register Repository</span>
                  <div id="repo-loading" class="loading-spinner" style="display: none;"></div>
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    `;

    // Add modal to page
    document.body.insertAdjacentHTML('beforeend', modalHtml);

    // Get form elements
    const overlay = document.getElementById('repo-modal-overlay');
    const form = document.getElementById('repo-register-form');
    const urlInput = document.getElementById('repo-url');
    const nameInput = document.getElementById('repo-name');
    const descriptionInput = document.getElementById('repo-description');
    const closeBtn = document.getElementById('repo-modal-close');
    const cancelBtn = document.getElementById('repo-cancel');
    const submitBtn = document.getElementById('repo-submit');
    const submitText = document.getElementById('repo-submit-text');
    const loadingSpinner = document.getElementById('repo-loading');

    // Auto-generate name from URL
    urlInput.addEventListener('input', () => {
      const url = urlInput.value.trim();
      if (url) {
        let repoName;
        if (url.includes('.git')) {
          repoName = url.split('/').pop()?.replace('.git', '') || '';
        } else {
          repoName = url.split('/').pop() || '';
        }
        nameInput.value = repoName;
      }
    });

    // Close modal handlers
    const closeModal = () => {
      overlay.remove();
    };

    closeBtn.addEventListener('click', closeModal);
    cancelBtn.addEventListener('click', closeModal);
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) closeModal();
    });

    // Form submission
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      
      const url = urlInput.value.trim();
      const name = nameInput.value.trim();
      const description = descriptionInput.value.trim();
      const cidxAware = document.getElementById('repo-cidx-aware').checked;

      if (!url) {
        alert('Please enter a Git URL or local path');
        return;
      }

      if (!name) {
        alert('Please enter a repository name');
        return;
      }

      // Show loading state
      submitBtn.disabled = true;
      submitText.style.display = 'none';
      loadingSpinner.style.display = 'inline-block';

      try {
        const repoData = {
          Name: name,
          GitUrl: url,
          Description: description || `Repository: ${name}`,
          CidxAware: cidxAware
        };

        await ClaudeApi.registerRepository(repoData);
        
        // Close modal immediately - don't wait for cloning/indexing
        closeModal();
        
        // Show success message with status explanation
        const statusMessage = cidxAware 
          ? `Repository '${name}' registration started! Cloning and indexing may take a while for large repositories.`
          : `Repository '${name}' registration started! Cloning in progress.`;
        this.showToast(statusMessage, 'success');
        
        // Navigate to repositories view to show the new repo with status
        if (this.currentView !== 'repositories') {
          await this.showView('repositories');
        } else {
          // Refresh repositories list to show the new repo
          await this.loadRepositories();
        }
      } catch (error) {
        console.error('Register repository error:', error);
        this.showToast(`Failed to register repository: ${error.message}`, 'error');
        
        // Reset loading state
        submitBtn.disabled = false;
        submitText.style.display = 'inline';
        loadingSpinner.style.display = 'none';
      }
    });

    // Focus first input
    urlInput.focus();
  }
}

// Initialize the application
const claudeUI = new ClaudeWebUI();

// Make it globally available for onclick handlers
window.claudeUI = claudeUI;