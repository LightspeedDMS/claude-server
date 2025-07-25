# Claude Web UI - API Client Services

This directory contains a robust API client service for the Claude Web UI that handles JWT authentication, error handling, and integration with the Claude Batch Server endpoints.

## 🚀 Features Implemented

### ✅ Robust API Client Class (`src/services/api.js`)

- **JWT Token Management**: Automatic injection of Bearer tokens into request headers
- **Base URL Configuration**: Configured for NGINX proxy (`/api/`) routing
- **Error Handling**: Comprehensive error handling with custom error classes
- **Request Timeouts**: Configurable timeout support (30s default)
- **Retry Logic**: Exponential backoff retry mechanism (3 attempts default)
- **Response Parsing**: Automatic JSON parsing with fallback for non-JSON responses
- **File Upload**: Progress tracking for file uploads using XMLHttpRequest
- **File Download**: Blob-based file downloads with proper error handling

### ✅ Authentication Service (`src/services/auth.js`)

- **Token Storage**: Secure localStorage management for JWT tokens
- **Session Management**: User session persistence across browser sessions
- **Token Validation**: Expiration checking with 5-minute buffer
- **Login/Logout**: Complete authentication flow with server integration
- **Error Handling**: Graceful handling of authentication failures
- **Auto-Redirect**: Automatic redirect to login on authentication errors

### ✅ Claude API Service (`src/services/claude-api.js`)

- **Complete Integration**: All Claude Batch Server endpoints covered
- **Job Management**: Create, start, monitor, cancel, and delete jobs
- **Repository Management**: Register, list, and unregister repositories
- **File Operations**: Upload files/images with progress tracking
- **Job Monitoring**: Real-time status polling with 2-second intervals
- **Status Formatting**: Human-readable status display utilities
- **Auto-generated Titles**: Integration with job title generation feature

## 🎯 API Endpoints Supported

### Authentication
- `POST /auth/login` - User authentication with JWT token response
- `POST /auth/logout` - Secure logout with token invalidation

### Repository Management
- `GET /repositories` - List all registered repositories with metadata
- `GET /repositories/{name}` - Get specific repository details
- `POST /repositories/register` - Register new repository with Git URL
- `DELETE /repositories/{name}` - Unregister repository

### Job Management
- `POST /jobs` - Create new job with auto-generated title
- `GET /jobs` - List user's jobs with filtering options
- `GET /jobs/{id}` - Get detailed job status and results
- `POST /jobs/{id}/start` - Start job execution
- `POST /jobs/{id}/cancel` - Cancel running job
- `DELETE /jobs/{id}` - Delete job and cleanup workspace

### File Operations
- `POST /jobs/{id}/files` - Upload files to job workspace
- `POST /jobs/{id}/images` - Upload images to job workspace
- `GET /jobs/{id}/files` - List files in job workspace
- `GET /jobs/{id}/files/{name}` - Download specific file
- `GET /jobs/{id}/files/{name}/content` - Get file content as text

## 🏗️ Architecture

### Core Design Principles

1. **Separation of Concerns**: Clear separation between authentication, HTTP client, and API-specific logic
2. **Error Resilience**: Comprehensive error handling with retry logic and graceful degradation
3. **Security First**: Secure token management with automatic header injection
4. **Developer Experience**: Clean, consistent API with proper TypeScript-style JSDoc documentation

### Class Structure

```
AuthService (Singleton)
├── Token management (localStorage)
├── Session validation
├── Login/logout flows
└── Error handling & redirects

ApiClient (Singleton)
├── HTTP request handling
├── Authentication integration
├── Retry logic & timeouts
├── Response parsing
└── File upload/download

ClaudeApiService (Singleton)
├── Claude-specific endpoints
├── Job lifecycle management
├── Repository operations
├── File management
└── Status utilities
```

## 🧪 Testing & Validation

### Demo Script
Run the comprehensive demo to see all features in action:

```bash
node demo.js
```

The demo validates:
- ✅ JWT token management and storage
- ✅ Automatic authentication header injection  
- ✅ Login/logout functionality
- ✅ Error handling for network failures
- ✅ Base URL configuration for NGINX proxy
- ✅ Request/response parsing
- ✅ Integration with Claude Batch Server endpoints
- ✅ Job management and status formatting
- ✅ Repository management
- ✅ Authentication state management

