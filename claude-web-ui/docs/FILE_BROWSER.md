# File Browser Component

The File Browser component provides a comprehensive interface for browsing, previewing, and managing files within a job's workspace. It features a modern, responsive design with touch-friendly mobile support.

## Features

### 🗂️ Tree View Navigation
- Hierarchical directory structure display
- Collapsible sidebar with directory tree
- Breadcrumb navigation showing current path
- Click navigation between directories

### 🔍 File Operations
- **Preview**: Text files with syntax highlighting, images with proper sizing
- **Download**: Individual files or bulk download selected files
- **Search**: Real-time search across file names and paths
- **Sort**: Multiple sorting options (name, size, date, type)
- **Filter**: Search with instant filtering capabilities

### 📱 Mobile Responsive Design
- Touch-friendly interface for mobile devices
- Collapsible sidebar for narrow screens
- Swipe gestures for navigation
- Proper touch target sizes (44px minimum)
- Prevention of iOS zoom on form inputs

### 🎨 User Experience
- File type icons for better visual identification
- Loading states and error handling
- Empty state messaging
- Syntax highlighting for code files
- Image preview with proper scaling
- Keyboard accessibility support

## API Integration

The file browser integrates with the following API endpoints:

### GET /jobs/{jobId}/files
Lists files and directories in the workspace.

**Query Parameters:**
- `path` (optional): Directory path to list

**Response:**
```json
{
  "files": [
    {
      "name": "README.md",
      "path": "/workspace/README.md",
      "type": "file",
      "size": 1024,
      "lastModified": "2023-01-01T12:00:00Z"
    },
    {
      "name": "src",
      "path": "/workspace/src",
      "type": "directory",
      "size": null,
      "lastModified": "2023-01-01T12:00:00Z"
    }
  ]
}
```

### GET /jobs/{jobId}/files/content
Retrieves text content of a file for preview.

**Query Parameters:**
- `path` (required): File path to preview

**Response:** Plain text content of the file

### GET /jobs/{jobId}/files/download
Downloads a file from the workspace.

**Query Parameters:**
- `path` (required): File path to download

**Response:** Binary file content with appropriate headers

## Component Usage

### Basic Usage

```javascript
import { FileBrowserComponent } from './components/file-browser.js'

// Create file browser instance
const container = document.getElementById('file-browser-container')
const fileBrowser = new FileBrowserComponent(container, jobId, options)

// Cleanup when done
fileBrowser.destroy()
```

### Options

```javascript
const options = {
  onError: (error) => {
    console.error('File browser error:', error)
  },
  onFileSelect: (file) => {
    console.log('File selected:', file)
  }
}
```

### Integration with Job Details

The file browser is integrated into the Job Details component as a separate tab:

```javascript
import { JobDetailsComponent } from './components/job-details.js'

const jobDetails = new JobDetailsComponent(container, jobId, {
  onShowFiles: (jobId) => {
    // Switch to files tab
  }
})
```

## CSS Classes and Styling

### Main Container
- `.file-browser-container` - Main container with flexbox layout
- `.file-browser-header` - Header with breadcrumbs and actions
- `.file-browser-content` - Main content area with sidebar and file list

### Navigation
- `.breadcrumb-nav` - Breadcrumb navigation container
- `.breadcrumb-item` - Individual breadcrumb items
- `.breadcrumb-separator` - Arrow separators between breadcrumbs

### File Tree
- `.file-tree` - Sidebar tree container
- `.tree-node` - Individual tree nodes
- `.tree-item` - Clickable tree items
- `.tree-icon` - File/folder icons in tree

### File List
- `.file-list` - Main file list container
- `.file-item` - Individual file items
- `.file-icon` - File type icons
- `.file-name` - File name display
- `.file-actions` - Action buttons container

### File Preview
- `.file-preview-panel` - Preview panel container
- `.text-file-preview` - Text file preview wrapper
- `.code-preview` - Code syntax highlighting container
- `.preview-image` - Image preview display

### Responsive Breakpoints
- `768px`: Tablet layout adjustments
- `480px`: Mobile layout with stacked components
- Touch devices: Enhanced touch targets and gestures

## File Type Support

### Text Files
Supported for preview with syntax highlighting:
- Code: `.js`, `.ts`, `.jsx`, `.tsx`, `.py`, `.java`, `.cpp`, `.c`, `.html`, `.css`
- Config: `.json`, `.xml`, `.yaml`, `.yml`, `.env`, `.conf`
- Documentation: `.md`, `.txt`, `.readme`

### Image Files
Supported for image preview:
- `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.svg`, `.webp`

### Binary Files
Displayed with file type information and download option:
- Archives: `.zip`, `.tar`, `.gz`, `.rar`
- Other binary formats

## Accessibility Features

### Keyboard Navigation
- Tab navigation through all interactive elements
- Enter/Space activation of buttons
- Arrow key navigation in file list
- Escape to close search/preview

### Screen Reader Support
- Proper ARIA labels and roles
- Semantic HTML structure
- Descriptive button text
- Status announcements for loading/error states

### High Contrast Mode
- Enhanced borders and focus indicators
- Improved color contrast ratios
- Clear visual distinction between states

## Performance Optimizations

### Caching
- File preview content caching
- Lazy loading of directory contents
- Efficient re-rendering strategies

### Memory Management
- Automatic cleanup of blob URLs
- Preview cache size limits
- Event listener cleanup on destroy

### Network Efficiency
- Debounced search requests
- Conditional file loading
- Progressive enhancement approach

## Testing

### Unit Tests
Comprehensive test coverage for:
- Component initialization
- File list rendering
- Search and sorting functionality
- File actions and preview
- Error handling
- Mobile responsiveness

### E2E Tests
End-to-end testing for:
- Complete user workflows
- File download functionality
- Navigation between directories
- Mobile device compatibility
- Error state handling

## Browser Support

### Modern Browsers
- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

### Mobile Browsers
- iOS Safari 14+
- Chrome Mobile 90+
- Firefox Mobile 88+

### Features Requiring Polyfills
- Intersection Observer (for lazy loading)
- ResizeObserver (for responsive behavior)
- Clipboard API (for copy functionality)

## Security Considerations

### File Access Control
- Server-side authentication required
- Job-specific file access only
- No directory traversal attacks possible

### Content Security
- Safe HTML escaping for all user content
- Blob URL management with cleanup
- No execution of user-provided scripts

### Privacy
- No file content sent to external services
- Local preview generation only
- Secure download mechanisms

## Troubleshooting

### Common Issues

**Files not loading:**
- Check API endpoint availability
- Verify authentication status
- Confirm job ID is valid

**Preview not working:**
- Check file size limits (large files may fail)
- Verify file type is supported for preview
- Network connectivity issues

**Mobile layout issues:**
- Check viewport meta tag
- Verify CSS media queries
- Test touch event handling

### Debug Mode
Enable debug mode by setting `window.DEBUG_FILE_BROWSER = true` in browser console for detailed logging.

## Future Enhancements

### Planned Features
- Drag and drop file upload
- File editing capabilities
- Advanced search with filters
- Bulk operations (copy, move, delete)
- Version history for files
- Real-time file watching
- Integration with external storage services

### Performance Improvements
- Virtual scrolling for large directories
- Background file indexing
- Smart caching strategies
- Progressive loading

## Contributing

When contributing to the file browser component:

1. Follow existing code patterns and conventions
2. Add comprehensive tests for new features
3. Update documentation for API changes
4. Test across multiple devices and browsers
5. Ensure accessibility compliance
6. Consider performance implications

## License

This component is part of the Claude Web UI project and follows the same licensing terms.