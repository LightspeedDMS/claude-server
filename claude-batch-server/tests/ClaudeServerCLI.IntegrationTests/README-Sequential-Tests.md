# CLI Integration Tests - Sequential Execution

## ⚠️ IMPORTANT: No Parallel Execution

These CLI integration tests **MUST run sequentially** (not in parallel) due to:

1. **Shared TestServerHarness**: All tests use `[Collection("TestServer")]` sharing the same server instance
2. **Port conflicts**: Tests compete for the same server port (8444+)
3. **CLI process conflicts**: Multiple CLI processes executing simultaneously cause resource contention
4. **Resource exhaustion**: Parallel execution leads to memory issues and deadlocks

## ✅ Current Configuration

The following settings ensure sequential execution:

### 1. Project File (`ClaudeServerCLI.IntegrationTests.csproj`)
```xml
<PropertyGroup>
  <ParallelizeAssembly>false</ParallelizeAssembly>
  <ParallelizeTestCollections>false</ParallelizeTestCollections>
  <MaxParallelThreads>1</MaxParallelThreads>
  <VSTestParallel>false</VSTestParallel>
</PropertyGroup>
```

### 2. XUnit Configuration (`xunit.runner.json`)
```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false,
  "maxParallelThreads": 1
}
```

### 3. VSTest Settings (`test.runsettings`)
```xml
<RunConfiguration>
  <MaxCpuCount>1</MaxCpuCount>
  <DisableParallelization>true</DisableParallelization>
</RunConfiguration>
```

## 🚨 Test Execution

**✅ CORRECT:**
```bash
dotnet test ClaudeServerCLI.IntegrationTests.csproj
```

**❌ NEVER DO:**
```bash
dotnet test --parallel
dotnet test -m:parallel
```

## 🔧 Troubleshooting

If tests hang or timeout:
1. Verify no other test processes are running
2. Check for orphaned API server processes: `ps aux | grep ClaudeBatchServer.Api`
3. Kill any hanging processes before re-running tests
4. Ensure tests run with **maxParallelThreads=1**

## ✅ Test Results

With sequential execution:
- ✅ Individual test classes pass reliably
- ✅ Performance tests meet 10-second thresholds
- ✅ No resource contention or deadlocks
- ✅ Consistent test server startup/teardown