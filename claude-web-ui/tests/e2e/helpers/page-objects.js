/**
 * Page Object Model for Claude Web UI E2E Tests
 * Encapsulates page interactions and element selectors for maintainable tests
 */

import { expect } from '@playwright/test'

/**
 * Base Page Object with common functionality
 */
export class BasePage {
  constructor(page) {
    this.page = page
  }

  async goto(path = '/') {
    await this.page.goto(path)
    await this.page.waitForLoadState('networkidle')
  }

  async waitForElement(selector, options = {}) {
    const element = this.page.locator(selector)
    await element.waitFor({ state: 'visible', timeout: 10000, ...options })
    return element
  }

  async clickElement(selector) {
    const element = await this.waitForElement(selector)
    await element.click()
  }

  async fillField(selector, value) {
    const element = await this.waitForElement(selector)
    await element.fill(value)
  }

  async expectElement(selector, state = 'visible') {
    const element = this.page.locator(selector)
    if (state === 'visible') {
      await expect(element).toBeVisible()
    } else if (state === 'hidden') {
      await expect(element).toBeHidden()
    }
    return element
  }

  async expectText(selector, text) {
    const element = this.page.locator(selector)
    await expect(element).toContainText(text)
  }

  async waitForNetworkIdle() {
    await this.page.waitForLoadState('networkidle')
  }
}

/**
 * Login Page Object
 */
export class LoginPage extends BasePage {
  constructor(page) {
    super(page)
    this.selectors = {
      container: '[data-testid="login-container"]',
      form: '[data-testid="login-form"]',
      usernameField: '[data-testid="username"]',
      passwordField: '[data-testid="password"]',
      loginButton: '[data-testid="login-button"]',
      errorMessage: '[data-testid="error-message"]',
      loadingState: '[data-testid="loading-state"]'
    }
  }

  async login(username, password) {
    await this.goto('/')
    await this.expectElement(this.selectors.container)
    await this.fillField(this.selectors.usernameField, username)
    await this.fillField(this.selectors.passwordField, password)
    await this.clickElement(this.selectors.loginButton)
  }

  async expectLoginError(errorMessage) {
    await this.expectElement(this.selectors.errorMessage)
    await this.expectText(this.selectors.errorMessage, errorMessage)
  }

  async expectLoadingState() {
    await this.expectElement(this.selectors.loadingState)
  }

  async isLoggedIn() {
    try {
      await this.page.waitForURL('**/dashboard', { timeout: 5000 })
      return true
    } catch {
      return false
    }
  }
}

/**
 * Dashboard Page Object
 */
export class DashboardPage extends BasePage {
  constructor(page) {
    super(page)
    this.selectors = {
      container: '[data-testid="dashboard"]',
      jobsNav: '[data-testid="jobs-nav"]',
      repositoriesNav: '[data-testid="repositories-nav"]',
      userMenu: '[data-testid="user-menu"]',
      logoutButton: '[data-testid="logout-button"]',
      createJobButton: '[data-testid="create-job-button"]',
      jobList: '[data-testid="job-list"]',
      repositoryList: '[data-testid="repository-list"]'
    }
  }

  async expectDashboard() {
    await this.expectElement(this.selectors.container)
  }

  async navigateToJobs() {
    await this.clickElement(this.selectors.jobsNav)
    await this.expectElement(this.selectors.jobList)
  }

  async navigateToRepositories() {
    await this.clickElement(this.selectors.repositoriesNav)
    await this.expectElement(this.selectors.repositoryList)
  }

  async logout() {
    await this.clickElement(this.selectors.userMenu)
    await this.clickElement(this.selectors.logoutButton)
  }

  async startCreateJob() {
    await this.clickElement(this.selectors.createJobButton)
  }
}

/**
 * Job Creation Page Object
 */
