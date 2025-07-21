# Claude Code Batch Automation Server - Alpha Epic

## Project Overview

A dockerized REST API server written in .NET Core that automates Claude Code execution in batch mode, supporting multi-user environments with proper user session isolation and authentication.

**License:** MIT

## Key Architecture Principles

- **User Session Isolation**: Each Claude Code session runs under the authenticated user's context
- **Subscription Awareness**: Claude Code login state follows the OS user account
- **Root Service Pattern**: Service runs as root but impersonates users for Claude Code execution
- **Dockerized Deployment**: Complete containerization for easy deployment

## Epic Components

### 1. Prerequisites Installation (`install.sh`)

**Objective**: Create idempotent installation script supporting Rocky Linux and Ubuntu

**Requirements**:
- Detect OS distribution (Rocky Linux 9.x / Ubuntu 22.04+)
- Install .NET Core SDK 8.0
- Install Docker and Docker Compose
- Install Claude Code CLI
- Configure system dependencies and CoW filesystem support
- Detect and configure optimal Copy-on-Write method
- Validate all installations including CoW functionality
- Support re-running without breaking existing setup

**CoW Configuration**:
- **Rocky Linux**: Verify XFS reflink support (already available)
- **Ubuntu 22.04+**: Verify ext4 reflink support or detect Btrfs
- **Fallback Detection**: Test CoW methods and configure optimal approach

**Key Features**:
- OS detection and distribution-specific package management
- Idempotent execution (safe to run multiple times)
- Dependency validation and verification
- Error handling and rollback capabilities

### 2. Dockerized REST Server (.NET Core)

**Objective**: Build a containerized API server for Claude Code automation

#### 2.1 Authentication System
- **Method**: OS passthrough authentication using username/password
- **Phase 1**: Shadow file validation for local system accounts
- **Phase 2**: PAM integration for enterprise authentication (LDAP/AD)
- **Security**: Secure credential handling, JWT session management
- **User Context**: Maintain authenticated user identity for all operations

#### 2.2 Core API Endpoints

##### Authentication Endpoints
```
POST /auth/login
- Body: { "username": "string", "password": "string" }
- Response: { "token": "jwt_token", "user": "username", "expires": "datetime" }

POST /auth/logout
- Headers: Authorization: Bearer <token>
- Response: { "success": true }
```

##### Job Management (Separated Create/Start Pattern)
```
POST /jobs
- Headers: Authorization: Bearer <token>
- Body: { 
    "prompt": "string", 
    "repository": "repo-name",
    "images": ["image1.png", "image2.jpg"],
    "options": { "timeout": 300 }
  }
- Response: { "jobId": "uuid", "status": "created", "user": "username", "cowPath": "/workspace/jobs/{jobId}" }

POST /jobs/{jobId}/images
- Headers: Authorization: Bearer <token>
- Content-Type: multipart/form-data
- Body: image file with filename
- Response: { "filename": "stored_name.ext", "path": "/workspace/jobs/{jobId}/images/" }

POST /jobs/{jobId}/start
- Headers: Authorization: Bearer <token>
- Response: { "jobId": "uuid", "status": "queued|running", "queuePosition": int }

GET /jobs/{jobId}
- Headers: Authorization: Bearer <token>
- Response: { 
    "jobId": "uuid", 
    "status": "created|queued|running|completed|failed|timeout", 
    "output": "string", 
    "exitCode": int,
    "cowPath": "/workspace/jobs/{jobId}",
    "queuePosition": int
  }

DELETE /jobs/{jobId}
- Headers: Authorization: Bearer <token>
- Response: { "success": true, "terminated": true, "cowRemoved": true }

GET /jobs
- Headers: Authorization: Bearer <token>
- Response: [{ "jobId": "uuid", "user": "username", "status": "string", "started": "datetime", "repository": "string" }]
```

##### Repository and File Management
```
GET /repositories
- Headers: Authorization: Bearer <token>
- Response: [{ "name": "repo-name", "path": "/repos/repo-name", "description": "string" }]

GET /jobs/{jobId}/files
- Headers: Authorization: Bearer <token>
- Query: ?path=/optional/subpath
- Response: [{ "name": "filename", "type": "file|directory", "path": "relative/path", "size": int, "modified": "datetime" }]

GET /jobs/{jobId}/files/download
- Headers: Authorization: Bearer <token>
- Query: ?path=/path/to/file
- Response: File download with proper Content-Type headers

GET /jobs/{jobId}/files/content
- Headers: Authorization: Bearer <token>
- Query: ?path=/path/to/file
- Response: { "content": "file_content", "encoding": "utf8|base64" }
```

##### Administrative Endpoints
```
GET /admin/sessions
- Headers: Authorization: Bearer <admin_token>
- Response: [{ "sessionId": "uuid", "user": "username", "status": "string", "started": "datetime", "command": "string" }]
```

