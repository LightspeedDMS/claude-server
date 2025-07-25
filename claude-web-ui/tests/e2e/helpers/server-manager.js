/**
 * Claude Batch Server Management for E2E Tests
 * 
 * Handles starting and stopping the Claude Batch Server for E2E testing
 * Ensures tests are self-contained and don't depend on external server state
 */

import { spawn, exec, execSync } from 'child_process'
import { promisify } from 'util'
import path from 'path'
import fs, { existsSync } from 'fs'
import { promises as fsPromises } from 'fs'
import os from 'os'

const execAsync = promisify(exec)

export class ServerManager {
  constructor(config = {}) {
    this.config = {
      // Default configuration - can be overridden
      serverPath: config.serverPath || '../claude-batch-server',
      serverUrl: config.serverUrl || 'http://localhost:5185',
      healthEndpoint: config.healthEndpoint || '/swagger/v1/swagger.json',
      startupTimeout: config.startupTimeout || 60000, // 1 minute
      shutdownTimeout: config.shutdownTimeout || 30000, // 30 seconds
      dotnetCommand: config.dotnetCommand || this.findDotnetCommand(),
      projectFile: config.projectFile || 'src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj',
      ...config
    }
    
    this.serverProcess = null
    this.wasServerRunning = false
    this.startedByTest = false
  }

  /**
   * Find the dotnet command, checking common installation paths
   */
  findDotnetCommand() {
    
    // Common dotnet installation paths
    const possiblePaths = [
      'dotnet', // System PATH
      '/usr/bin/dotnet', // Linux system install
      '/usr/local/bin/dotnet', // Linux local install
      path.join(os.homedir(), '.dotnet', 'dotnet'), // User install
      'C:\\Program Files\\dotnet\\dotnet.exe', // Windows system
      'C:\\Program Files (x86)\\dotnet\\dotnet.exe', // Windows x86
    ]
    
    for (const dotnetPath of possiblePaths) {
      try {
        // Try to execute dotnet --version to test if it works
        if (dotnetPath === 'dotnet') {
          // Test if dotnet is in PATH
          execSync('dotnet --version', { stdio: 'ignore' })
          return 'dotnet'
        } else {
          // Test if specific path exists and works
          if (existsSync(dotnetPath)) {
            execSync(`"${dotnetPath}" --version`, { stdio: 'ignore' })
            return dotnetPath
          }
        }
      } catch (error) {
        // Continue trying other paths
        continue
      }
    }
    
    // If no working dotnet found, return default and let it fail with helpful message
    return 'dotnet'
  }

  /**
   * Check if Claude Batch Server is already running
   */
  async isServerRunning() {
    try {
      const response = await fetch(`${this.config.serverUrl}${this.config.healthEndpoint}`, {
        method: 'GET',
        timeout: 5000
      })
      return response.ok
    } catch (error) {
      console.log(`Server health check failed: ${error.message}`)
      return false
    }
  }

  /**
   * Verify Claude Batch Server project exists
   */
  async verifyServerProject() {
    try {
      const serverPath = path.resolve(this.config.serverPath)
      const projectPath = path.join(serverPath, this.config.projectFile)
      
      console.log(`Checking for Claude Batch Server at: ${projectPath}`)
      
      await fsPromises.access(projectPath)
      return { exists: true, path: serverPath, projectPath }
    } catch (error) {
      console.error(`Claude Batch Server project not found at: ${this.config.serverPath}`)
      console.error('Expected project structure:')
      console.error('  ../claude-batch-server/src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj')
      console.error('')
      console.error('Please ensure the Claude Batch Server is cloned in the correct location.')
      return { exists: false, error: error.message }
    }
  }

  /**
   * Start Claude Batch Server if not already running
   */
  async ensureServerRunning() {
    console.log('🔍 Checking Claude Batch Server availability...')
    
    // Check if server is already running
    this.wasServerRunning = await this.isServerRunning()
    if (this.wasServerRunning) {
      console.log('✅ Claude Batch Server is already running')
      return true
    }

    console.log('⚠️ Claude Batch Server not running, attempting to start...')
    
    // Verify server project exists
    const projectCheck = await this.verifyServerProject()
    if (!projectCheck.exists) {
      throw new Error(`Claude Batch Server project not found: ${projectCheck.error}`)
    }

    // Start the server
    await this.startServer(projectCheck.path)
    return true
  }

  /**
   * Start the Claude Batch Server process
   */
  async startServer(serverPath) {
    return new Promise((resolve, reject) => {
      console.log('🚀 Starting Claude Batch Server...')
      
      const startTime = Date.now()
      const projectFile = path.join(serverPath, this.config.projectFile)
      
      // Start the server process
      this.serverProcess = spawn(this.config.dotnetCommand, ['run', '--project', projectFile], {
        cwd: serverPath,
        stdio: ['ignore', 'pipe', 'pipe'],
        detached: false
      })

      let serverOutput = ''
      let serverReady = false

      // Capture server output
      this.serverProcess.stdout.on('data', (data) => {
        const output = data.toString()
        serverOutput += output
        console.log(`[SERVER] ${output.trim()}`)
        
        // Look for indicators that server is ready
        if (output.includes('Now listening on:') || 
            output.includes('Application started') ||
            output.includes('Hosting started')) {
          serverReady = true
        }
      })

      this.serverProcess.stderr.on('data', (data) => {
        const error = data.toString()
        console.error(`[SERVER ERROR] ${error.trim()}`)
        serverOutput += error
      })

      this.serverProcess.on('error', (error) => {
        console.error('❌ Failed to start Claude Batch Server:', error.message)
        reject(new Error(`Failed to start server: ${error.message}`))
      })

      this.serverProcess.on('exit', (code, signal) => {
        if (code !== 0 && !serverReady) {
          console.error(`❌ Claude Batch Server exited with code ${code}`)
          console.error('Server output:', serverOutput)
          reject(new Error(`Server exited with code ${code}`))
        }
      })

      // Wait for server to be ready
      const checkServerReady = async () => {
        if (await this.isServerRunning()) {
          console.log('✅ Claude Batch Server is ready and responding to health checks')
          this.startedByTest = true
          resolve()
          return
        }

        const elapsed = Date.now() - startTime
        if (elapsed > this.config.startupTimeout) {
          console.error('❌ Server startup timeout exceeded')
          console.error('Server output:', serverOutput)
          this.cleanup()
          reject(new Error('Server startup timeout'))
          return
        }

        // Check again in 2 seconds
        setTimeout(checkServerReady, 2000)
      }

      // Start checking after giving the process time to initialize
      setTimeout(checkServerReady, 5000)
    })
  }