export class JobCreatePage extends BasePage {
  constructor(page) {
    super(page)
    this.selectors = {
      form: '[data-testid="job-creation-form"]',
      repositorySelect: '[data-testid="repository-select"]',
      promptInput: '[data-testid="prompt-input"]',
      fileUpload: '[data-testid="file-upload"]',
      uploadArea: '[data-testid="upload-area"]',
      uploadedFiles: '[data-testid="uploaded-files"]',
      fileItem: '[data-testid="file-item"]',
      removeFileButton: '[data-testid="remove-file"]',
      advancedOptions: '[data-testid="advanced-options"]',
      timeoutInput: '[data-testid="timeout-input"]',
      gitAwareCheckbox: '[data-testid="git-aware"]',
      cidxAwareCheckbox: '[data-testid="cidx-aware"]',
      submitButton: '[data-testid="submit-job"]',
      cancelButton: '[data-testid="cancel-job"]',
      errorMessage: '[data-testid="error-message"]',
      loadingState: '[data-testid="loading-state"]'
    }
  }

  async fillJobForm(options = {}) {
    const {
      repository = 'frontend-project',
      prompt = 'Test job prompt',
      files = [],
      timeout = 300,
      gitAware = true,
      cidxAware = true
    } = options

    // Select repository
    await this.waitForElement(this.selectors.repositorySelect)
    await this.page.selectOption(this.selectors.repositorySelect, repository)

    // Fill prompt
    await this.fillField(this.selectors.promptInput, prompt)

    // Upload files if provided
    if (files.length > 0) {
      await this.uploadFiles(files)
    }

    // Configure advanced options if different from defaults
    if (timeout !== 300 || !gitAware || !cidxAware) {
      await this.configureAdvancedOptions({ timeout, gitAware, cidxAware })
    }
  }

  async uploadFiles(files) {
    // Convert file names to full paths
    const filePaths = files.map(fileName => 
      `tests/fixtures/test-files/${fileName}`
    )
    
    await this.page.setInputFiles(this.selectors.fileUpload, filePaths)
    
    // Wait for upload completion
    for (const fileName of files) {
      await this.expectElement(`[data-testid="file-item"][data-filename="${fileName}"]`)
    }
  }

  async dragAndDropFiles(files) {
    const uploadArea = await this.waitForElement(this.selectors.uploadArea)
    
    // Simulate drag and drop for each file
    for (const fileName of files) {
      const filePath = `tests/fixtures/test-files/${fileName}`
      await uploadArea.setInputFiles(filePath, { noWaitAfter: true })
    }
    
    await this.waitForNetworkIdle()
  }

  async removeUploadedFile(fileName) {
    const fileItem = `[data-testid="file-item"][data-filename="${fileName}"]`
    const removeButton = `${fileItem} [data-testid="remove-file"]`
    await this.clickElement(removeButton)
  }

  async configureAdvancedOptions(options = {}) {
    const { timeout, gitAware, cidxAware } = options
    
    // Show advanced options
    await this.clickElement(this.selectors.advancedOptions)
    
    if (timeout !== undefined) {
      await this.fillField(this.selectors.timeoutInput, timeout.toString())
    }
    
    if (gitAware !== undefined) {
      const checkbox = this.page.locator(this.selectors.gitAwareCheckbox)
      const isChecked = await checkbox.isChecked()
      if (isChecked !== gitAware) {
        await checkbox.click()
      }
    }
    
    if (cidxAware !== undefined) {
      const checkbox = this.page.locator(this.selectors.cidxAwareCheckbox)
      const isChecked = await checkbox.isChecked()
      if (isChecked !== cidxAware) {
        await checkbox.click()
      }
    }
  }

  async submitJob() {
    await this.clickElement(this.selectors.submitButton)
    await this.waitForNetworkIdle()
  }

  async cancelJob() {
    await this.clickElement(this.selectors.cancelButton)
  }

  async expectValidationError(message) {
    await this.expectElement(this.selectors.errorMessage)
    await this.expectText(this.selectors.errorMessage, message)
  }

  async expectSubmitDisabled() {
    const submitButton = this.page.locator(this.selectors.submitButton)
    await expect(submitButton).toBeDisabled()
  }