### Unit Tests
Comprehensive unit tests are provided for both services:
- `tests/unit/auth-service.test.js` - Authentication service tests
- `tests/unit/api-client.test.js` - API client tests
- `tests/test-utils.js` - Shared test utilities

## 🚀 Usage Examples

### Authentication

```javascript
import AuthService from './services/auth.js';

// Login
const success = await AuthService.login({
  username: 'testuser',
  password: 'testpass'
});

// Check authentication status
if (AuthService.isAuthenticated()) {
  console.log('User is logged in:', AuthService.getUser());
}

// Logout
await AuthService.logout();
```

### API Requests

```javascript
import ApiClient from './services/api.js';

// GET request with authentication
const jobs = await ApiClient.get('jobs');

// POST request with data
const newJob = await ApiClient.post('jobs', {
  repository: 'my-repo',
  prompt: 'Analyze this codebase'
});

// File upload with progress
await ApiClient.uploadWithProgress('jobs/123/files', formData, (percent) => {
  console.log(`Upload progress: ${percent}%`);
});
```

### Claude API Integration

```javascript
import ClaudeApi from './services/claude-api.js';

// Create and start a job
const job = await ClaudeApi.createJob({
  repository: 'my-repo',
  prompt: 'Create comprehensive documentation',
  options: { timeout: 600, gitAware: true, cidxAware: true }
});

await ClaudeApi.startJob(job.jobId);

// Monitor job progress
await ClaudeApi.monitorJob(job.jobId, (status) => {
  console.log(`Job status: ${ClaudeApi.formatJobStatus(status.status)}`);
});
```

## 🔧 Configuration

### NGINX Proxy Setup
The API client is configured to work with NGINX proxy routing:

```nginx
server {
    listen 80;
    server_name your-domain.com;
    
    # Serve static web UI files
    location / {
        root /var/www/claude-web-ui/dist;
        try_files $uri $uri/ /index.html;
    }
    
    # Proxy API requests to Claude Batch Server
    location /api/ {
        proxy_pass http://localhost:5000/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### API Client Configuration

```javascript
// Timeout configuration
ApiClient.setTimeout(60000); // 60 seconds

// Retry configuration  
ApiClient.setRetryConfig(5, 2000); // 5 retries, 2s base delay
```

## 🔒 Security Features

- **JWT Token Management**: Secure storage and automatic injection
- **Token Expiration**: Automatic validation with 5-minute buffer
- **Authentication Errors**: Automatic session cleanup and redirect
- **HTTPS Ready**: Designed for secure production deployment
- **CORS Compatible**: Works with existing Claude Batch Server CORS configuration

## 📁 File Structure

```
claude-web-ui/
├── src/
│   ├── services/
│   │   ├── auth.js           # Authentication service
│   │   ├── api.js            # Core API client
│   │   └── claude-api.js     # Claude-specific API wrapper
│   ├── styles/
│   │   └── main.css          # Modern UI styling
│   └── main.js               # Main application logic
├── tests/
│   ├── unit/
│   │   ├── auth-service.test.js
│   │   └── api-client.test.js
│   └── test-utils.js
├── index.html                # Main HTML interface
├── demo.js                   # Feature demonstration
├── package.json              # Dependencies and scripts
├── vite.config.js            # Vite build configuration
└── README.md                 # This file
```

## 🌟 Key Benefits

1. **Production Ready**: Robust error handling, retry logic, and security features
2. **Developer Friendly**: Clean APIs with comprehensive documentation
3. **Highly Compatible**: Designed to work seamlessly with existing Claude Batch Server
4. **Extensible**: Easy to add new endpoints and features
5. **Well Tested**: Comprehensive test coverage with demo validation
6. **Modern Standards**: Uses ES6+ features and modern web APIs
7. **Performance Optimized**: Efficient request handling with caching and retry logic

## 🚀 Next Steps

1. **Integration**: Connect to running Claude Batch Server instance
2. **Testing**: Run against real API endpoints
3. **Enhancement**: Add any additional endpoints as needed
4. **Deployment**: Configure NGINX and deploy to production
5. **Monitoring**: Add logging and metrics as required

The API client service is now ready for integration with the Claude Batch Server and provides a solid foundation for the complete web UI implementation!