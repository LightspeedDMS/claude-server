# Claude Batch Server - Enhanced File Manager API Documentation

## Overview

The Claude Batch Server now includes an enhanced file manager system with advanced filtering, file browsing, and download capabilities. This document describes the enhanced API endpoints and their usage.

## Enhanced File Management Endpoints

### GET /jobs/{jobId}/files

Browse files and directories in a job's workspace with advanced filtering capabilities.

**Parameters:**
- `jobId` (path, required): The GUID of the job
- `path` (query, optional): Subdirectory path to browse
- `mask` (query, optional): File filter mask (e.g., "*.js", "*.ts,*.json")
- `type` (query, optional): Filter by type ("files" or "directories")
- `depth` (query, optional): Maximum directory traversal depth (integer ≥ 1)

**Request Example:**
```
GET /jobs/550e8400-e29b-41d4-a716-446655440000/files?mask=*.js&type=files&depth=2
Authorization: Bearer {jwt-token}
```

**Response Example:**
```json
[
  {
    "name": "index.js",
    "type": "file",
    "path": "src/index.js",
    "size": 1234,
    "modified": "2024-07-24T20:30:00Z"
  },
  {
    "name": "components",
    "type": "directory",
    "path": "src/components",
    "size": 0,
    "modified": "2024-07-24T19:45:00Z"
  }
]
```

**HTTP Status Codes:**
- `200 OK`: Success
- `400 Bad Request`: Invalid parameters (e.g., invalid mask, negative depth)
- `401 Unauthorized`: Authentication required
- `403 Forbidden`: Access denied
- `404 Not Found`: Job or path not found

### GET /jobs/{jobId}/files/content

Retrieve the content of a specific file for viewing.

**Parameters:**
- `jobId` (path, required): The GUID of the job
- `path` (query, required): File path relative to job workspace

**Request Example:**
```
GET /jobs/550e8400-e29b-41d4-a716-446655440000/files/content?path=src/index.js
Authorization: Bearer {jwt-token}
```

**Response Example:**
```json
{
  "content": "console.log('Hello World');\nmodule.exports = { app: 'test' };",
  "encoding": "utf8"
}
```

### GET /jobs/{jobId}/files/download

Download a file from the job workspace.

**Parameters:**
- `jobId` (path, required): The GUID of the job
- `path` (query, required): File path relative to job workspace

**Request Example:**
```
GET /jobs/550e8400-e29b-41d4-a716-446655440000/files/download?path=dist/bundle.js
Authorization: Bearer {jwt-token}
```

**Response:**
- Returns the file as a binary stream with appropriate Content-Type header
- Content-Disposition header set for proper file naming

## File Filtering Examples

### Filter by File Extension
```
GET /jobs/{jobId}/files?mask=*.js
```
Returns only JavaScript files.

### Multiple File Extensions
```
GET /jobs/{jobId}/files?mask=*.js,*.ts,*.json
```
Returns JavaScript, TypeScript, and JSON files.

### Files Only (No Directories)
```
GET /jobs/{jobId}/files?type=files
```
Returns only files, excludes directories.

### Directories Only
```
GET /jobs/{jobId}/files?type=directories
```
Returns only directories, excludes files.

### Limit Directory Depth
```
GET /jobs/{jobId}/files?depth=1
```
Returns only first-level items, no deep traversal.

### Combined Filters
```
GET /jobs/{jobId}/files?mask=*.js&type=files&depth=2&path=src
```
Returns JavaScript files only, maximum 2 levels deep, within the src directory.

## Security Features

### Path Traversal Protection
- All file masks are validated to prevent dangerous characters
- File paths are validated to prevent directory traversal attacks
- Mask patterns cannot contain "../", "/", or "\"

### Authentication
- All endpoints require JWT authentication
- Tokens must be included in the Authorization header as "Bearer {token}"
- Users can only access files from their own jobs

## Error Handling

### Common Error Responses

**400 Bad Request - Invalid File Mask:**
```json
{
  "error": "Invalid file mask contains dangerous characters"
}
```