  async expectSubmitEnabled() {
    const submitButton = this.page.locator(this.selectors.submitButton)
    await expect(submitButton).toBeEnabled()
  }

  async getJobId() {
    // After successful submission, get the job ID from the redirect or response
    await this.page.waitForURL('**/jobs/**', { timeout: 10000 })
    const url = this.page.url()
    const match = url.match(/\/jobs\/([^\/]+)/)
    return match ? match[1] : null
  }
}

/**
 * Job Details/Monitor Page Object
 */
export class JobDetailsPage extends BasePage {
  constructor(page) {
    super(page)
    this.selectors = {
      container: '[data-testid="job-details"]',
      jobId: '[data-testid="job-id"]',
      jobTitle: '[data-testid="job-title"]',
      jobStatus: '[data-testid="job-status"]',
      statusBadge: '[data-testid="status-badge"]',
      progressBar: '[data-testid="progress-bar"]',
      progressText: '[data-testid="progress-text"]',
      jobOutput: '[data-testid="job-output"]',
      jobError: '[data-testid="job-error"]',
      cancelButton: '[data-testid="cancel-job"]',
      deleteButton: '[data-testid="delete-job"]',
      downloadButton: '[data-testid="download-results"]',
      filesList: '[data-testid="files-list"]',
      fileItem: '[data-testid="file-item"]',
      viewFileButton: '[data-testid="view-file"]',
      downloadFileButton: '[data-testid="download-file"]',
      refreshButton: '[data-testid="refresh-status"]',
      backButton: '[data-testid="back-to-jobs"]'
    }
  }

  async expectJobDetails(jobId) {
    await this.expectElement(this.selectors.container)
    await this.expectText(this.selectors.jobId, jobId)
  }

  async expectJobStatus(status) {
    await this.expectElement(this.selectors.statusBadge)
    await this.expectText(this.selectors.statusBadge, status)
  }

  async waitForJobCompletion(timeout = 300000) {
    // Wait for job to complete or fail
    const statusBadge = this.page.locator(this.selectors.statusBadge)
    
    await expect(statusBadge).toHaveText(/^(completed|failed|cancelled)$/, {
      timeout
    })
    
    const finalStatus = await statusBadge.textContent()
    return finalStatus.toLowerCase()
  }

  async waitForStatusChange(fromStatus, timeout = 30000) {
    const statusBadge = this.page.locator(this.selectors.statusBadge)
    
    // Wait for status to change from the current status
    await expect(statusBadge).not.toHaveText(fromStatus, { timeout })
    
    const newStatus = await statusBadge.textContent()
    return newStatus.toLowerCase()
  }

  async cancelJob() {
    await this.clickElement(this.selectors.cancelButton)
    await this.expectJobStatus('cancelled')
  }

  async deleteJob() {
    await this.clickElement(this.selectors.deleteButton)
    // Confirm deletion if there's a confirmation dialog
    const confirmButton = this.page.locator('[data-testid="confirm-delete"]')
    if (await confirmButton.isVisible({ timeout: 1000 })) {
      await confirmButton.click()
    }
  }

  async getJobOutput() {
    const outputElement = this.page.locator(this.selectors.jobOutput)
    return await outputElement.textContent()
  }

  async getJobError() {
    const errorElement = this.page.locator(this.selectors.jobError)
    return await errorElement.textContent()
  }

  async expectOutputContains(text) {
    await this.expectText(this.selectors.jobOutput, text)
  }

  async expectErrorContains(text) {
    await this.expectText(this.selectors.jobError, text)
  }

  async getGeneratedFiles() {
    const fileItems = await this.page.locator(this.selectors.fileItem).all()
    const files = []
    
    for (const item of fileItems) {
      const name = await item.getAttribute('data-filename')
      const size = await item.locator('[data-testid="file-size"]').textContent()
      files.push({ name, size })
    }
    
    return files
  }

  async viewFile(fileName) {
    const fileItem = `[data-testid="file-item"][data-filename="${fileName}"]`
    const viewButton = `${fileItem} [data-testid="view-file"]`
    await this.clickElement(viewButton)
  }