#### 2.3 Copy-on-Write Repository Management

**Critical Architecture**: Each job operates on a CoW clone of the source repository

**Implementation Strategy**:
- Use OS-level CoW commands (`cp --reflink=always` on Btrfs/XFS, `btrfs subvolume snapshot` on Btrfs)
- Instant repository cloning without storage duplication
- Isolated workspace per job: `/workspace/jobs/{jobId}/`
- Pre-configured `.claude/` directory in each repository
- Configurable pre-execution commands per repository

**Repository Structure**:
```
/workspace/
├── repos/                    # Master repository storage
│   ├── repo1/
│   │   ├── .claude/         # Repository-specific Claude config
│   │   ├── .precommands     # Optional pre-execution scripts
│   │   └── [repo content]
│   └── repo2/
└── jobs/                    # Job workspaces (CoW clones)
    ├── {jobId1}/
    │   ├── images/          # Uploaded images for this job
    │   ├── .claude/         # Inherited Claude config
    │   └── [cloned content]
    └── {jobId2}/
```

#### 2.4 User Impersonation Architecture

**Critical Requirement**: Service runs as root but executes Claude Code as authenticated user

**Implementation Strategy**:
- Use `setuid`/`setgid` system calls for user impersonation
- Maintain user environment variables and home directory context
- Preserve Claude Code authentication state per user
- Handle file permissions and access controls properly
- Mount persistent user home directories for Claude login state

**Security Considerations**:
- Validate user permissions before impersonation
- Sanitize command inputs to prevent injection
- Log all user impersonation events
- Implement configurable session timeouts (default: 1 day)

#### 2.5 Queue Management System

**Configuration-Driven Concurrency**:
- Configurable maximum concurrent jobs (global limit)
- Queue-based job scheduling with FIFO ordering
- Real-time queue position tracking
- Job priority support (future enhancement)

**Features**:
- Process lifecycle management (create, queue, start, monitor, terminate)
- Output capture and streaming
- Resource usage monitoring
- Automatic cleanup on timeout (1 day default)
- CoW workspace removal on job completion/timeout

**Architecture**:
- Background service for job queue management
- In-memory job store with optional persistence
- Automatic CoW cleanup and orphan prevention
- Configurable timeout handling with workspace removal

### 3. Service Deployment (`install.sh` extension)

**Objective**: Extend installation script to deploy and configure the service

**Requirements**:
- Build and deploy Docker containers
- Configure systemd service for auto-start
- Set up reverse proxy (nginx) for HTTPS
- Configure logging and monitoring
- Create service user accounts
- Set up backup and recovery procedures

**Configuration Management**:
- Environment-based configuration
- Secrets management for authentication
- Service discovery and health checks
- Rolling updates and deployment strategies

## Technical Specifications

### Technology Stack
- **Backend**: .NET Core 8.0, ASP.NET Core Web API
- **Authentication**: JWT tokens, shadow file validation (Phase 1), PAM integration (Phase 2)
- **Containerization**: Docker, Docker Compose with privileged mode
- **Storage**: Btrfs/XFS with CoW support, persistent volume mounts
- **Process Management**: System.Diagnostics.Process with user impersonation
- **Queue Management**: In-memory queue with configurable concurrency limits
- **Logging**: Serilog with structured logging
- **Testing**: xUnit, integration tests with TestContainers

### Security Requirements
- HTTPS-only communication
- JWT token expiration and refresh
- Input validation and sanitization
- Rate limiting and DoS protection
- Audit logging for all operations
- Secure credential storage

### Performance Requirements
- Support 10+ concurrent Claude Code sessions
- Session startup time < 5 seconds
- API response time < 500ms
- Memory usage monitoring and limits
- Graceful degradation under load

## Implementation Questions & Considerations

### Copy-on-Write Strategy
**Cross-Platform Support**: Rocky Linux (XFS) and Ubuntu (ext4/Btrfs)

**Implementation Strategy**:
1. **XFS Reflinks** (Rocky Linux): Use `cp --reflink=always` for instant CoW copies
   - ✅ Already enabled on Rocky Linux development system
   - ✅ No additional filesystem installation required
2. **ext4 Reflinks** (Ubuntu 22.04+): Use `cp --reflink=always` for supported ext4
   - ✅ Ubuntu 22.04+ has ext4 with reflink support
   - ✅ No filesystem changes needed on modern Ubuntu
3. **Btrfs Snapshots** (Ubuntu Alternative): Use `btrfs subvolume snapshot` if available
   - Option for Ubuntu systems with Btrfs filesystem
4. **Fallback**: Use `rsync` with hardlinks for older/unsupported filesystems
5. **Auto-Detection**: Detect filesystem capabilities at startup and choose optimal method

