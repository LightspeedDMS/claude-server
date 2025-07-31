/**
 * Modern Job File Manager
 * Provides a comprehensive file browsing interface for job workspace files
 */

class JobFileManager {
    constructor() {
        this.currentJob = null;
        this.currentPath = '';
        this.files = [];
        this.directories = [];
        this.filters = {
            mask: '',
            type: '',
            depth: null
        };
        
        this.init();
    }

    init() {
        this.bindEvents();
        this.loadJobs();
        this.initializeResizer();
    }

    bindEvents() {
        // Job selection
        document.getElementById('jobSelect').addEventListener('change', (e) => {
            this.selectJob(e.target.value);
        });

        document.getElementById('refreshJobs').addEventListener('click', () => {
            this.loadJobs();
        });

        // Filters
        document.getElementById('applyFilters').addEventListener('click', () => {
            this.applyFilters();
        });

        document.getElementById('clearFilters').addEventListener('click', () => {
            this.clearFilters();
        });

        // File filter input - apply on Enter
        document.getElementById('fileFilter').addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.applyFilters();
            }
        });

        // View mode toggles
        document.getElementById('listView').addEventListener('click', () => {
            this.setViewMode('list');
        });

        document.getElementById('gridView').addEventListener('click', () => {
            this.setViewMode('grid');
        });

        // Modal events
        document.getElementById('closeFileViewer').addEventListener('click', () => {
            this.closeFileViewer();
        });

        document.getElementById('closeFileViewerFooter').addEventListener('click', () => {
            this.closeFileViewer();
        });

        document.getElementById('downloadFileBtn').addEventListener('click', () => {
            this.downloadCurrentFile();
        });

        document.getElementById('overlay').addEventListener('click', () => {
            this.closeFileViewer();
        });

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                this.closeFileViewer();
            }
        });
    }

    async loadJobs() {
        try {
            // Check authentication first
            if (!(await this.checkAuthentication())) {
                return;
            }

            this.showLoading(true);
            
            const response = await fetch('/jobs', {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (response.status === 401) {
                // Token expired, prompt for login again
                localStorage.removeItem('authToken');
                this.showLoginPrompt();
                return;
            }

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const jobs = await response.json();
            this.populateJobSelector(jobs);
        } catch (error) {
            console.error('Error loading jobs:', error);
            this.showError('Failed to load jobs. Please check your connection and try again.');
        } finally {
            this.showLoading(false);
        }
    }

    populateJobSelector(jobs) {
        const jobSelect = document.getElementById('jobSelect');
        jobSelect.innerHTML = '<option value="">Select a job...</option>';
        
        jobs.forEach(job => {
            const option = document.createElement('option');
            option.value = job.jobId;
            option.textContent = `${job.repository} - ${job.status} (${new Date(job.started).toLocaleDateString()})`;
            jobSelect.appendChild(option);
        });
    }

    async selectJob(jobId) {
        if (!jobId) {
            this.showWelcome();
            return;
        }

        this.currentJob = jobId;
        this.currentPath = '';
        this.updateBreadcrumb([]);
        
        await this.loadDirectoryTree();
        this.showFileManager();
    }

    async loadDirectoryTree() {
        if (!this.currentJob) return;

        try {
            this.showLoading(true);
            
            // Load only the root directories initially
            const response = await fetch(`/jobs/${this.currentJob}/files/directories`, {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const directories = await response.json();
            this.directories = this.buildDirectoryTreeFromFlat(directories || []);
            this.renderDirectoryTree();
            
            // Load files for the current directory (root initially)
            await this.loadFilesForDirectory(this.currentPath);
            
        } catch (error) {
            console.error('Error loading directory tree:', error);
            this.showError('Failed to load directories. Please try again.');
        } finally {
            this.showLoading(false);
        }
    }

    async loadSubdirectories(directoryPath) {
        if (!this.currentJob) return;

        try {
            const response = await fetch(`/jobs/${this.currentJob}/files/directories?path=${encodeURIComponent(directoryPath)}`, {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const subdirectories = await response.json();
            return subdirectories || [];
            
        } catch (error) {
            console.error('Error loading subdirectories:', error);
            return [];
        }
    }

    async loadFilesForDirectory(directoryPath) {
        if (!this.currentJob) return;

        try {
            // Build query parameters for file filtering
            const params = new URLSearchParams();
            params.append('path', directoryPath);
            if (this.filters.mask) params.append('mask', this.filters.mask);

            const response = await fetch(`/jobs/${this.currentJob}/files/files?${params}`, {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const files = await response.json();
            this.files = files || [];
            this.currentPath = directoryPath;
            
            this.renderFileList();
            this.updateBreadcrumb(directoryPath.split('/').filter(p => p));
            
        } catch (error) {
            console.error('Error loading files:', error);
            this.showError('Failed to load files. Please try again.');
        }
    }

    buildDirectoryTreeFromFlat(directories) {
        // Build a flat structure that can be lazy-loaded
        const treeMap = new Map();
        
        directories.forEach(dir => {
            treeMap.set(dir.path, {
                name: dir.name,
                path: dir.path,
                modified: dir.modified,
                hasSubdirectories: dir.hasSubdirectories,
                fileCount: dir.fileCount,
                children: new Map(),
                loaded: false,
                expanded: false
            });
        });
        
        return treeMap;
    }

    renderDirectoryTree() {
        const treeContainer = document.getElementById('directoryTree');
        
        if (this.directories.size === 0) {
            treeContainer.innerHTML = '<div class="empty-state">No directories found</div>';
            return;
        }

        let html = '<div class="tree-root">';
        
        // Render root directories
        for (const [path, dir] of this.directories) {
            html += this.renderDirectoryNode(dir, 0);
        }
        
        html += '</div>';
        treeContainer.innerHTML = html;
    }

    renderDirectoryNode(dir, level) {
        const indent = level * 20;
        const hasChildren = dir.hasSubdirectories;
        const isSelected = this.currentPath === dir.path;
        const canExpand = hasChildren && !dir.expanded;
        const canCollapse = hasChildren && dir.expanded;
        
        let html = `
            <div class="directory-item ${isSelected ? 'selected' : ''}" 
                 data-path="${dir.path}"
                 style="padding-left: ${indent}px">
                <div class="directory-content" onclick="fileManager.handleDirectoryClick('${dir.path}')">
        `;
        
        if (hasChildren) {
            html += `
                <span class="expand-icon ${dir.expanded ? 'expanded' : ''}" 
                      onclick="event.stopPropagation(); fileManager.toggleDirectory('${dir.path}')">
                    ${dir.expanded ? '▼' : '▶'}
                </span>
            `;
        } else {
            html += '<span class="expand-icon placeholder">  </span>';
        }
        
        html += `
                    <span class="directory-icon">📁</span>
                    <span class="directory-name">${dir.name}</span>
                    <span class="file-count">(${dir.fileCount})</span>
                </div>
            </div>
        `;
        
        // Render children if expanded
        if (dir.expanded && dir.children.size > 0) {
            for (const [childPath, childDir] of dir.children) {
                html += this.renderDirectoryNode(childDir, level + 1);
            }
        }
        
        return html;
    }

    async toggleDirectory(directoryPath) {
        const dir = this.directories.get(directoryPath);
        if (!dir) return;

        if (!dir.expanded) {
            // Expand: load subdirectories if not loaded
            if (!dir.loaded) {
                const subdirectories = await this.loadSubdirectories(directoryPath);
                
                subdirectories.forEach(subdir => {
                    const subdirData = {
                        name: subdir.name,
                        path: subdir.path,
                        modified: subdir.modified,
                        hasSubdirectories: subdir.hasSubdirectories,
                        fileCount: subdir.fileCount,
                        children: new Map(),
                        loaded: false,
                        expanded: false
                    };
                    dir.children.set(subdir.path, subdirData);
                });
                
                dir.loaded = true;
            }
            dir.expanded = true;
        } else {
            // Collapse
            dir.expanded = false;
        }
        
        this.renderDirectoryTree();
    }

    async handleDirectoryClick(directoryPath) {
        // Load files for this directory
        await this.loadFilesForDirectory(directoryPath);
        
        // Update selection in the tree
        this.renderDirectoryTree();
    }

    renderFileList() {
        const fileListContainer = document.getElementById('fileList');
        const files = this.files.filter(f => f.type === 'file');
        
        if (files.length === 0) {
            fileListContainer.innerHTML = '<div class="empty-state">No files found in this directory.</div>';
            return;
        }

        const html = files.map(file => {
            const fileType = this.getFileType(file.name);
            const fileSize = this.formatFileSize(file.size);
            const lastModified = new Date(file.modified).toLocaleDateString();
            
            return `
                <div class="file-item" data-type="${fileType}" data-path="${file.path}">
                    <div class="file-icon"></div>
                    <div class="file-info">
                        <div class="file-name">${file.name}</div>
                        <div class="file-meta">
                            <span class="file-size">${fileSize}</span>
                            <span class="file-date">${lastModified}</span>
                        </div>
                    </div>
                    <div class="file-actions">
                        <button class="action-btn view" onclick="fileManager.viewFile('${file.path}', '${file.name}')">
                            View
                        </button>
                        <button class="action-btn download" onclick="fileManager.downloadFile('${file.path}', '${file.name}')">
                            Download
                        </button>
                    </div>
                </div>
            `;
        }).join('');
        
        fileListContainer.innerHTML = html;
    }

    async navigateToDirectory(path) {
        await this.loadFilesForDirectory(path);
    }

    async viewFile(filePath, fileName) {
        try {
            this.showLoading(true);
            
            const response = await fetch(`/jobs/${this.currentJob}/files/content?path=${encodeURIComponent(filePath)}`, {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const result = await response.json();
            this.showFileViewer(fileName, result.content, filePath);
            
        } catch (error) {
            console.error('Error viewing file:', error);
            this.showError('Failed to load file content. Please try again.');
        } finally {
            this.showLoading(false);
        }
    }

    async downloadFile(filePath, fileName) {
        try {
            const response = await fetch(`/jobs/${this.currentJob}/files/download?path=${encodeURIComponent(filePath)}`, {
                headers: {
                    'Authorization': 'Bearer ' + this.getAuthToken()
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const blob = await response.blob();
            this.downloadBlob(blob, fileName);
            
        } catch (error) {
            console.error('Error downloading file:', error);
            this.showError('Failed to download file. Please try again.');
        }
    }

    downloadBlob(blob, filename) {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.style.display = 'none';
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);
    }

    showFileViewer(fileName, content, filePath) {
        document.getElementById('fileViewerTitle').textContent = fileName;
        document.getElementById('fileViewerContent').textContent = content;
        document.getElementById('downloadFileBtn').setAttribute('data-path', filePath);
        document.getElementById('downloadFileBtn').setAttribute('data-name', fileName);
        
        document.getElementById('fileViewerModal').classList.remove('hidden');
        document.getElementById('overlay').classList.remove('hidden');
    }

    closeFileViewer() {
        document.getElementById('fileViewerModal').classList.add('hidden');
        document.getElementById('overlay').classList.add('hidden');
    }

    downloadCurrentFile() {
        const btn = document.getElementById('downloadFileBtn');
        const filePath = btn.getAttribute('data-path');
        const fileName = btn.getAttribute('data-name');
        
        if (filePath && fileName) {
            this.downloadFile(filePath, fileName);
        }
    }

    applyFilters() {
        this.filters.mask = document.getElementById('fileFilter').value.trim();
        
        // Reload files for current directory with new filters
        this.loadFilesForDirectory(this.currentPath);
    }

    clearFilters() {
        document.getElementById('fileFilter').value = '';
        
        this.filters = { mask: '' };
        this.loadFilesForDirectory(this.currentPath);
    }

    setViewMode(mode) {
        const fileList = document.getElementById('fileList');
        const listBtn = document.getElementById('listView');
        const gridBtn = document.getElementById('gridView');
        
        if (mode === 'grid') {
            fileList.classList.add('grid-view');
            listBtn.classList.remove('active');
            gridBtn.classList.add('active');
        } else {
            fileList.classList.remove('grid-view');
            listBtn.classList.add('active');
            gridBtn.classList.remove('active');
        }
    }

    updateBreadcrumb(pathParts) {
        const breadcrumb = document.getElementById('breadcrumb');
        let html = '<span class="breadcrumb-item" onclick="fileManager.navigateToDirectory(\'\')">Root</span>';
        
        let currentPath = '';
        pathParts.forEach((part, index) => {
            currentPath += (currentPath ? '/' : '') + part;
            const isLast = index === pathParts.length - 1;
            html += `<span class="breadcrumb-item ${isLast ? 'active' : ''}" 
                           onclick="fileManager.navigateToDirectory('${currentPath}')">${part}</span>`;
        });
        
        breadcrumb.innerHTML = html;
    }

    showWelcome() {
        document.getElementById('fileManager').classList.add('hidden');
        document.getElementById('welcomeMessage').classList.remove('hidden');
        document.getElementById('loadingIndicator').classList.add('hidden');
    }

    showFileManager() {
        document.getElementById('fileManager').classList.remove('hidden');
        document.getElementById('welcomeMessage').classList.add('hidden');
        document.getElementById('loadingIndicator').classList.add('hidden');
    }

    showLoading(show) {
        if (show) {
            document.getElementById('loadingIndicator').classList.remove('hidden');
        } else {
            document.getElementById('loadingIndicator').classList.add('hidden');
        }
    }

    showError(message) {
        // Simple error display - could be enhanced with a proper modal
        alert('Error: ' + message);
    }

    getFileType(fileName) {
        const ext = fileName.split('.').pop().toLowerCase();
        
        const typeMap = {
            'js': 'javascript',
            'ts': 'typescript',
            'json': 'json',
            'html': 'html',
            'htm': 'html',
            'css': 'css',
            'scss': 'css',
            'less': 'css',
            'png': 'image',
            'jpg': 'image',
            'jpeg': 'image',
            'gif': 'image',
            'svg': 'image',
            'txt': 'text',
            'log': 'text',
            'md': 'markdown',
            'markdown': 'markdown',
            'zip': 'archive',
            'tar': 'archive',
            'gz': 'archive'
        };
        
        return typeMap[ext] || 'unknown';
    }

    formatFileSize(bytes) {
        if (bytes === 0) return '0 B';
        
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        
        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
    }

    getAuthToken() {
        return localStorage.getItem('authToken') || '';
    }

    async checkAuthentication() {
        const token = this.getAuthToken();
        if (!token) {
            this.showLoginPrompt();
            return false;
        }
        return true;
    }

    showLoginPrompt(retryReason = null) {
        this.authRetryCount = this.authRetryCount || 0;
        this.maxRetries = this.maxRetries || 3;
        
        let promptMessage = 'Enter username:';
        if (retryReason) {
            promptMessage = `Authentication failed (${retryReason}). Enter username:`;
        }
        
        const username = prompt(promptMessage);
        if (!username) return;

        let passwordMessage = 'Enter password or hash (e.g., $y$...):';
        if (retryReason) {
            passwordMessage = `Enter password or hash (${retryReason}):`;
        }
        
        const password = prompt(passwordMessage);
        
        if (username && password) {
            this.login(username, password);
        }
    }

    async login(username, password) {
        try {
            // Detect if user is providing a pre-computed hash
            const isHashAuth = this.isPrecomputedHash(password);
            
            const response = await fetch('/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ 
                    username, 
                    password,
                    authType: isHashAuth ? 'hash' : 'plaintext'
                })
            });

            if (response.ok) {
                const result = await response.json();
                localStorage.setItem('authToken', result.token);
                localStorage.setItem('username', result.username);
                this.authRetryCount = 0; // Reset retry count on success
                this.loadJobs(); // Retry loading jobs after authentication
                return true;
            } else {
                // Parse detailed error response
                const errorData = await response.json().catch(() => ({}));
                const errorType = errorData.errorType || 'Unknown';
                const errorMessage = errorData.error || 'Login failed';
                
                this.handleLoginError(errorType, errorMessage);
                return false;
            }
        } catch (error) {
            console.error('Login error:', error);
            this.handleLoginError('NetworkError', 'Network error occurred. Please check your connection.');
            return false;
        }
    }

    // Test method for hash detection
    isPrecomputedHash(password) {
        // Shadow file hash format: $algorithm$salt$hash
        // Valid algorithms: $1$ (MD5), $5$ (SHA-256), $6$ (SHA-512), $y$ (yescrypt)
        if (!password.startsWith("$")) return false;
        
        const parts = password.split('$');
        if (parts.length < 4) return false;
        
        // Check if algorithm is supported
        const algorithm = parts[1];
        return algorithm === "1" || algorithm === "5" || algorithm === "6" || algorithm === "y";
    }

    // Specific error handling with retry logic
    handleLoginError(errorType, errorMessage) {
        this.authRetryCount = (this.authRetryCount || 0) + 1;
        
        if (this.authRetryCount >= (this.maxRetries || 3)) {
            alert(`Authentication failed after ${this.maxRetries || 3} attempts. Please contact an administrator.\n\nLast error: ${errorMessage}`);
            this.authRetryCount = 0;
            return;
        }

        let retryPrompt = '';
        switch (errorType) {
            case 'InvalidCredentials':
                retryPrompt = 'Invalid username or password';
                break;
            case 'MalformedHash':
                retryPrompt = 'Invalid hash format. Use $y$... for yescrypt hashes';
                break;
            case 'UserNotFound':
                retryPrompt = 'User not found';
                break;
            case 'ValidationError':
                retryPrompt = 'Username and password are required';
                break;
            case 'NetworkError':
                retryPrompt = 'Network error';
                break;
            default:
                retryPrompt = 'Authentication error';
        }

        // Show retry prompt with specific error context
        setTimeout(() => {
            this.showLoginPrompt(retryPrompt);
        }, 100);
    }

    initializeResizer() {
        const splitter = document.querySelector('.splitter');
        const leftPanel = document.querySelector('.left-panel');
        let isResizing = false;

        splitter.addEventListener('mousedown', (e) => {
            isResizing = true;
            document.addEventListener('mousemove', handleMouseMove);
            document.addEventListener('mouseup', handleMouseUp);
            e.preventDefault();
        });

        function handleMouseMove(e) {
            if (!isResizing) return;
            
            const containerRect = document.querySelector('.file-manager-content').getBoundingClientRect();
            const newWidth = e.clientX - containerRect.left;
            
            if (newWidth >= 200 && newWidth <= 500) {
                leftPanel.style.width = newWidth + 'px';
            }
        }

        function handleMouseUp() {
            isResizing = false;
            document.removeEventListener('mousemove', handleMouseMove);
            document.removeEventListener('mouseup', handleMouseUp);
        }
    }
}

// Initialize the file manager when the page loads
let fileManager;
document.addEventListener('DOMContentLoaded', () => {
    fileManager = new JobFileManager();
});

// Global function for inline event handlers
window.fileManager = fileManager;