  async downloadFile(fileName) {
    const fileItem = `[data-testid="file-item"][data-filename="${fileName}"]`
    const downloadButton = `${fileItem} [data-testid="download-file"]`
    
    // Handle download
    const downloadPromise = this.page.waitForEvent('download')
    await this.clickElement(downloadButton)
    const download = await downloadPromise
    
    return download
  }

  async refreshStatus() {
    await this.clickElement(this.selectors.refreshButton)
    await this.waitForNetworkIdle()
  }

  async backToJobsList() {
    await this.clickElement(this.selectors.backButton)
  }
}

/**
 * Repository Management Page Object
 */
export class RepositoryPage extends BasePage {
  constructor(page) {
    super(page)
    this.selectors = {
      container: '[data-testid="repository-list"]',
      registerButton: '[data-testid="register-repo-button"]',
      repositoryItem: '[data-testid="repository-item"]',
      repositoryName: '[data-testid="repo-name"]',
      repositoryStatus: '[data-testid="repo-status"]',
      repositoryUrl: '[data-testid="repo-url"]',
      unregisterButton: '[data-testid="unregister-repo"]',
      refreshButton: '[data-testid="refresh-repos"]',
      
      // Registration form
      registerForm: '[data-testid="register-form"]',
      nameInput: '[data-testid="repo-name-input"]',
      urlInput: '[data-testid="repo-url-input"]',
      descriptionInput: '[data-testid="repo-description-input"]',
      submitRegister: '[data-testid="submit-register"]',
      cancelRegister: '[data-testid="cancel-register"]'
    }
  }

  async expectRepositoryList() {
    await this.expectElement(this.selectors.container)
  }

  async getRepositories() {
    const repoItems = await this.page.locator(this.selectors.repositoryItem).all()
    const repositories = []
    
    for (const item of repoItems) {
      const name = await item.locator(this.selectors.repositoryName).textContent()
      const status = await item.locator(this.selectors.repositoryStatus).textContent()
      const url = await item.locator(this.selectors.repositoryUrl).textContent()
      repositories.push({ name, status, url })
    }
    
    return repositories
  }

  async registerRepository(options = {}) {
    const {
      name = 'test-repo',
      url = 'https://github.com/test/repo.git',
      description = 'Test repository'
    } = options

    await this.clickElement(this.selectors.registerButton)
    await this.expectElement(this.selectors.registerForm)
    
    await this.fillField(this.selectors.nameInput, name)
    await this.fillField(this.selectors.urlInput, url)
    await this.fillField(this.selectors.descriptionInput, description)
    
    await this.clickElement(this.selectors.submitRegister)
    await this.waitForNetworkIdle()
    
    return { name, url, description }
  }

  async waitForRepositoryReady(repoName, timeout = 120000) {
    const repoItem = `[data-testid="repository-item"][data-repo-name="${repoName}"]`
    const statusElement = `${repoItem} [data-testid="repo-status"]`
    
    await expect(this.page.locator(statusElement)).toHaveText('ready', { timeout })
  }

  async unregisterRepository(repoName) {
    const repoItem = `[data-testid="repository-item"][data-repo-name="${repoName}"]`
    const unregisterButton = `${repoItem} [data-testid="unregister-repo"]`
    
    await this.clickElement(unregisterButton)
    
    // Confirm unregistration if there's a confirmation dialog
    const confirmButton = this.page.locator('[data-testid="confirm-unregister"]')
    if (await confirmButton.isVisible({ timeout: 1000 })) {
      await confirmButton.click()
    }
    
    await this.waitForNetworkIdle()
  }

  async refreshRepositories() {
    await this.clickElement(this.selectors.refreshButton)
    await this.waitForNetworkIdle()
  }

  async expectRepositoryStatus(repoName, status) {
    const repoItem = `[data-testid="repository-item"][data-repo-name="${repoName}"]`
    const statusElement = `${repoItem} [data-testid="repo-status"]`
    await this.expectText(statusElement, status)
  }
}