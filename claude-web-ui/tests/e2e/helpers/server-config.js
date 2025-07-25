/**
 * Server Configuration Reader
 * 
 * Reads configuration from Claude Batch Server's .env files
 * This ensures E2E tests use the same credentials as the server
 */

import { readFileSync } from 'fs'
import path from 'path'

/**
 * Parse a .env file and return key-value pairs
 */
function parseEnvFile(filePath) {
  try {
    const content = readFileSync(filePath, 'utf8')
    const config = {}
    
    content.split('\n').forEach(line => {
      // Skip comments and empty lines
      line = line.trim()
      if (!line || line.startsWith('#')) return
      
      // Parse KEY=VALUE format
      const [key, ...valueParts] = line.split('=')
      if (key && valueParts.length > 0) {
        config[key.trim()] = valueParts.join('=').trim()
      }
    })
    
    return config
  } catch (error) {
    console.warn(`Could not read env file ${filePath}: ${error.message}`)
    return {}
  }
}

/**
 * Get server configuration from Claude Batch Server .env files
 */
export function getServerConfig() {
  const serverPath = path.resolve('../claude-batch-server')
  
  // Try to read from multiple .env files in order of preference
  const envFiles = [
    path.join(serverPath, '.env.test'),    // Test-specific config
    path.join(serverPath, '.env'),         // Main env file
  ]
  
  let config = {}
  
  // Merge configs from all available env files
  for (const envFile of envFiles) {
    const envConfig = parseEnvFile(envFile)
    config = { ...config, ...envConfig }
  }
  
  console.log('📋 Loaded server configuration from Claude Batch Server .env files')
  console.log(`📋 Available test user: ${config.TEST_USERNAME || 'Not found'}`)
  
  return {
    testUser: {
      username: config.TEST_USERNAME || 'testuser',
      password: config.TEST_PASSWORD || 'testpass'
    },
    serverUrl: config.SERVER_URL || 'http://localhost:5185',
    jwtKey: config.JWT_KEY,
    // Add other server configs as needed
    ...config
  }
}

/**
 * Validate that required test credentials are available
 */
export function validateTestCredentials() {
  const config = getServerConfig()
  
  if (!config.testUser.username || !config.testUser.password) {
    throw new Error(`
❌ Test credentials not found in Claude Batch Server .env files

Expected files:
  ../claude-batch-server/.env.test
  ../claude-batch-server/.env

Expected format:
  TEST_USERNAME=your-test-user
  TEST_PASSWORD=your-test-password

Current values:
  TEST_USERNAME=${config.testUser.username}
  TEST_PASSWORD=${config.testUser.password ? '[REDACTED]' : 'Not set'}

Please ensure the Claude Batch Server has test credentials configured.
    `)
  }
  
  console.log('✅ Test credentials validated from server configuration')
  return config
}