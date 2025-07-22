# Claude Batch Server - API Reference

Quick reference for the Claude Batch Server REST API with Git + Cidx integration.

## Base URLs
- **Development**: http://localhost:5000
- **Docker**: http://localhost:8080
- **Docker HTTPS**: https://localhost:8443

## Authentication

All endpoints except `/auth/login` require JWT authentication:
```
Authorization: Bearer <jwt-token>
```

### POST /auth/login
```json
{
  "username": "your-username",
  "password": "your-password-or-hash"
}
```

Response:
```json
{
  "token": "jwt-token",
  "user": "username", 
  "expires": "2024-07-22T12:00:00Z"
}
```

## Repository Management

### GET /repositories
List registered repositories.

### POST /repositories/register
Register and clone Git repository.
```json
{
  "name": "repo-name",
  "gitUrl": "https://github.com/user/repo.git",
  "description": "Optional description"
}
```

### DELETE /repositories/{repoName}
Unregister repository and remove from disk.

## Job Management

### POST /jobs
Create job with Git + Cidx integration.
```json
{
  "prompt": "Your Claude Code prompt",
  "repository": "repo-name",
  "images": ["image1.png"],
  "options": {
    "timeout": 600,
    "gitAware": true,
    "cidxAware": true
  }
}
```

### POST /jobs/{jobId}/start
Start job execution with Git pull and Cidx indexing.

### GET /jobs/{jobId}
Get job status with Git and Cidx tracking.
```json
{
  "jobId": "uuid",
  "status": "completed",
  "gitStatus": "pulled", 
  "cidxStatus": "ready",
  "output": "...",
  "exitCode": 0
}
```

### DELETE /jobs/{jobId}
Terminate job and cleanup resources.

### GET /jobs
List all user jobs.

## File Operations

### POST /jobs/{jobId}/images
Upload image files (multipart/form-data).

### GET /jobs/{jobId}/files
List files in job workspace.

### GET /jobs/{jobId}/files/download?path=/path/to/file
Download file from workspace.

### GET /jobs/{jobId}/files/content?path=/path/to/file  
Get text file content as JSON.

## Status Values

### Job Status
- `created` - Job created, not started
- `queued` - Waiting for execution slot
- `git_pulling` - Updating repository
- `git_failed` - Git operation failed
- `cidx_indexing` - Building semantic index
- `cidx_ready` - Semantic search ready
- `running` - Claude Code executing
- `completed` - Finished successfully
- `failed` - Execution failed
- `timeout` - Exceeded timeout

### Git Status  
- `not_checked` - Git operations not started
- `checking` - Validating repository
- `pulled` - Successfully updated
- `failed` - Git operation failed
- `not_git_repo` - Not a Git repository

### Cidx Status
- `not_started` - Cidx not initiated
- `starting` - Starting containers
- `indexing` - Building semantic index
- `ready` - Available for queries
- `failed` - Cidx operation failed
- `stopped` - Containers stopped

## Example Workflow

```bash
# 1. Login
TOKEN=$(curl -s -X POST localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"user","password":"pass"}' | jq -r '.token')

# 2. Register repository
curl -X POST localhost:8080/repositories/register \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"my-repo","gitUrl":"https://github.com/user/repo.git"}'

# 3. Create job  
JOB_ID=$(curl -s -X POST localhost:8080/jobs \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "prompt":"Find all test files in this repository",
    "repository":"my-repo",
    "options":{"gitAware":true,"cidxAware":true,"timeout":600}
  }' | jq -r '.jobId')

# 4. Start job
curl -X POST localhost:8080/jobs/$JOB_ID/start \
  -H "Authorization: Bearer $TOKEN"

# 5. Monitor progress
curl localhost:8080/jobs/$JOB_ID \
  -H "Authorization: Bearer $TOKEN" | jq '{status,gitStatus,cidxStatus}'

# 6. Get results
curl localhost:8080/jobs/$JOB_ID \
  -H "Authorization: Bearer $TOKEN" | jq '.output'

# 7. Cleanup
curl -X DELETE localhost:8080/repositories/my-repo \
  -H "Authorization: Bearer $TOKEN"
```

## Error Responses

All endpoints return standard HTTP status codes:
- `400` - Bad Request (validation failed)
- `401` - Unauthorized (invalid/missing token)
- `403` - Forbidden (insufficient permissions)
- `404` - Not Found (resource doesn't exist)
- `409` - Conflict (resource already exists)
- `500` - Internal Server Error

Error response format:
```json
{
  "error": "Error description",
  "details": "Additional details if available"
}
```

## Git + Cidx Features

### Automatic Git Integration
- Repositories cloned once during registration
- Jobs get CoW clones for isolation
- Automatic git pull before Claude execution
- Real-time git operation status tracking

### Semantic Search with Cidx
- Per-job Docker container isolation
- Automatic semantic indexing after git pull
- Dynamic Claude prompts based on cidx availability
- Voyage-id embeddings for high-quality search
- Automatic cleanup after job completion

### System Prompt Integration
Claude receives context-aware prompts:
- **Cidx Ready**: Instructions to use `cidx query` for semantic search
- **Cidx Unavailable**: Fallback to traditional `grep`/`find`/`rg` tools
- **Service Status**: Real-time cidx service health information

For complete documentation, see [README.md](README.md).