**Ubuntu CoW Support**:
- **Ubuntu 22.04+**: ext4 with reflink support (default)
- **Ubuntu with Btrfs**: Full subvolume snapshot support
- **Older Ubuntu**: Fallback to hardlink-based copying

### Authentication Implementation Strategy

**Phase 1: Shadow File Validation** (MVP):
- **Implementation**: Read `/etc/shadow`, verify password hashes using crypt()
- **Pros**: Direct, lightweight, no external dependencies, fast development
- **Cons**: Only local users, no advanced auth features
- **Use Case**: Initial deployment, local user management, development/testing

**Phase 2: PAM Integration** (Enterprise):
- **Implementation**: Use `pam_authenticate()` C API via P/Invoke
- **Pros**: Supports LDAP, Active Directory, multi-factor auth, system policies
- **Cons**: More complex, requires PAM modules in container
- **Use Case**: Enterprise environments, existing directory services

**Migration Path**: Shadow file authentication provides foundation, PAM can be added as alternative auth provider without breaking existing functionality.

### User Impersonation in Containers
**Question**: How to handle user impersonation from containerized root service?

**Proposed Solution**:
1. **Privileged Container**: Run with `--privileged` or specific capabilities
2. **User Namespace Mapping**: Map container users to host users
3. **Volume Mounts**: Mount `/etc/passwd`, `/etc/shadow`, `/home` as volumes
4. **Process Execution**: Use `setuid()`/`setgid()` before executing Claude Code
5. **Home Directory Access**: Ensure user home directories are accessible

### Job Queue and Resource Management
**Configuration Parameters**:
- `maxConcurrentJobs`: Global limit (default: 5)
- `jobTimeoutHours`: Auto-cleanup timeout (default: 24)
- `queueTimeout`: Max time in queue before failure (default: 1 hour)
- `diskSpaceThreshold`: Minimum free space for new jobs (default: 10GB)

### Repository and Workspace Management
**Structure Design**:
```
/workspace/
├── repos/                   # Source repositories (read-only)
├── jobs/                    # Active job workspaces (CoW clones)
├── completed/               # Archived completed jobs (optional)
└── uploads/                 # Temporary image upload staging
```

**Cleanup Strategy**:
- Jobs auto-removed after timeout (configurable: 1 day default)
- Failed jobs retained for debugging (configurable retention period)
- Disk space monitoring with automatic cleanup of oldest jobs

## Acceptance Criteria

### Phase 1: Basic Functionality (Shadow File Auth)
- [ ] Install script works on Rocky Linux 9 and Ubuntu 22.04+
- [ ] REST API with shadow file authentication and JWT tokens
- [ ] Job creation with prompt and repository selection
- [ ] Image upload system with per-job isolation
- [ ] Copy-on-Write repository cloning system
- [ ] Job queue with configurable concurrency limits
- [ ] Claude Code execution with user impersonation
- [ ] Job monitoring and output capture
- [ ] File download/access from job workspaces
- [ ] Automatic job cleanup with timeouts
- [ ] Basic logging and error handling

### Phase 2: Production Ready + PAM Integration
- [ ] PAM integration for enterprise authentication
- [ ] LDAP/Active Directory support
- [ ] HTTPS with proper certificate management
- [ ] Job persistence across service restarts
- [ ] Comprehensive error handling and recovery
- [ ] Performance monitoring and metrics
- [ ] Security audit and penetration testing
- [ ] Complete API documentation

### Phase 3: Advanced Features
- [ ] WebSocket streaming for real-time output
- [ ] Multi-node deployment and load balancing
- [ ] Advanced job prioritization and scheduling
- [ ] Multi-factor authentication support
- [ ] Backup and disaster recovery procedures
- [ ] Job result archiving and retention policies

## Risks and Mitigation

### Security Risks
- **Root privilege escalation**: Minimize attack surface with Linux capabilities
- **Command injection**: Strict input validation and sanitization
- **Authentication bypass**: Implement proper JWT validation and expiration

### Operational Risks
- **Resource exhaustion**: Implement quotas and monitoring
- **Session orphaning**: Proper cleanup and recovery procedures
- **Service availability**: Health checks and auto-restart mechanisms

## Next Steps

1. **Environment Setup**: Create development environment with Docker
2. **Prototype Development**: Build basic REST API with authentication
3. **User Impersonation POC**: Validate user switching mechanism
4. **Claude Code Integration**: Test batch mode execution and output capture
5. **Testing Framework**: Implement comprehensive test suite
6. **Documentation**: Create deployment and API documentation

---

**Note**: This epic focuses on the core functionality. Additional features like web UI, advanced monitoring, and enterprise integrations will be addressed in future epics.