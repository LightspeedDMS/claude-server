import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { FileBrowserComponent } from '../../src/components/file-browser.js'
import apiClient from '../../src/services/api.js'

// Mock the API client
vi.mock('../../src/services/api.js', () => ({
  default: {
    getJobDirectories: vi.fn(),
    getJobFiles: vi.fn(),
    getJobFileContent: vi.fn(),
    downloadJobFileBlob: vi.fn()
  }
}))

describe('FileBrowserComponent', () => {
  let container
  let fileBrowser
  const mockJobId = 'test-job-123'

  const mockFiles = [
    {
      name: 'README.md',
      path: '/workspace/README.md',
      type: 'file',
      size: 1024,
      lastModified: '2023-01-01T12:00:00Z'
    },
    {
      name: 'src',
      path: '/workspace/src',
      type: 'directory',
      size: null,
      lastModified: '2023-01-01T12:00:00Z'
    },
    {
      name: 'package.json',
      path: '/workspace/package.json',
      type: 'file',
      size: 512,
      lastModified: '2023-01-01T11:00:00Z'
    }
  ]

  beforeEach(() => {
    container = document.createElement('div')
    document.body.appendChild(container)
    
    // Reset mocks
    vi.clearAllMocks()
    
    // Setup default mock responses
    apiClient.getJobDirectories.mockResolvedValue([])
    apiClient.getJobFiles.mockResolvedValue(mockFiles.filter(f => f.type === 'file'))
  })

  afterEach(() => {
    if (fileBrowser) {
      fileBrowser.destroy()
      fileBrowser = null
    }
    if (container.parentNode) {
      document.body.removeChild(container)
    }
  })

  describe('Initialization', () => {
    it('should create a file browser instance', () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      expect(fileBrowser).toBeDefined()
      expect(fileBrowser.jobId).toBe(mockJobId)
      expect(fileBrowser.currentPath).toBe('')
    })

    it('should render the main structure', async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      
      // Wait for initialization
      await new Promise(resolve => setTimeout(resolve, 100))
      
      expect(container.querySelector('[data-testid="file-browser"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="breadcrumb-nav"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-search"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-tree"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-list"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-preview"]')).toBeTruthy()
    })

    it('should load files on initialization', async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      
      // Wait for initialization
      await new Promise(resolve => setTimeout(resolve, 100))
      
      expect(apiClient.getJobDirectories).toHaveBeenCalledWith(mockJobId, '')
      expect(apiClient.getJobFiles).toHaveBeenCalledWith(mockJobId, '')
    })
  })

  describe('File List Rendering', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should render file items', () => {
      const fileItems = container.querySelectorAll('[data-testid="file-item"]')
      expect(fileItems).toHaveLength(3)
    })

    it('should display file names', () => {
      const fileNames = Array.from(container.querySelectorAll('[data-testid="file-name"]'))
        .map(el => el.textContent)
      
      expect(fileNames).toContain('README.md')
      expect(fileNames).toContain('src')
      expect(fileNames).toContain('package.json')
    })

    it('should show directories first', () => {
      const fileItems = container.querySelectorAll('[data-testid="file-item"]')
      const firstItem = fileItems[0]
      expect(firstItem.getAttribute('data-type')).toBe('directory')
    })
  })

  describe('Search Functionality', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should filter files based on search term', () => {
      const searchInput = container.querySelector('[data-testid="file-search"]')
      
      // Simulate typing 'README'
      searchInput.value = 'README'
      searchInput.dispatchEvent(new Event('input'))
      
      const visibleFiles = container.querySelectorAll('[data-testid="file-item"]')
      expect(visibleFiles).toHaveLength(1)
      expect(visibleFiles[0].querySelector('[data-testid="file-name"]').textContent).toBe('README.md')
    })

    it('should show clear search button when searching', () => {
      const searchInput = container.querySelector('[data-testid="file-search"]')
      const clearButton = container.querySelector('.search-clear')
      
      expect(clearButton.style.display).toBe('')
      
      searchInput.value = 'test'
      searchInput.dispatchEvent(new Event('input'))
      
      expect(clearButton.style.display).toBe('block')
    })

    it('should clear search when clear button is clicked', () => {
      const searchInput = container.querySelector('[data-testid="file-search"]')
      const clearButton = container.querySelector('.search-clear')
      
      searchInput.value = 'test'
      searchInput.dispatchEvent(new Event('input'))
      
      clearButton.click()
      
      expect(searchInput.value).toBe('')
      const visibleFiles = container.querySelectorAll('[data-testid="file-item"]')
      expect(visibleFiles).toHaveLength(3)
    })
  })

  describe('Sorting Functionality', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should sort files by name ascending by default', () => {
      const fileNames = Array.from(container.querySelectorAll('[data-testid="file-name"]'))
        .map(el => el.textContent)
      
      // Directories first, then files alphabetically
      expect(fileNames).toEqual(['src', 'README.md', 'package.json'])
    })

    it('should sort files by size when selected', () => {
      const sortSelect = container.querySelector('[data-testid="sort-select"]')
      
      sortSelect.value = 'size-desc'
      sortSelect.dispatchEvent(new Event('change'))
      
      const fileNames = Array.from(container.querySelectorAll('[data-testid="file-name"]'))
        .map(el => el.textContent)
      
      // Directories first, then files by size (descending)
      expect(fileNames).toEqual(['src', 'README.md', 'package.json'])
    })
  })

  describe('File Actions', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should show preview button for files', () => {
      const fileItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.getAttribute('data-type') === 'file')
      
      const previewButton = fileItem.querySelector('[data-testid="preview-file"]')
      expect(previewButton).toBeTruthy()
    })

    it('should show download button for files', () => {
      const fileItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.getAttribute('data-type') === 'file')
      
      const downloadButton = fileItem.querySelector('[data-testid="download-file"]')
      expect(downloadButton).toBeTruthy()
    })

    it('should not show file action buttons for directories', () => {
      const directoryItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.getAttribute('data-type') === 'directory')
      
      const previewButton = directoryItem.querySelector('[data-testid="preview-file"]')
      const downloadButton = directoryItem.querySelector('[data-testid="download-file"]')
      
      expect(previewButton).toBeFalsy()
      expect(downloadButton).toBeFalsy()
    })
  })

  describe('File Preview', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should show placeholder when no file is selected', () => {
      const preview = container.querySelector('[data-testid="file-preview"]')
      expect(preview.querySelector('.preview-placeholder')).toBeTruthy()
    })

    it('should preview text files', async () => {
      const mockContent = '# Test README\nThis is a test file.'
      apiClient.getJobFileContent.mockResolvedValue(mockContent)
      
      const readmeItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.querySelector('[data-testid="file-name"]').textContent === 'README.md')
      
      const previewButton = readmeItem.querySelector('[data-testid="preview-file"]')
      previewButton.click()
      
      // Wait for preview to load
      await new Promise(resolve => setTimeout(resolve, 100))
      
      expect(apiClient.getJobFileContent).toHaveBeenCalledWith(mockJobId, '/workspace/README.md')
    })

    it('should handle preview errors gracefully', async () => {
      apiClient.getJobFileContent.mockRejectedValue(new Error('File not found'))
      
      const readmeItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.querySelector('[data-testid="file-name"]').textContent === 'README.md')
      
      const previewButton = readmeItem.querySelector('[data-testid="preview-file"]')
      previewButton.click()
      
      // Wait for error to be displayed
      await new Promise(resolve => setTimeout(resolve, 100))
      
      const preview = container.querySelector('[data-testid="file-preview"]')
      expect(preview.querySelector('.preview-error')).toBeTruthy()
    })
  })

  describe('File Download', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should download files when download button is clicked', async () => {
      const mockBlob = new Blob(['test content'], { type: 'text/plain' })
      apiClient.downloadJobFileBlob.mockResolvedValue({
        blob: mockBlob,
        filename: 'README.md',
        contentType: 'text/plain'
      })
      
      // Mock URL.createObjectURL and appendChild
      global.URL.createObjectURL = vi.fn(() => 'mock-url')
      global.URL.revokeObjectURL = vi.fn()
      const mockAppendChild = vi.spyOn(document.body, 'appendChild').mockImplementation(() => {})
      const mockRemoveChild = vi.spyOn(document.body, 'removeChild').mockImplementation(() => {})
      
      const readmeItem = Array.from(container.querySelectorAll('[data-testid="file-item"]'))
        .find(item => item.querySelector('[data-testid="file-name"]').textContent === 'README.md')
      
      const downloadButton = readmeItem.querySelector('[data-testid="download-file"]')
      downloadButton.click()
      
      // Wait for download to process
      await new Promise(resolve => setTimeout(resolve, 100))
      
      expect(apiClient.downloadJobFileBlob).toHaveBeenCalledWith(mockJobId, '/workspace/README.md')
      expect(global.URL.createObjectURL).toHaveBeenCalledWith(mockBlob)
      
      // Cleanup
      mockAppendChild.mockRestore()
      mockRemoveChild.mockRestore()
    })
  })

  describe('Bulk Operations', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should enable download selected button when files are selected', async () => {
      const downloadSelectedButton = container.querySelector('[data-testid="download-selected"]')
      expect(downloadSelectedButton.disabled).toBe(true)
      
      const checkbox = container.querySelector('.file-checkbox')
      checkbox.checked = true
      
      // Manually call the update method since event delegation might not work in tests
      fileBrowser.updateSelectedActions()
      
      expect(downloadSelectedButton.disabled).toBe(false)
    })

    it('should select all files when select all is clicked', () => {
      const selectAllButton = container.querySelector('[data-testid="select-all"]')
      selectAllButton.click()
      
      const checkboxes = container.querySelectorAll('.file-checkbox')
      checkboxes.forEach(checkbox => {
        expect(checkbox.checked).toBe(true)
      })
    })
  })

  describe('Responsive Design', () => {
    beforeEach(async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
    })

    it('should have proper data-testid attributes for E2E testing', () => {
      expect(container.querySelector('[data-testid="file-browser"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="breadcrumb-nav"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-search"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="refresh-files"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="sort-select"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-tree"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-list"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="select-all"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="download-selected"]')).toBeTruthy()
      expect(container.querySelector('[data-testid="file-preview"]')).toBeTruthy()
    })
  })

  describe('Error Handling', () => {
    it('should handle API errors gracefully', async () => {
      apiClient.getJobFiles.mockRejectedValue(new Error('Network error'))
      apiClient.getJobDirectories.mockRejectedValue(new Error('Network error'))
      
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      
      // Wait for error to be displayed
      await new Promise(resolve => setTimeout(resolve, 100))
      
      const errorState = container.querySelector('[data-testid="error-state"]')
      expect(errorState).toBeTruthy()
    })

    it('should show empty state when no files are found', async () => {
      apiClient.getJobFiles.mockResolvedValue([])
      apiClient.getJobDirectories.mockResolvedValue([])
      
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      
      // Wait for empty state to be displayed
      await new Promise(resolve => setTimeout(resolve, 100))
      
      const emptyState = container.querySelector('[data-testid="empty-files"]')
      expect(emptyState).toBeTruthy()
    })
  })

  describe('Memory Management', () => {
    it('should cleanup resources on destroy', async () => {
      fileBrowser = new FileBrowserComponent(container, mockJobId)
      await new Promise(resolve => setTimeout(resolve, 100))
      
      // Mock URL.revokeObjectURL
      global.URL.revokeObjectURL = vi.fn()
      
      fileBrowser.destroy()
      
      expect(fileBrowser.filePreviewCache.size).toBe(0)
    })
  })
})