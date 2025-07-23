# Claude Code Batch Automation Server

A dockerized REST API server that automates Claude Code execution in batch mode, supporting multi-user environments with proper user session isolation and authentication.

## Features

- **Multi-user Support**: Each Claude Code session runs under the authenticated user's context
- **Copy-on-Write Repositories**: Instant repository cloning using filesystem CoW features
- **Job Queue Management**: Configurable concurrency limits with queue-based scheduling
- **File Upload Support**: Per-job file isolation for Claude Code prompts (any file type)
- **Image Analysis**: Complete image upload → Claude analysis → detailed results pipeline
- **JWT Authentication**: Production-ready authentication with shadow file validation
- **Cross-Platform**: Supports Rocky Linux (XFS) and Ubuntu (ext4/Btrfs)
- **MIT Licensed**: Open source with permissive licensing

## Architecture

- **Authentication**: Shadow file validation (Phase 1), PAM integration (Phase 2)
- **Isolation**: Copy-on-Write repository clones per job
- **User Impersonation**: Service runs as root but executes Claude Code as authenticated user
- **Queue System**: Configurable concurrent job limits with automatic cleanup

## Quick Start

```bash
# Install prerequisites
./install.sh

# Start the service
docker-compose up -d
```

## API Endpoints

### Authentication
- `POST /auth/login` - User authentication (sync)
- `POST /auth/logout` - Session termination (sync)

### Repository Management
- `POST /repositories/register` - Register repository (async)
- `GET /repositories` - List repositories (sync)
- `GET /repositories/{name}` - Get repository details with Git metadata (sync)
- `DELETE /repositories/{name}` - Unregister repository (sync)

### Job Management
- `POST /jobs` - Create new job with prompt and optional files (sync)
- `POST /jobs/{id}/files` - Upload multiple files with filename preservation (sync) - **UPDATED FROM /images**
- `POST /jobs/{id}/start` - Start job execution with template substitution (async)
- `GET /jobs/{id}` - Get job status and output (sync)
- `POST /jobs/{id}/cancel` - Cancel running job (sync)
- `DELETE /jobs/{id}` - Terminate job (sync)

### File Access
- `GET /jobs/{id}/files` - List job workspace files (sync)
- `GET /jobs/{id}/files/download` - Download files from job workspace (sync)

## Documentation

- [Development Roadmap](backlog/alpha.md) - Detailed requirements and implementation plan
- [HTTPS Setup Guide](claude-batch-server/docs/HTTPS-SETUP.md) - Complete SSL/TLS configuration

## Development

See [backlog/alpha.md](backlog/alpha.md) for detailed requirements and implementation plan.

## License

MIT License - see LICENSE file for details.