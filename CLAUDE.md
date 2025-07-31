## Development Guidelines

- Going forward, you will always compile the code when you complete changes before you claim anything has been done. No code compilation success, you didn't achieve anything. You will not run the server, I run the server, you compile, you run tests, I run the server

## Code Quality

- No warnings in this project. Ever. Production code, tests. No warnings.

## Test Architecture and E2E Test Structure

### Overview

The Claude Batch Server uses a systematic test execution approach designed to handle different test types appropriately based on their characteristics and interdependence requirements.

### Test Execution Structure

#### Main Entry Point: `/run.sh`
The root-level `run.sh` script provides the main interface for all test operations:

```bash
./run.sh test unit         # Unit tests (fast, run as suites)
./run.sh test integration  # Integration tests (run as suites with timeouts)
./run.sh test e2e          # E2E tests (run individually - systematic approach)
./run.sh test all          # All tests in sequence (unit → integration → e2e)
./run.sh test playwright   # Playwright web UI tests (separate)
```

#### Test Execution Engine: `/claude-batch-server/test-runner.sh`
The actual test execution logic is implemented in `claude-batch-server/test-runner.sh`, which is called by the main `run.sh` script. This separation allows for:
- Complex test orchestration logic without cluttering the main runner
- Specialized handling for different test types
- Detailed progress tracking and failure analysis
- Timeout management and resource cleanup

### E2E Test Structure and Individual Execution

#### Why Individual E2E Test Execution?

**Critical Discovery**: E2E tests in the `AuthenticationE2ETests` class work perfectly when run individually (10/10 pass) but hang when run as a complete test suite. This led to the architectural decision to run E2E tests individually rather than as suites.

**Root Cause**: Test interdependence issues where:
- Shared resources (TestServerHarness, CLI processes, authentication state)
- Port conflicts between test instances
- Authentication state pollution between tests
- Race conditions in parallel execution

#### E2E Test Location and Organization

**Primary E2E Test File**: `/claude-batch-server/tests/ClaudeServerCLI.IntegrationTests/E2E/`

Key test classes:
- `AuthenticationE2ETests.cs` - CLI authentication flows (login, logout, whoami, profiles)
- `RepositoryManagementE2ETests.cs` - Git repository operations and management
- `JobManagementE2ETests.cs` - Job creation, execution, and management
- Other E2E test classes as needed

#### Individual Test Execution Implementation

The `test-runner.sh` script implements individual E2E test execution through:

1. **Test Discovery**: Uses `dotnet test --list-tests` to discover all E2E test methods
2. **Individual Filtering**: Creates specific filters for each test: `FullyQualifiedName~"TestMethodName"`
3. **Sequential Execution**: Runs each test with its own isolated process and timeout
4. **Resource Management**: Includes cleanup delays between tests to prevent resource contention
5. **Detailed Logging**: Captures individual test results and failure details

```bash
# Example of how individual tests are executed:
run_single_test "$TEST_PROJECT" "FullyQualifiedName~\"LoginCommand WithValidCredentials ShouldSucceed\"" "LoginCommand WithValidCredentials ShouldSucceed" $E2E_TIMEOUT
```

#### Test Infrastructure Components

**TestServerHarness** (`/claude-batch-server/tests/ClaudeServerCLI.IntegrationTests/TestServerHarness.cs`):
- Manages API server lifecycle for CLI E2E tests
- Provides isolated test environment with dynamic port allocation
- Creates temporary workspaces and authentication files
- **Critical**: Uses `[Collection("TestServer")]` to ensure sequential execution across all E2E tests

**CLITestHelper** (implied in test base classes):
- Executes CLI commands with proper server URL configuration
- Handles process lifecycle and output capture
- Manages authentication state between CLI commands

#### Success Metrics and Current Status

**Confirmed Working Tests**: 47+ tests across categories
- UserManagementIntegrationTests: 10/10 PASS
- Phase34IntegrationTests: 17/17 PASS  
- ReposList Tests: 5/5 PASS
- Individual AuthenticationE2ETests: 10/10 PASS (when run individually)

#### Key Architectural Decisions

1. **No Parallel Execution**: All E2E tests run sequentially to avoid resource conflicts
2. **Individual Test Isolation**: Each E2E test runs in its own process with timeouts
3. **Real Operations**: E2E tests perform actual network operations, file system operations, and git operations (no mocking of core functionality)
4. **Systematic Failure Analysis**: Detailed logging and failure capture for debugging
5. **Test Type Segregation**: Unit tests run as fast suites, integration tests run as suites with timeouts, E2E tests run individually

#### Troubleshooting Common Issues

**Test Suite Hangs**: If an E2E test suite hangs but individual tests work, this indicates test interdependence. The solution is individual test execution, not architectural changes.

**JSON Output Contamination**: Ensure CLI commands use `--quiet` flags to prevent ANSI escape codes from contaminating JSON output in tests.

**Port Conflicts**: The TestServerHarness automatically allocates available ports (8444-8499 range) to prevent conflicts.

**Authentication State**: Tests must properly handle authentication state setup and cleanup between test executions.

This architecture ensures reliable, systematic test execution while maintaining the integrity of E2E testing principles - testing real functionality end-to-end without mocking core features.