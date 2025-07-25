import { JobService, JobMonitor } from '../services/jobs.js';
import { repositoryService } from '../services/repositories.js';

/**
 * Job Creation Component - ChatGPT-like interface for creating Claude Code jobs
 */
export class JobCreateComponent {
  constructor(container) {
    this.container = container;
    this.jobService = new JobService();
    this.repositories = [];
    this.uploadedFiles = [];
    this.isSubmitting = false;
    this.showAdvancedOptions = false;
    
    // Job options defaults
    this.jobOptions = {
      timeout: 300,
      gitAware: true,
      cidxAware: true,
    };
    
    this.init();
  }

  async init() {
    await this.loadRepositories();
    this.render();
    this.attachEventListeners();
    this.updateSubmitButton();
  }

  async loadRepositories() {
    try {
      this.repositories = await repositoryService.getRepositories();
    } catch (error) {
      console.error('Failed to load repositories:', error);
      this.showError('Failed to load repositories. Please try again.');
    }
  }

  render() {
    const html = `
      <div class="job-create-container">
        <form class="job-create-form" data-testid="job-creation-form">
          <div class="job-create-header">
            <h1>Create New Job</h1>
            <p>Describe what you'd like Claude Code to do with your repository</p>
          </div>
          
          <div class="job-create-body">
            <!-- Repository Selection -->
            <div class="repository-section">
              <label class="form-label" for="repository-select">
                Select Repository <span style="color: var(--color-error);">*</span>
              </label>
              <select 
                class="form-control form-select repository-select" 
                id="repository-select"
                data-testid="repository-select"
                required
              >
                <option value="">Choose a repository...</option>
                ${this.repositories.map(repo => `
                  <option value="${repo.name}" ${repo.cloneStatus !== 'ready' ? 'disabled' : ''}>
                    ${repo.name} ${repo.cloneStatus !== 'ready' ? `(${repo.cloneStatus})` : ''}
                  </option>
                `).join('')}
              </select>
              ${this.repositories.length === 0 ? `
                <small class="text-muted">No repositories available. <a href="/repositories">Register a repository</a> first.</small>
              ` : ''}
            </div>

            <!-- Prompt Input -->
            <div class="prompt-section">
              <label class="form-label" for="prompt-input">
                What would you like Claude Code to do? <span style="color: var(--color-error);">*</span>
              </label>
              <textarea 
                class="form-control prompt-textarea" 
                id="prompt-input"
                data-testid="prompt-input"
                placeholder="Describe your task in detail. For example:

• Analyze this codebase and suggest improvements
• Add unit tests for the authentication module
• Refactor the database connection logic
• Create documentation for the API endpoints
• Fix the bug in the user registration flow

Be specific about what you want Claude Code to accomplish!"
                required
              ></textarea>
            </div>

            <!-- File Upload -->
            <div class="file-upload-section">
              <label class="form-label">Upload Files (Optional)</label>
              <div 
                class="file-upload-area" 
                data-testid="file-upload-area"
                id="file-upload-area"
              >
                <div class="file-upload-icon">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                    <polyline points="7,10 12,15 17,10"/>
                    <line x1="12" y1="15" x2="12" y2="3"/>
                  </svg>
                </div>
                <p class="file-upload-text">Drop files here or click to browse</p>
                <p class="file-upload-subtext">
                  Support for images, documents, code files up to 50MB each
                </p>
              </div>
              <input 
                type="file" 
                class="file-upload-input" 
                id="file-upload-input"
                data-testid="file-upload"
                multiple 
                accept="*/*"
              />
              
              <div class="uploaded-files" id="uploaded-files" data-testid="uploaded-files">
                <!-- Uploaded files will appear here -->
              </div>
            </div>

            <!-- Job Options -->
            <div class="job-options-section">
              <button 
                type="button" 
                class="options-toggle" 
                id="options-toggle"
                data-testid="advanced-options-toggle"
              >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 20h9"/>
                  <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/>
                </svg>
                Advanced Options
                <svg class="options-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="transform: ${this.showAdvancedOptions ? 'rotate(180deg)' : 'rotate(0deg)'}; transition: transform 0.2s;">
                  <path d="m6 9 6 6 6-6"/>
                </svg>
              </button>
              
              <div class="options-panel ${this.showAdvancedOptions ? '' : 'hidden'}" id="options-panel">
                <div class="options-grid">
                  <div class="option-group">
                    <label class="form-label" for="timeout-select">Timeout</label>
                    <select class="form-control form-select" id="timeout-select" data-testid="timeout">
                      <option value="300" ${this.jobOptions.timeout === 300 ? 'selected' : ''}>5 minutes</option>
                      <option value="600" ${this.jobOptions.timeout === 600 ? 'selected' : ''}>10 minutes</option>
                      <option value="900" ${this.jobOptions.timeout === 900 ? 'selected' : ''}>15 minutes</option>
                      <option value="1800" ${this.jobOptions.timeout === 1800 ? 'selected' : ''}>30 minutes</option>
                    </select>
                  </div>
                  
                  <div class="option-group">
                    <div class="checkbox-group">
                      <input 
                        type="checkbox" 
                        class="checkbox" 
                        id="git-aware" 
                        data-testid="git-aware"
                        ${this.jobOptions.gitAware ? 'checked' : ''}
                      />
                      <label for="git-aware">Git-aware execution</label>
                    </div>
                    <small class="text-muted">Enable Git context and operations</small>
                  </div>
                  
                  <div class="option-group">
                    <div class="checkbox-group">
                      <input 
                        type="checkbox" 
                        class="checkbox" 
                        id="cidx-aware" 
                        data-testid="cidx-aware"
                        ${this.jobOptions.cidxAware ? 'checked' : ''}
                      />
                      <label for="cidx-aware">Semantic search (cidx)</label>
                    </div>
                    <small class="text-muted">Enable AI-powered code search</small>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Submit Section -->
          <div class="submit-section">
            <div class="submit-info">
              <p>Each submission creates an independent job against a fresh copy of your repository</p>
            </div>
            <button 
              type="submit" 
              class="submit-button" 
              id="submit-button"
              data-testid="submit-job"
              disabled
            >
              <span class="spinner hidden" id="submit-spinner"></span>
              <span id="submit-text">Create Job</span>
            </button>
          </div>
        </form>

        <!-- Error/Success Messages -->
        <div class="hidden" id="message-container">
          <div class="card" id="message-card">
            <div class="card-body">
              <p id="message-text"></p>
            </div>
          </div>
        </div>
      </div>
    `;

    this.container.innerHTML = html;
  }

