# Claude Code Batch Automation Server

A dockerized REST API server that automates Claude Code execution in batch mode, supporting multi-user environments with proper user session isolation and authentication.

## Features

- **Multi-user Support**: Each Claude Code session runs under the authenticated user's context
- **Copy-on-Write Repositories**: Instant repository cloning using filesystem CoW features
- **Job Queue Management**: Configurable concurrency limits with queue-based scheduling
- **Image Upload Support**: Per-job image isolation for Claude Code prompts
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
- `POST /auth/login` - User authentication
- `POST /auth/logout` - Session termination

### Job Management
- `POST /jobs` - Create new job
- `POST /jobs/{id}/images` - Upload images for job
- `POST /jobs/{id}/start` - Start job execution
- `GET /jobs/{id}` - Get job status and output
- `DELETE /jobs/{id}` - Terminate job

### File Access
- `GET /jobs/{id}/files` - List job workspace files
- `GET /jobs/{id}/files/download` - Download files from job workspace

## Development

See [backlog/alpha.md](backlog/alpha.md) for detailed requirements and implementation plan.

## License

MIT License - see LICENSE file for details.