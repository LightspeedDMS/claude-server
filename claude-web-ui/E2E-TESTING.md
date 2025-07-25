# Real E2E Testing with Automatic Server Management

This document explains how to run true end-to-end tests that automatically manage the Claude Batch Server lifecycle.

## Overview

The E2E testing system now provides **automatic server lifecycle management**:

- ✅ **Detects** if Claude Batch Server is already running
- ✅ **Starts** Claude Batch Server if not running  
- ✅ **Waits** for server to be fully ready
- ✅ **Runs** tests against real API integration
- ✅ **Shuts down** server if it was started by tests
- ✅ **Preserves** existing server if already running

## Quick Start

### 1. Prerequisites

Ensure these are available:

```bash
# .NET SDK (for Claude Batch Server)
dotnet --version

# Node.js (for Web UI)
node --version
npm --version

# Claude Batch Server project
ls ../claude-batch-server/src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj
```

### 2. Set Test Credentials (Optional)

```bash
# Set test user credentials (optional - defaults will be used)
export E2E_TEST_USERNAME="your-test-user"
export E2E_TEST_PASSWORD="your-test-password"
```

### 3. Run Real E2E Tests

```bash
# Run real E2E tests with automatic server management
npm run test:e2e:real
```

**What happens:**
1. 🔍 Checks if Claude Batch Server is running on port 8080
2. 🚀 Starts server if not running (`dotnet run` in ../claude-batch-server)
3. ⏳ Waits for server health checks to pass
4. 🌐 Starts Web UI dev server on port 5173  
5. 🧪 Runs tests against real integration
6. 🛑 Shuts down server if it was started by tests
7. 🔄 Leaves server running if it was already running

## Test Scenarios

### Scenario 1: No Server Running
```bash
npm run test:e2e:real
```
**Result:**
- ✅ Starts Claude Batch Server automatically
- ✅ Runs tests against real server
- ✅ Shuts down server when tests complete

### Scenario 2: Server Already Running  
```bash
# In terminal 1:
cd ../claude-batch-server
dotnet run

# In terminal 2:
npm run test:e2e:real
```
**Result:**
- ✅ Detects existing server
- ✅ Runs tests against existing server  
- ✅ Leaves server running after tests

### Scenario 3: Server Configuration Issues
```bash
npm run test:e2e:real
```
**Result:**
- ❌ Clear error messages explaining the problem
- ❌ Tests don't run (preventing false positives)
- 💡 Helpful troubleshooting suggestions

## Configuration

### Environment Variables

```bash
# Server management
E2E_SERVER_BASE_URL="http://localhost:8080"     # Auto-set by global setup
E2E_SERVER_WAS_RUNNING="true"                   # Auto-set by global setup  
E2E_SERVER_STARTED_BY_TEST="false"              # Auto-set by global setup

# Test credentials (set these manually)
E2E_TEST_USERNAME="testuser"                    # Your test user
E2E_TEST_PASSWORD="testpass"                    # Your test password
```

### Server Manager Configuration

The server manager can be customized in `tests/e2e/helpers/server-manager.js`:

```javascript
const serverManager = new ServerManager({
  serverPath: '../../../claude-batch-server',              // Path to server project
  serverUrl: 'http://localhost:8080',                      // Server URL
  healthEndpoint: '/health',                               // Health check endpoint
  startupTimeout: 60000,                                   // 1 minute startup timeout
  shutdownTimeout: 30000,                                  // 30 second shutdown timeout
  dotnetCommand: 'dotnet',                                 // .NET CLI command
  projectFile: 'src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj'
})
```

## Test Structure

### Real E2E Tests (`tests/e2e/real-*.spec.js`)
- ❌ **NO API MOCKS** - Tests real server responses
- ✅ **Real HTTP requests** - Captures actual API behavior
- ✅ **Full integration** - Tests complete Web UI ↔ API flow
- ✅ **Production-like** - Mimics real user workflows

### Example Test Flow:
```javascript
test('should perform complete login flow with real Claude Batch Server', async ({ page }) => {
  // 1. Navigate to Web UI
  await page.goto('http://localhost:5173')
  
  // 2. Enter real credentials
  await page.fill('[data-testid="username"]', 'testuser')
  await page.fill('[data-testid="password"]', 'testpass')
  
  // 3. Capture REAL API request
  const [response] = await Promise.all([
    page.waitForResponse(response => response.url().includes('/auth/login')),
    page.click('[data-testid="login-button"]')
  ])
  
  // 4. Verify REAL API response
  expect(response.status()).toBe(200)
  const token = await response.json()
  expect(token).toHaveProperty('token')
  
  // 5. Verify UI responds to real data
  await expect(page.locator('[data-testid="dashboard"]')).toBeVisible()
})
```

## Troubleshooting

### Common Issues

**1. "Claude Batch Server project not found"**
```bash
# Solution: Ensure correct directory structure
ls ../claude-batch-server/src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj
```

**2. ".NET SDK not installed"**
```bash
# Solution: Install .NET SDK
dotnet --version
# If not installed: https://dotnet.microsoft.com/download
```

**3. "Port 8080 already in use"**
```bash
# Solution: Kill process using port 8080
sudo lsof -ti:8080 | xargs kill -9
# Or change server configuration to use different port
```

**4. "Health check failed"**
```bash
# Solution: Check server logs and configuration
# Ensure database and dependencies are properly configured
```

**5. "Test user credentials invalid"**
```bash
# Solution: Set correct test credentials
export E2E_TEST_USERNAME="valid-user"
export E2E_TEST_PASSWORD="valid-password"
```

### Debug Mode

Run tests with debug output:
```bash
DEBUG=1 npm run test:e2e:real
```

### Manual Server Management

If you need to manage the server manually:
```bash
# Start server manually
cd ../claude-batch-server
dotnet run

# Run tests against existing server
npm run test:e2e:real

# Server will be left running after tests
```

## Benefits of This Approach

### ✅ **True Integration Testing**
- Tests real API responses, not mocks
- Catches actual integration bugs
- Validates complete system behavior

### ✅ **Self-Contained Tests**  
- No manual server setup required
- Tests manage their own dependencies
- Consistent execution environment

### ✅ **CI/CD Ready**
- Works in automated environments
- Proper cleanup and teardown
- Clear failure reporting

### ✅ **Developer Friendly**
- Automatic server lifecycle management
- Clear error messages and troubleshooting
- Preserves existing development workflow

## Comparison with Mock-Based Tests

| Aspect | Real E2E Tests | Mock-Based Tests |
|--------|----------------|------------------|
| **API Integration** | ✅ Tests real API | ❌ Tests fake responses |
| **Bug Detection** | ✅ Catches integration bugs | ❌ Misses API changes |
| **Confidence** | ✅ High confidence | ❌ False confidence |
| **Setup Complexity** | 🟡 Automated but requires server | ✅ Simple (no server) |
| **Execution Speed** | 🟡 Slower (real server) | ✅ Fast (no I/O) |
| **CI/CD Integration** | ✅ Automated lifecycle | ✅ No dependencies |

## Future Enhancements

- [ ] Support for different server configurations (staging, production)
- [ ] Test data seeding and cleanup
- [ ] Parallel test execution with multiple server instances
- [ ] Performance monitoring during E2E tests
- [ ] Integration with CI/CD pipelines

---

**Remember:** These are TRUE end-to-end tests. They will fail if the Claude Batch Server has issues - and that's the correct behavior! The goal is to catch real integration problems, not to always pass.