  attachEventListeners() {
    const form = document.getElementById('repository-select').closest('form');
    const repositorySelect = document.getElementById('repository-select');
    const promptInput = document.getElementById('prompt-input');
    const fileUploadArea = document.getElementById('file-upload-area');
    const fileUploadInput = document.getElementById('file-upload-input');
    const optionsToggle = document.getElementById('options-toggle');
    const timeoutSelect = document.getElementById('timeout-select');
    const gitAwareCheckbox = document.getElementById('git-aware');
    const cidxAwareCheckbox = document.getElementById('cidx-aware');

    // Form submission
    form.addEventListener('submit', this.handleSubmit.bind(this));

    // Repository and prompt validation
    repositorySelect.addEventListener('change', this.updateSubmitButton.bind(this));
    promptInput.addEventListener('input', this.updateSubmitButton.bind(this));

    // File upload - drag and drop
    fileUploadArea.addEventListener('click', () => fileUploadInput.click());
    fileUploadArea.addEventListener('dragover', this.handleDragOver.bind(this));
    fileUploadArea.addEventListener('dragleave', this.handleDragLeave.bind(this));
    fileUploadArea.addEventListener('drop', this.handleDrop.bind(this));
    fileUploadInput.addEventListener('change', this.handleFileSelect.bind(this));

    // Advanced options toggle
    optionsToggle.addEventListener('click', this.toggleAdvancedOptions.bind(this));

    // Job options
    timeoutSelect.addEventListener('change', (e) => {
      this.jobOptions.timeout = parseInt(e.target.value);
    });
    
    gitAwareCheckbox.addEventListener('change', (e) => {
      this.jobOptions.gitAware = e.target.checked;
    });
    
    cidxAwareCheckbox.addEventListener('change', (e) => {
      this.jobOptions.cidxAware = e.target.checked;
    });
  }

  updateSubmitButton() {
    const repository = document.getElementById('repository-select').value;
    const prompt = document.getElementById('prompt-input').value.trim();
    const submitButton = document.getElementById('submit-button');
    
    const isValid = repository && prompt && !this.isSubmitting;
    submitButton.disabled = !isValid;
  }

  toggleAdvancedOptions() {
    this.showAdvancedOptions = !this.showAdvancedOptions;
    const panel = document.getElementById('options-panel');
    const chevron = document.querySelector('.options-chevron');
    
    if (this.showAdvancedOptions) {
      panel.classList.remove('hidden');
      chevron.style.transform = 'rotate(180deg)';
    } else {
      panel.classList.add('hidden');
      chevron.style.transform = 'rotate(0deg)';
    }
  }

  handleDragOver(e) {
    e.preventDefault();
    e.stopPropagation();
    document.getElementById('file-upload-area').classList.add('drag-over');
  }

  handleDragLeave(e) {
    e.preventDefault();
    e.stopPropagation();
    document.getElementById('file-upload-area').classList.remove('drag-over');
  }

  handleDrop(e) {
    e.preventDefault();
    e.stopPropagation();
    document.getElementById('file-upload-area').classList.remove('drag-over');
    
    const files = Array.from(e.dataTransfer.files);
    this.addFiles(files);
  }