  /**
   * Stop the server if it was started by tests
   */
  async cleanup() {
    if (!this.startedByTest || !this.serverProcess) {
      if (this.wasServerRunning) {
        console.log('🔄 Leaving Claude Batch Server running (was already running before tests)')
      }
      return
    }

    console.log('🛑 Shutting down Claude Batch Server (started by tests)...')
    
    return new Promise((resolve) => {
      let shutdownComplete = false
      
      const forceKill = setTimeout(() => {
        if (!shutdownComplete && this.serverProcess) {
          console.log('⚠️ Force killing server process...')
          this.serverProcess.kill('SIGKILL')
        }
      }, this.config.shutdownTimeout)

      this.serverProcess.on('exit', () => {
        shutdownComplete = true
        clearTimeout(forceKill)
        console.log('✅ Claude Batch Server shut down successfully')
        this.serverProcess = null
        resolve()
      })

      // Try graceful shutdown first
      this.serverProcess.kill('SIGTERM')
      
      // If graceful shutdown doesn't work, try SIGINT
      setTimeout(() => {
        if (!shutdownComplete && this.serverProcess) {
          console.log('🔄 Trying SIGINT...')
          this.serverProcess.kill('SIGINT')
        }
      }, 5000)
    })
  }

  /**
   * Wait for server to be fully ready for testing
   */
  async waitForServerReady(maxAttempts = 30) {
    console.log('⏳ Waiting for Claude Batch Server to be fully ready...')
    
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        // Check health endpoint
        const healthResponse = await fetch(`${this.config.serverUrl}${this.config.healthEndpoint}`, {
          method: 'GET',
          timeout: 5000
        })
        
        if (!healthResponse.ok) {
          throw new Error(`Health check failed: ${healthResponse.status}`)
        }

        // Try to get API info or swagger endpoint to ensure API is ready
        try {
          const apiResponse = await fetch(`${this.config.serverUrl}/swagger/v1/swagger.json`, {
            method: 'GET',
            timeout: 5000
          })
          
          if (apiResponse.ok) {
            console.log('✅ Claude Batch Server API is fully ready')
            return true
          }
        } catch (swaggerError) {
          // Swagger might not be available, that's OK
        }

        // If health check passes but API isn't ready, wait a bit more
        if (attempt < maxAttempts) {
          console.log(`⏳ Health check passed, waiting for API to be fully ready... (${attempt}/${maxAttempts})`)
          await new Promise(resolve => setTimeout(resolve, 2000))
          continue
        }

        // Health check passed, assume server is ready
        console.log('✅ Claude Batch Server health check passed')
        return true
        
      } catch (error) {
        if (attempt === maxAttempts) {
          throw new Error(`Server not ready after ${maxAttempts} attempts: ${error.message}`)
        }
        
        console.log(`⏳ Server not ready yet, attempt ${attempt}/${maxAttempts}...`)
        await new Promise(resolve => setTimeout(resolve, 2000))
      }
    }
    
    return false
  }

  /**
   * Get server configuration for tests
   */
  getServerConfig() {
    return {
      baseUrl: this.config.serverUrl,
      healthEndpoint: this.config.healthEndpoint,
      wasAlreadyRunning: this.wasServerRunning,
      startedByTest: this.startedByTest
    }
  }
}

/**
 * Global server manager instance for E2E tests
 */
export const serverManager = new ServerManager()

/**
 * Playwright global setup helper
 */
export async function setupServer() {
  try {
    await serverManager.ensureServerRunning()
    await serverManager.waitForServerReady()
    
    console.log('🎯 Server setup complete, E2E tests can proceed')
    return serverManager.getServerConfig()
  } catch (error) {
    console.error('❌ Failed to setup Claude Batch Server for E2E tests:', error.message)
    console.error('')
    console.error('Possible solutions:')
    console.error('1. Ensure .NET SDK is installed and `dotnet` command is available')
    console.error('2. Verify Claude Batch Server project exists at ../claude-batch-server')
    console.error('3. Check that the server project can be built with `dotnet build`')
    console.error('4. Ensure no other process is using port 8080')
    console.error('')
    throw error
  }
}

/**
 * Playwright global teardown helper
 */
export async function teardownServer() {
  try {
    await serverManager.cleanup()
    console.log('🧹 Server teardown complete')
  } catch (error) {
    console.error('⚠️ Error during server teardown:', error.message)
    // Don't fail tests due to cleanup issues
  }
}