**400 Bad Request - Invalid Type Parameter:**
```json
{
  "error": "Type parameter must be 'files' or 'directories'"
}
```

**400 Bad Request - Invalid Depth:**
```json
{
  "error": "Depth parameter must be greater than 0"
}
```

**404 Not Found - Path Not Found:**
```json
{
  "error": "Path not found"
}
```

**404 Not Found - Job Not Found:**
```json
{
  "error": "Job not found"
}
```

## File Manager UI

### Web Interface

The enhanced file manager provides a modern web interface accessible at the server root path (`/`). Features include:

- **Two-Panel Layout**: Directory tree on the left, file list on the right
- **Advanced Filtering**: File mask input, type filters, depth limiting
- **View Modes**: List view and grid view for files
- **Interactive Features**: Resizable panels, breadcrumb navigation
- **File Operations**: View file content, download files
- **Responsive Design**: Works on desktop and mobile devices

### Authentication Flow

1. User visits the file manager interface
2. If not authenticated, login prompt appears
3. User enters credentials via the login API (`POST /auth/login`)
4. JWT token is stored in localStorage
5. All subsequent API calls include the token
6. Token expiration is handled gracefully with re-authentication prompts

## Client Integration

### JavaScript API Client

The file manager includes a JavaScript API client that can be used for custom integrations:

```javascript
// Initialize file manager
const fileManager = new JobFileManager();

// Load jobs
await fileManager.loadJobs();

// Select a job
await fileManager.selectJob(jobId);

// Apply filters
fileManager.filters = {
    mask: '*.js,*.ts',
    type: 'files',
    depth: 2
};
await fileManager.loadFiles();

// View a file
await fileManager.viewFile(filePath, fileName);

// Download a file  
await fileManager.downloadFile(filePath, fileName);
```

## Performance Considerations

- File listings are optimized for large directory structures
- Depth limiting prevents excessive recursion
- File type detection is efficient and cached
- Binary file downloads use streaming for large files
- Pagination may be added for very large directories

## Browser Compatibility

- Modern browsers supporting ES6+ features
- Fetch API support required
- LocalStorage support for authentication tokens
- File download uses Blob API for cross-browser compatibility

## File Upload System

### POST /jobs/{jobId}/files

Upload files to a job's staging area with support for multiple files and placeholder replacement.

**Request Example:**
```bash
curl -X POST http://localhost:5000/jobs/{jobId}/files \
  -H "Authorization: Bearer {jwt-token}" \
  -F "files=@screenshot.png" \
  -F "files=@data.csv" \
  -F "files=@document.pdf"
```

**Response Example:**
```json
[
  {
    "filename": "screenshot.png",
    "path": "/staging/screenshot.png",
    "serverPath": "/full/server/path/screenshot_abc123.png",
    "fileType": "image/png",
    "fileSize": 12345,
    "overwritten": false
  },
  {
    "filename": "data.csv",
    "path": "/staging/data.csv", 
    "serverPath": "/full/server/path/data_def456.csv",
    "fileType": "text/csv",
    "fileSize": 67890,
    "overwritten": false
  }
]
```

### Staging Area Workflow

1. **File Upload**: Files are uploaded to a staging area with hash-based naming to prevent conflicts
2. **Job Execution**: When job starts, staged files are copied to the job workspace
3. **Filename Cleanup**: Hash suffixes are removed when copying to workspace 
4. **Placeholder Replacement**: `{{filename}}` placeholders in prompts are replaced with actual uploaded filenames
5. **Cleanup**: Staging area is cleaned up after successful copy

### Placeholder Support

Use `{{filename}}` in job prompts to reference uploaded files:

```json
{
  "prompt": "Analyze the image {{screenshot.png}} and compare it with the data in {{data.csv}}",
  "repository": "my-repo",
  "images": ["screenshot.png", "data.csv"]
}
```

## Future Enhancements

- File editing within the interface
- Advanced search with regex support
- File history and versioning
- Bulk operations (zip download, multi-select)
- Real-time file system watching
- Collaborative features for team access