  handleFileSelect(e) {
    const files = Array.from(e.target.files);
    this.addFiles(files);
    e.target.value = ''; // Reset input
  }

  addFiles(files) {
    const maxSize = 50 * 1024 * 1024; // 50MB
    const validFiles = files.filter(file => {
      if (file.size > maxSize) {
        this.showError(`File ${file.name} is too large. Maximum size is 50MB.`);
        return false;
      }
      return true;
    });

    validFiles.forEach(file => {
      if (!this.uploadedFiles.find(f => f.name === file.name)) {
        this.uploadedFiles.push({
          file,
          name: file.name,
          size: file.size,
          uploading: false,
          progress: 0,
        });
      }
    });

    this.renderUploadedFiles();
  }

  removeFile(fileName, event) {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    this.uploadedFiles = this.uploadedFiles.filter(f => f.name !== fileName);
    this.renderUploadedFiles();
  }

  renderUploadedFiles() {
    const container = document.getElementById('uploaded-files');
    
    if (this.uploadedFiles.length === 0) {
      container.innerHTML = '';
      return;
    }

    const html = this.uploadedFiles.map(fileData => `
      <div class="file-item" data-testid="file-item">
        <div class="file-info">
          <svg class="file-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"/>
          </svg>
          <div class="file-details">
            <div class="file-name">${fileData.name}</div>
            <div class="file-size">${this.formatFileSize(fileData.size)}</div>
            ${fileData.uploading ? `
              <div class="upload-progress">
                <div class="progress-bar">
                  <div class="progress-fill" style="width: ${fileData.progress}%"></div>
                </div>
                <div class="progress-text">${fileData.progress}% uploaded</div>
              </div>
            ` : ''}
          </div>
        </div>
        <div class="file-actions">
          ${!fileData.uploading ? `
            <button 
              type="button" 
              class="remove-file" 
              onclick="jobCreateComponent.removeFile('${fileData.name}', event)"
              data-testid="remove-file"
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          ` : ''}
        </div>
      </div>
    `).join('');

    container.innerHTML = html;
  }

  formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  async handleSubmit(e) {
    e.preventDefault();
    
    if (this.isSubmitting) return;
    
    const repository = document.getElementById('repository-select').value;
    const prompt = document.getElementById('prompt-input').value.trim();
    
    if (!repository || !prompt) {
      this.showError('Please select a repository and enter a prompt.');
      return;
    }

    this.isSubmitting = true;
    this.updateSubmitButton();
    this.showSubmitLoading(true);

    try {
      // Create the job
      const jobData = {
        prompt,
        repository,
        ...this.jobOptions
      };

      const createResponse = await this.jobService.createJob(jobData);
      console.log('Job created:', createResponse);

      // Upload files if any
      if (this.uploadedFiles.length > 0) {
        await this.uploadFiles(createResponse.jobId);
      }

      // Start the job
      const startResponse = await this.jobService.startJob(createResponse.jobId);
      console.log('Job started:', startResponse);

      // Navigate to job monitoring view
      this.navigateToJob(createResponse.jobId);

    } catch (error) {
      console.error('Job creation failed:', error);
      this.showError(error.message || 'Failed to create job. Please try again.');
      this.isSubmitting = false;
      this.updateSubmitButton();
      this.showSubmitLoading(false);
    }
  }

  async uploadFiles(jobId) {
    for (const fileData of this.uploadedFiles) {
      if (fileData.uploading) continue;
      
      fileData.uploading = true;
      fileData.progress = 0;
      this.renderUploadedFiles();

      try {
        await this.jobService.uploadFile(
          jobId, 
          fileData.file, 
          (progress) => {
            fileData.progress = progress;
            this.renderUploadedFiles();
          }
        );
      } catch (error) {
        console.error(`Failed to upload ${fileData.name}:`, error);
        throw new Error(`Failed to upload ${fileData.name}: ${error.message}`);
      }
    }
  }

  showSubmitLoading(loading) {
    const spinner = document.getElementById('submit-spinner');
    const text = document.getElementById('submit-text');
    
    if (loading) {
      spinner.classList.remove('hidden');
      text.textContent = 'Creating Job...';
    } else {
      spinner.classList.add('hidden');
      text.textContent = 'Create Job';
    }
  }

  showError(message) {
    const container = document.getElementById('message-container');
    const card = document.getElementById('message-card');
    const text = document.getElementById('message-text');
    
    text.textContent = message;
    card.style.borderColor = 'var(--color-error)';
    card.style.backgroundColor = 'rgb(239 68 68 / 0.05)';
    container.classList.remove('hidden');
    
    // Auto-hide after 5 seconds
    setTimeout(() => {
      container.classList.add('hidden');
    }, 5000);
  }

  navigateToJob(jobId) {
    // Navigate to job monitoring view
    window.location.href = `/jobs/${jobId}`;
  }
}

// Global reference for event handlers
window.jobCreateComponent = null;