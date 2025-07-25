# Claude Web UI Testing Strategy

## Test Types Overview

### 1. Unit Tests (`/tests/unit/`)
- **Purpose**: Test individual components and services in isolation
- **Mocking**: Heavy use of mocks for external dependencies
- **Command**: `npm test`
- **Status**: ✅ 41/101 passing (core functionality working)

### 2. E2E Tests with Mocks (`/tests/e2e/*.spec.js`)
- **Purpose**: Test UI interactions with mocked API responses
- **Mocking**: Full API mocking via `page.route()`
- **Command**: `npm run test:e2e`
- **Status**: ❌ **INCORRECT APPROACH** - These are not true E2E tests

### 3. REAL E2E Tests (`/tests/e2e/real-*.spec.js`)
- **Purpose**: Test complete system integration with real Claude Batch Server
- **Mocking**: ❌ **NO MOCKS** - Tests against real API
- **Command**: `npm run test:e2e:real`
- **Prerequisites**: 
  - Claude Batch Server running on configured port
  - Valid test user credentials
  - Real database connection

## The Problem with Current E2E Tests

The current E2E tests in `/tests/e2e/*.spec.js` contain extensive API mocking:

```javascript
// ❌ WRONG - This is not E2E testing
await page.route('**/api/auth/login', async route => {
  await route.fulfill({
    status: 200,
    body: JSON.stringify({ token: 'mock-token' })
  })
})
```

**Why this is wrong:**
- Defeats the purpose of E2E testing
- Doesn't catch real integration issues
- Can pass even when API is completely broken
- Tests fake behavior, not real system

## Real E2E Testing Approach

The new `/tests/e2e/real-*.spec.js` files test against the actual Claude Batch Server:

```javascript
// ✅ CORRECT - Real E2E testing
const [response] = await Promise.all([
  page.waitForResponse(response => 
    response.url().includes('/auth/login') && 
    response.request().method() === 'POST'
  ),
  page.click('[data-testid="login-button"]')
])

// Verify REAL API response
expect(response.status()).toBe(200)
const responseBody = await response.json()
expect(responseBody).toHaveProperty('token')
```

## Running Tests

### Development Testing (with mocks)
```bash
npm test                # Unit tests only
npm run test:e2e        # E2E with API mocks (current implementation)
```

### Production Testing (real integration)
```bash
# 1. Start Claude Batch Server
cd ../claude-batch-server
dotnet run

# 2. Update test configuration in real-auth.spec.js
# - Set correct API URL
# - Set valid test credentials

# 3. Run real E2E tests
npm run test:e2e:real
```

## Test Configuration

### Real E2E Test Setup (`tests/e2e/real-auth.spec.js`)

```javascript
const config = {
  apiBaseUrl: 'http://localhost:8080',  // Update to Claude Batch Server URL
  testUser: {
    username: 'testuser',               // Must exist in the system
    password: 'testpass'                // Valid password
  },
  webUIUrl: 'http://localhost:5173'     // Vite dev server
}
```

## Expected Test Results

### When Claude Batch Server is NOT running:
```
❌ Real E2E tests will FAIL - This is CORRECT behavior
✅ Mocked E2E tests will PASS - But this proves nothing
✅ Unit tests will mostly pass
```

### When Claude Batch Server IS running:
```
✅ Real E2E tests should PASS - True integration working
✅ Mocked E2E tests will PASS - Still meaningless
✅ Unit tests will mostly pass
```

## Recommendation

1. **Keep unit tests** - Good for development
2. **Remove API mocks from E2E tests** - They provide false confidence
3. **Use real E2E tests for integration validation** - Only way to verify the system works
4. **Run real E2E tests in CI/CD** - With real Claude Batch Server instance

## Migration Plan

1. ✅ Created real E2E tests without mocks
2. 🔄 Mark current E2E tests as "integration tests with mocks" 
3. ⏳ Remove or refactor existing mocked E2E tests
4. ⏳ Set up CI/CD pipeline with real Claude Batch Server

The goal is to have **genuine end-to-end testing** that catches real integration issues between the web UI and Claude Batch Server.