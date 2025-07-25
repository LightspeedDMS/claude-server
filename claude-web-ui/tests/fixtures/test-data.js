/**
 * Comprehensive test data for Claude Web UI E2E tests
 * Realistic data that mirrors actual Claude Code usage patterns
 */

export const testData = {
  // Test users for authentication scenarios
  users: {
    valid: {
      username: 'testuser',
      password: 'testpass123',
      expectedToken: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.mock-token',
      roles: ['user']
    },
    admin: {
      username: 'admin',
      password: 'adminpass123',
      expectedToken: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.admin-token',
      roles: ['admin', 'user']
    },
    invalid: {
      username: 'wronguser',
      password: 'wrongpass',
      expectedError: 'Invalid credentials'
    },
    expired: {
      username: 'expireduser',
      password: 'expiredpass',
      expectedError: 'Session expired'
    }
  },

  // Test repositories for job creation
  repositories: [
    {
      name: 'frontend-project',
      gitUrl: 'https://github.com/test/frontend-project.git',
      status: 'ready',
      description: 'React-based frontend application',
      lastPull: '2024-01-15T10:00:00Z',
      gitInfo: {
        branch: 'main',
        commit: 'abc123',
        commitMessage: 'Add component library'
      }
    },
    {
      name: 'backend-api',
      gitUrl: 'https://github.com/test/backend-api.git',
      status: 'ready',
      description: 'Node.js REST API server',
      lastPull: '2024-01-15T09:30:00Z',
      gitInfo: {
        branch: 'main',
        commit: 'def456',
        commitMessage: 'Update authentication middleware'
      }
    },
    {
      name: 'cloning-repo',
      gitUrl: 'https://github.com/test/cloning-repo.git',
      status: 'cloning',
      description: 'Repository currently being cloned',
      lastPull: null,
      gitInfo: null
    },
    {
      name: 'error-repo',
      gitUrl: 'https://github.com/test/error-repo.git',
      status: 'error',
      description: 'Repository with clone error',
      lastPull: null,
      gitInfo: null,
      error: 'Authentication failed'
    }
  ],

  // Sample files for upload testing
  testFiles: {
    // Small text file
    simple: {
      name: 'simple.txt',
      content: 'This is a simple test file for upload testing.\nSecond line for multiline testing.',
      size: 89,
      type: 'text/plain'
    },
    
    // JavaScript code file
    javascript: {
      name: 'sample-code.js',
      content: `/**
 * Sample JavaScript file for testing
 */
function calculateSum(a, b) {
  return a + b;
}

class TestClass {
  constructor(name) {
    this.name = name;
  }
  
  greet() {
    return \`Hello, \${this.name}!\`;
  }
}

export { calculateSum, TestClass };`,
      size: 245,
      type: 'text/javascript'
    },

    // JSON configuration file
    config: {
      name: 'config.json',
      content: JSON.stringify({
        api: {
          baseUrl: 'https://api.example.com',
          timeout: 5000,
          retries: 3
        },
        features: {
          authentication: true,
          logging: true,
          monitoring: false
        }
      }, null, 2),
      size: 156,
      type: 'application/json'
    },

    // Large file for testing file size limits
    large: {
      name: 'large-data.txt',
      content: 'x'.repeat(1024 * 100), // 100KB file
      size: 1024 * 100,
      type: 'text/plain'
    },

    // Binary-like file (not actually binary, but simulated)
    binary: {
      name: 'binary-file.bin',
      content: 'BINARY_CONTENT_SIMULATION_' + 'x'.repeat(500),
      size: 527,
      type: 'application/octet-stream'
    }
  },

  // Job test scenarios
  jobs: {
    // Quick completion job
    quick: {
      prompt: 'List all files in the repository and create a simple README.md',
      repository: 'frontend-project',
      files: ['simple.txt'],
      expectedDuration: 30000, // 30 seconds
      expectedStatus: 'completed',
      expectedFiles: ['README.md', 'file-list.txt']
    },

    // Medium complexity job
    medium: {
      prompt: 'Analyze the codebase structure, identify potential improvements, and create documentation for the main components.',
      repository: 'backend-api',
      files: ['sample-code.js', 'config.json'],
      expectedDuration: 120000, // 2 minutes
      expectedStatus: 'completed',
      expectedFiles: ['analysis-report.md', 'component-docs.md', 'improvements.txt']
    },

    // Complex long-running job
    complex: {
      prompt: 'Perform comprehensive code review, generate tests for all functions, create API documentation, and suggest architectural improvements.',
      repository: 'frontend-project',
      files: ['sample-code.js', 'config.json', 'simple.txt'],
      expectedDuration: 300000, // 5 minutes
      expectedStatus: 'completed',
      expectedFiles: ['code-review.md', 'generated-tests.js', 'api-docs.md', 'architecture-review.md']
    },

    // Job that should fail
    failing: {
      prompt: 'This job is designed to fail for testing error handling',
      repository: 'error-repo',
      files: ['large-data.txt'],
      expectedDuration: 10000, // 10 seconds
      expectedStatus: 'failed',
      expectedError: 'Repository access denied'
    },

    // Job with file upload issues
    uploadError: {
      prompt: 'Process uploaded files',
      repository: 'frontend-project',
      files: ['non-existent-file.txt'], // File that doesn't exist
      expectedStatus: 'error',
      expectedError: 'File upload failed'
    }
  },

  // Job statuses for testing state transitions
  jobStatuses: [
    'created',
    'queued', 
    'starting',
    'running',
    'completed',
    'failed',
    'cancelled'
  ],

  // Mock API responses
  mockResponses: {
    // Authentication responses
    auth: {
      loginSuccess: {
        token: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.mock-valid-token',
        user: {
          username: 'testuser',
          roles: ['user']
        },
        expiresIn: 3600
      },
      
      loginFailure: {
        error: 'Invalid credentials',
        code: 'AUTH_INVALID_CREDENTIALS',
        status: 401
      },

      tokenExpired: {
        error: 'Token expired',
        code: 'TOKEN_EXPIRED', 
        status: 401
      }
    },

    // Job creation responses
    jobCreate: {
      success: {
        jobId: 'job-test-12345',
        status: 'created',
        title: 'Test Job Title',
        user: 'testuser',
        repository: 'frontend-project',
        cowPath: '/tmp/workspace/job-test-12345',
        createdAt: '2024-01-15T14:00:00Z'
      },

      failure: {
        error: 'Job creation failed',
        code: 'JOB_CREATE_ERROR',
        status: 500,
        details: 'Repository not accessible'
      }
    },

    // Job monitoring responses
    jobStatus: {
      created: {
        jobId: 'job-test-12345',
        status: 'created',
        progress: 0,
        message: 'Job created successfully'
      },

      running: {
        jobId: 'job-test-12345', 
        status: 'running',
        progress: 45,
        message: 'Analyzing repository structure...',
        startedAt: '2024-01-15T14:00:15Z'
      },

      completed: {
        jobId: 'job-test-12345',
        status: 'completed',
        progress: 100,
        message: 'Job completed successfully',
        output: 'Job execution completed. Generated 3 files.',
        exitCode: 0,
        startedAt: '2024-01-15T14:00:15Z',
        completedAt: '2024-01-15T14:03:30Z',
        files: [
          {
            name: 'analysis-report.md',
            path: '/workspace/analysis-report.md',
            size: 2048,
            type: 'text/markdown'
          }
        ]
      },

      failed: {
        jobId: 'job-test-12345',
        status: 'failed',
        progress: 25,
        message: 'Job failed during execution',
        error: 'Repository access denied',
        exitCode: 1,
        startedAt: '2024-01-15T14:00:15Z',
        completedAt: '2024-01-15T14:01:30Z'
      }
    }
  },

  // Test prompts for different scenarios
  prompts: {
    simple: 'List all files in the repository',
    
    codeAnalysis: `Analyze this codebase and provide:
1. Overall architecture overview
2. Code quality assessment  
3. Potential security vulnerabilities
4. Performance optimization suggestions
5. Documentation improvements needed`,

    testGeneration: `Generate comprehensive test cases for all functions in the uploaded files:
- Unit tests with edge cases
- Integration tests for API endpoints
- Mock data for testing
- Test documentation`,

    documentation: `Create comprehensive documentation including:
- API reference with examples
- Setup and installation guide
- Usage examples
- Contributing guidelines
- Deployment instructions`,

    refactoring: `Review the code and suggest refactoring improvements:
- Extract reusable components
- Improve naming conventions
- Optimize performance bottlenecks
- Enhance error handling
- Update deprecated patterns`
  },

  // Expected file outputs for different job types
  expectedOutputs: {
    analysis: [
      'architecture-overview.md',
      'code-quality-report.md', 
      'security-analysis.md',
      'performance-recommendations.md'
    ],
    
    testing: [
      'unit-tests.js',
      'integration-tests.js',
      'test-data.json',
      'testing-guide.md'
    ],
    
    documentation: [
      'api-reference.md',
      'setup-guide.md',
      'usage-examples.md',
      'contributing.md',
      'deployment.md'
    ],

    refactoring: [
      'refactoring-plan.md',
      'improved-components.js',
      'performance-optimizations.md',
      'migration-guide.md'
    ]
  },

  // Performance benchmarks
  performance: {
    // Expected response times (milliseconds)
    responseTime: {
      login: 500,
      jobCreate: 1000,
      jobStatus: 200,
      fileUpload: 2000,
      repositoryList: 300
    },

    // Expected job execution times (milliseconds)
    jobExecution: {
      simple: 30000,      // 30 seconds
      medium: 120000,     // 2 minutes  
      complex: 300000,    // 5 minutes
      maximum: 600000     // 10 minutes (timeout)
    },

    // File size limits
    fileLimits: {
      singleFile: 10 * 1024 * 1024,    // 10MB
      totalUpload: 50 * 1024 * 1024,   // 50MB
      maxFiles: 20
    }
  },

  // Error scenarios for testing
  errorScenarios: {
    network: {
      timeout: 'Network timeout',
      connectionRefused: 'Connection refused',
      serverError: 'Internal server error'
    },
    
    validation: {
      emptyPrompt: 'Prompt cannot be empty',
      noRepository: 'Repository must be selected',
      invalidFile: 'Invalid file type',
      fileTooLarge: 'File size exceeds limit'
    },

    authentication: {
      invalidCredentials: 'Invalid username or password',
      sessionExpired: 'Your session has expired',
      accessDenied: 'Access denied'
    },

    job: {
      creationFailed: 'Failed to create job',
      executionFailed: 'Job execution failed',
      cancelled: 'Job was cancelled',
      timeout: 'Job execution timed out'
    }
  }
}

export default testData