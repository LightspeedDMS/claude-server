# Claude Server CLI - Complete Command Reference

The Claude Server CLI provides comprehensive command-line access to the Claude Batch Server API with advanced features including universal file upload, interactive wizards, and modern terminal UI.

## Table of Contents

- [Installation & Setup](#installation--setup)
- [Authentication Commands](#authentication-commands)
- [User Management](#user-management)
- [Repository Management](#repository-management)
- [Job Management](#job-management)
- [File Operations](#file-operations)
- [Global Options](#global-options)
- [Configuration](#configuration)
- [Examples](#examples)

## Installation & Setup

### Prerequisites

- .NET 8.0 or higher
- Access to a Claude Batch Server instance

### Installation

```bash
# Install as global tool
dotnet tool install -g ClaudeServerCLI

# Or run from source
git clone https://github.com/your-repo/claude-batch-server
cd claude-batch-server/src/ClaudeServerCLI
dotnet run -- [commands]
```

### Initial Setup

```bash
# Set server URL (if different from default)
claude-server --server-url https://your-server.com

# Login to create your first profile
claude-server auth login --email your-email@example.com
```

## Authentication Commands

### `auth login`

Authenticate with the Claude Batch Server and create/update profiles.

```bash
claude-server auth login [OPTIONS]
```

**Options:**
- `--username`, `--usr`, `-u`: Username (required)
- `--password`, `--pwd`, `-p`: Password (required)
- `--profile`, `-prof`: Profile name (default: "default")
- `--hashed`, `-h`: Indicates password is already hashed (default: false)

**Examples:**
```bash
# Basic login
claude-server auth login --username testuser --password mypassword

# Login with specific profile
claude-server auth login --username workuser --password workpass --profile work

# Login with pre-hashed password (for security over HTTP)
claude-server auth login --username testuser --password '$6$salt$hash...' --hashed
```

### `auth logout`

Log out from the current or specified profile.

```bash
claude-server auth logout [OPTIONS]
```

**Options:**
- `--profile`: Profile to logout from (default: current)
- `--all`: Logout from all profiles

**Examples:**
```bash
# Logout from current profile
claude-server auth logout

# Logout from specific profile
claude-server auth logout --profile work

# Logout from all profiles
claude-server auth logout --all
```

### `auth whoami`

Show information about the currently authenticated user.

```bash
claude-server auth whoami
```

**Examples:**
```bash
# Show current user information
claude-server auth whoami
```

## User Management

The user management commands allow you to manage Claude Server authentication users directly through shadow file operations. These commands work locally and do not require API communication.

### `user add`

Add a new user to Claude Server authentication.

```bash
claude-server user add <username> <password> [OPTIONS]
```

**Arguments:**
- `<username>`: Username for the new user (3-32 characters, alphanumeric + underscore/dash, must start with letter)
- `<password>`: Password for the new user

**Options:**
- `--uid`, `-u`: User ID (default: 1000)
- `--gid`, `-g`: Group ID (default: 1000)
- `--home`, `-h`: Home directory (defaults to /home/{username})
- `--shell`, `-s`: Shell (default: /bin/bash)

**Examples:**
```bash
# Add user with default settings
claude-server user add testuser mypassword123

# Add user with custom UID/GID
claude-server user add workuser workpass --uid 1001 --gid 1001

# Add user with custom home directory and shell
claude-server user add adminuser adminpass --home /opt/admin --shell /bin/zsh

# Add system user with low UID
claude-server user add svcuser servicepass --uid 500 --gid 500 --home /var/svc
```

**Features:**
- Automatic password hashing using SHA-512 with salt
- Multiple hashing fallbacks (mkpasswd, Python crypt, C# implementation)
- Automatic backup creation before modifications
- Username format validation
- Duplicate user detection
- File permission and safety checks

### `user remove`

Remove a user from Claude Server authentication.

```bash
claude-server user remove <username> [OPTIONS]
```

**Arguments:**
- `<username>`: Username to remove

**Options:**
- `--force`, `-f`: Skip confirmation prompt

**Examples:**
```bash
# Remove user with confirmation
claude-server user remove testuser

# Force remove without confirmation
claude-server user remove olduser --force
```

**Features:**
- Confirmation prompt for safety (unless --force used)
- Automatic backup creation before removal
- Removes user from both passwd and shadow files
- Detailed progress reporting

### `user list`

List all users in Claude Server authentication.

```bash
claude-server user list [OPTIONS]
```

**Options:**
- `--detailed`, `-d`: Show detailed user information including home directory, shell, and password status

**Examples:**
```bash
# Basic user list
claude-server user list

# Detailed user information
claude-server user list --detailed
```

**Output Information:**
- **Basic view:** Username, UID, Status, Last Password Change
- **Detailed view:** Username, UID, GID, Home Directory, Shell, Last Password Change, Status

**User Status Types:**
- `✅ Active`: User has valid password and shadow entry
- `🔒 No Password`: User exists but has no password set
- `❌ No Shadow`: User in passwd file but missing shadow entry
- `🚫 Locked`: User account is locked

### `user update`

Update a user's password in Claude Server authentication.

```bash
claude-server user update <username> <new-password>
```

**Arguments:**
- `<username>`: Username to update
- `<new-password>`: New password for the user

**Examples:**
```bash
# Update user password
claude-server user update testuser newsecurepassword123

# Update system user password
claude-server user update svcuser newservicepass
```

**Features:**
- Automatic password hashing using SHA-512 with salt
- Preserves other shadow file fields (expiration, etc.)
- Automatic backup creation before update
- User existence validation
- Updates last password change timestamp

### User Management File Operations

The user management system operates on local shadow files:

**Files Managed:**
- `claude-server-passwd`: User account information (username, UID, GID, home, shell)
- `claude-server-shadow`: Password hashes and security information

**File Locations:**
- Commands look for auth files in the current working directory first
- Fallback to project directory structure if not found locally
- Perfect for development and deployment scenarios

**Backup System:**
All user management operations create timestamped backups:
```
claude-server-passwd.backup.20250725_184000
claude-server-shadow.backup.20250725_184000
```

**Security Features:**
- SHA-512 password hashing with random salt generation
- Multiple hashing method fallbacks for cross-platform compatibility
- File validation and safety checks
- Detailed error reporting and recovery information

## Repository Management

### `repos list`

List all available repositories with details.

```bash
claude-server repos list [OPTIONS]
```

**Options:**
- `--format`, `-f`: Output format (table, json, yaml)
- `--watch`, `-w`: Watch for changes in real-time
- `--type`: Filter by repository type
- `--sort`: Sort by (name, size, modified)

**Examples:**
```bash
# List repositories in table format
claude-server repos list

# Watch for repository changes
claude-server repos list --watch

# Export repository list as JSON
claude-server repos list --format json > repositories.json

# Show only Git repositories
claude-server repos list --type git
```

### `repos create`

Create/register a new repository.

```bash
claude-server repos create [OPTIONS]
```

**Options:**
- `--name`, `-n`: Repository name (required)
- `--path`, `-p`: Local path to repository
- `--url`: Git URL for remote repositories
- `--type`: Repository type (git, local)
- `--description`: Repository description
- `--branch`: Default branch (for git repositories)

**Examples:**
```bash
# Register local repository
claude-server repos create --name my-project --path /path/to/project --type git

# Register remote repository
claude-server repos create --name external-lib --url https://github.com/user/repo.git

# Create with description
claude-server repos create --name api-service --path ./api --description "Main API service"
```

### `repos show`

Show detailed information about a specific repository.

```bash
claude-server repos show <repository-name> [OPTIONS]
```

**Options:**
- `--format`, `-f`: Output format (table, json, yaml)
- `--files`: Show repository files
- `--watch`, `-w`: Watch for changes

**Examples:**
```bash
# Show repository details
claude-server repos show my-project

# Show with file listing
claude-server repos show my-project --files

# Watch repository status
claude-server repos show my-project --watch
```

### `repos delete`

Delete/unregister a repository.

```bash
claude-server repos delete <repository-name> [OPTIONS]
```

**Options:**
- `--force`, `-f`: Skip confirmation
- `--remove-files`: Also remove local files (dangerous!)

**Examples:**
```bash
# Delete repository with confirmation
claude-server repos delete old-project

# Force delete without confirmation
claude-server repos delete temp-repo --force
```

## Job Management

### `jobs create`

Create a new job with advanced features including file upload and templates.

```bash
claude-server jobs create [OPTIONS]
```

**Options:**
- `--repo`, `--repository`, `-r`: Repository name (required unless using --interactive)
- `--prompt`: Prompt text (alternative to stdin or interactive mode)
- `--file`, `-f`: Files to upload (supports multiple files, all types)
- `--interactive`, `-i`: Use interactive full-screen wizard
- `--overwrite`: Overwrite existing files with same name
- `--auto-start`, `--start`, `-s`: Auto-start job after creation
- `--watch`, `-w`: Watch execution progress (implies --auto-start)
- `--job-timeout`: Job timeout in seconds (default: 300)

**Prompt Input Methods:**

1. **Inline prompt:**
```bash
claude-server jobs create --repo myapp --prompt "Analyze the codebase and suggest improvements"
```

2. **Piped from stdin:**
```bash
echo "Complex multi-line prompt here" | claude-server jobs create --repo myapp
cat detailed-prompt.txt | claude-server jobs create --repo myapp
```

3. **Interactive mode:**
```bash
claude-server jobs create --interactive
```

**File Upload Examples:**
```bash
# Single file with template reference
claude-server jobs create --repo myapp \
  --prompt "Analyze {{screenshot.png}} and explain what you see" \
  --file screenshot.png

# Multiple files with templates
claude-server jobs create --repo myapp \
  --prompt "Review {{requirements.pdf}}, analyze {{diagram.png}}, then check {{config.yaml}}" \
  --file requirements.pdf diagram.png config.yaml

# Any file type supported
claude-server jobs create --repo myapp \
  --prompt "Process {{data.xlsx}} and generate report" \
  --file data.xlsx --overwrite

# Auto-start and watch
claude-server jobs create --repo myapp --prompt "Run tests" --auto-start --watch
```

**Interactive Mode:**
The interactive mode provides a full-screen wizard with:
- Repository selection from available repos
- File browser for selecting uploads
- Multi-line prompt editor with template assistance
- Configuration options
- Preview before creation

### `jobs list`

List all jobs with filtering and real-time updates.

```bash
claude-server jobs list [OPTIONS]
```

**Options:**
- `--format`, `-f`: Output format (table, json, yaml)
- `--watch`, `-w`: Real-time updates
- `--status`, `-s`: Filter by status (running, completed, failed, cancelled, pending)
- `--repository`, `--repo`, `-r`: Filter by repository
- `--limit`, `-l`: Limit number of results (default: 50)

**Examples:**
```bash
# List recent jobs
claude-server jobs list

# Watch job status changes
claude-server jobs list --watch

# Show only running jobs
claude-server jobs list --status running

# Filter by repository
claude-server jobs list --repo my-project

# Export as JSON
claude-server jobs list --format json > jobs.json
```

### `jobs show`

Show detailed information about a specific job.

```bash
claude-server jobs show <job-id> [OPTIONS]
```

**Options:**
- `--format`, `-f`: Output format (table, json, yaml)
- `--watch`, `-w`: Watch for updates until completion

**Examples:**
```bash
# Show job details (supports partial job IDs)
claude-server jobs show 12345678

# Watch job until completion
claude-server jobs show 12345678 --watch

# Export job details
claude-server jobs show 12345678 --format json
```

### `jobs start`

Start execution of a pending job.

```bash
claude-server jobs start <job-id>
```

**Examples:**
```bash
# Start job execution
claude-server jobs start 12345678
```

### `jobs cancel`

Cancel a running job.

```bash
claude-server jobs cancel <job-id>
```

**Examples:**
```bash
# Cancel running job
claude-server jobs cancel 12345678
```

### `jobs delete`

Delete a job and all associated data.

```bash
claude-server jobs delete <job-id> [OPTIONS]
```

**Options:**
- `--force`, `-f`: Skip confirmation

**Examples:**
```bash
# Delete job with confirmation
claude-server jobs delete 12345678

# Force delete
claude-server jobs delete 12345678 --force
```

### `jobs logs`

View job execution logs with real-time streaming.

```bash
claude-server jobs logs <job-id> [OPTIONS]
```

**Options:**
- `--watch`, `-w`, `--follow`, `-f`: Stream logs in real-time
- `--tail`: Number of lines to show from end (default: 50)

**Examples:**
```bash
# Show recent logs
claude-server jobs logs 12345678

# Stream logs in real-time
claude-server jobs logs 12345678 --follow

# Show last 100 lines
claude-server jobs logs 12345678 --tail 100
```

## File Operations

### Supported File Types

The CLI supports universal file upload with automatic content-type detection:

**Text Files:**
- `.txt`, `.md`, `.json`, `.xml`, `.yaml`, `.yml`, `.csv`, `.log`, `.sql`
- `.html`, `.htm`, `.css`, `.js`, `.ts`

**Code Files:**
- `.py`, `.java`, `.cs`, `.cpp`, `.c`, `.h`, `.php`, `.rb`, `.go`, `.rs`
- `.scala`, `.sh`, `.bat`, `.ps1`

**Configuration Files:**
- `.ini`, `.cfg`, `.conf`, `.config`, `.toml`, `.dockerfile`

**Document Files:**
- `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt`, `.pptx`, `.rtf`

**Image Files:**
- `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.svg`, `.webp`, `.ico`

**Archive Files:**
- `.zip`, `.tar`, `.gz`, `.rar`, `.7z`

**Other:**
- Files without extensions (README, Dockerfile, etc.)
- Binary files (`.exe`, `.dll`, `.so`, `.dylib`)
- Database files (`.db`, `.sqlite`, `.sqlite3`)

### Template System

Use `{{filename.ext}}` in prompts to reference uploaded files:

```bash
# Template will be replaced with server file path
claude-server jobs create --repo myapp \
  --prompt "Analyze {{data.csv}} and create visualization based on {{config.json}}" \
  --file data.csv config.json
```

## Global Options

These options can be used with any command:

- `--server-url`, `--url`: Override server URL
- `--timeout`, `-t`: Request timeout in seconds (default: 30)
- `--verbose`, `-v`: Enable verbose logging
- `--profile`: Use specific profile
- `--format`: Output format for applicable commands
- `--help`, `-h`: Show help information
- `--version`: Show version information

## Configuration

### Environment Variables

- `CLAUDE_SERVER_TOKEN`: Authentication token
- `CLAUDE_SERVER_URL`: Default server URL
- `CLAUDE_SERVER_PROFILE`: Default profile to use

### Configuration Files

Profiles are stored in:
- **Windows:** `%APPDATA%\ClaudeServerCLI\profiles.json`
- **macOS:** `~/Library/Application Support/ClaudeServerCLI/profiles.json`
- **Linux:** `~/.config/ClaudeServerCLI/profiles.json`

### Profile Structure

```json
{
  "default": {
    "serverUrl": "https://localhost:8443",
    "email": "user@example.com",
    "token": "eyJ...",
    "lastUsed": "2024-07-22T10:30:00Z"
  }
}
```

## Examples

### Common Workflows

**1. Initial Setup:**
```bash
# First-time setup
claude-server auth login --email user@example.com
claude-server repos create --name myapp --path ./my-application

# Set up local authentication users
claude-server user add adminuser securepassword --uid 1001
claude-server user add apiuser apipassword --uid 1002
```

**2. Simple Job Creation:**
```bash
# Basic job
claude-server jobs create --repo myapp --prompt "Review the code quality" --auto-start --watch
```

**3. Document Analysis:**
```bash
# Upload and analyze documents
claude-server jobs create --repo myapp \
  --prompt "Summarize {{report.pdf}} and create action items based on {{meeting-notes.docx}}" \
  --file report.pdf meeting-notes.docx \
  --auto-start
```

**4. Complex Interactive Workflow:**
```bash
# Use interactive mode for complex jobs
claude-server jobs create --interactive
# This opens a full-screen wizard for:
# - Repository selection
# - File browser
# - Multi-line prompt editor
# - Template assistance
# - Configuration options
```

**5. Monitoring and Management:**
```bash
# Watch all job activity
claude-server jobs list --watch

# Monitor specific job execution
claude-server jobs show abc12345 --watch

# Stream logs in real-time
claude-server jobs logs abc12345 --follow
```

**6. Batch Operations:**
```bash
# Create multiple jobs with different file sets
for config in configs/*.yaml; do
  claude-server jobs create --repo myapp \
    --prompt "Validate configuration {{$(basename "$config")}}" \
    --file "$config" \
    --auto-start
done
```

**7. Data Processing Pipeline:**
```bash
# Process data files with results tracking
claude-server jobs create --repo data-processor \
  --prompt "Process {{raw-data.csv}}, apply transformations from {{transforms.json}}, and generate report" \
  --file data/raw-data.csv config/transforms.json \
  --auto-start --watch > processing-log.txt
```

### Advanced Usage

**Using Multiple Profiles:**
```bash
# Work profile for company projects
claude-server auth login --email work@company.com --profile work
claude-server --profile work repos create --name company-api --url https://github.com/company/api

# Personal profile for side projects  
claude-server auth login --email personal@gmail.com --profile personal
claude-server --profile personal repos create --name my-bot --path ~/projects/bot
```

**Scripting with JSON Output:**
```bash
#!/bin/bash
# Create job and wait for completion
JOB_ID=$(claude-server jobs create --repo myapp --prompt "Run tests" --auto-start --format json | jq -r '.jobId')
echo "Created job: $JOB_ID"

# Poll for completion
while [ "$(claude-server jobs show $JOB_ID --format json | jq -r '.status')" != "completed" ]; do
  echo "Job still running..."
  sleep 5
done

echo "Job completed!"
claude-server jobs logs $JOB_ID
```

**Complex File Processing:**
```bash
# Upload entire directory of related files
find ./analysis-files -type f \( -name "*.txt" -o -name "*.json" -o -name "*.csv" \) \
  -exec claude-server jobs create --repo analytics \
    --prompt "Analyze all uploaded files and create summary report" \
    --file {} + \
    --auto-start
```

**User Management Workflows:**
```bash
# Complete user management workflow
# 1. Set up authentication users for a new deployment
claude-server user add admin masterpassword --uid 1000 --shell /bin/bash
claude-server user add developer devpass --uid 1001 --home /opt/dev
claude-server user add service svcpass --uid 1002 --shell /bin/false

# 2. List all users to verify setup
claude-server user list --detailed

# 3. Update passwords for security rotation
claude-server user update admin newmasterpass
claude-server user update developer newdevpass
claude-server user update service newsvcpass

# 4. Remove deprecated users
claude-server user remove olduser --force
claude-server user remove tempuser --force

# 5. Verify final user state
claude-server user list

# Batch user creation from configuration
while read username password uid; do
  claude-server user add "$username" "$password" --uid "$uid"
done < users.txt

# Backup current authentication state
cp claude-server-passwd claude-server-passwd.manual-backup
cp claude-server-shadow claude-server-shadow.manual-backup
```

## Troubleshooting

### Common Issues

**1. Authentication failures:**
```bash
# Check current profile
claude-server auth profiles

# Re-login if token expired
claude-server auth login --email user@example.com
```

**2. File upload issues:**
```bash
# Check file permissions
ls -la problematic-file.txt

# Verify file type support
file problematic-file.txt
```

**3. Network connectivity:**
```bash
# Test server connectivity
claude-server --server-url https://your-server.com --timeout 10 repos list
```

**4. Performance issues:**
```bash
# Use verbose mode for debugging
claude-server --verbose jobs create --repo myapp --prompt "test"
```

**5. User management issues:**
```bash
# Check authentication file permissions
ls -la claude-server-passwd claude-server-shadow

# Verify user management commands work
claude-server user list

# Test password hashing
claude-server user add testuser testpass123
claude-server user remove testuser --force

# Check backup files if operation failed
ls -la *.backup.*

# Restore from backup if needed
cp claude-server-passwd.backup.20250725_184000 claude-server-passwd
cp claude-server-shadow.backup.20250725_184000 claude-server-shadow
```

### Getting Help

- Use `--help` with any command for detailed usage
- Check server logs for API-related issues
- Verify network connectivity and firewall settings
- Ensure file permissions allow reading uploaded files

---

For more information, visit the [Claude Batch Server Documentation](../../../README.md).