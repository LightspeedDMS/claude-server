/**
 * Sample JavaScript code for testing purposes
 * This file demonstrates various JavaScript patterns and can be used
 * to test code analysis functionality in Claude Batch Server
 */

// ES6 Class example
class TestUtility {
  constructor(name) {
    this.name = name
    this.version = '1.0.0'
  }

  // Async method example
  async processData(data) {
    try {
      const result = await this.validateData(data)
      return this.transformData(result)
    } catch (error) {
      console.error('Error processing data:', error)
      throw error
    }
  }

  // Method with various JavaScript features
  validateData(data) {
    if (!data || typeof data !== 'object') {
      throw new Error('Invalid data provided')
    }

    // Destructuring and default values
    const { name = 'unnamed', type = 'unknown', ...rest } = data

    // Template literals and arrow functions
    const isValid = Object.keys(rest).every(key => 
      rest[key] !== null && rest[key] !== undefined
    )

    if (!isValid) {
      throw new Error(`Validation failed for ${name}`)
    }

    return { name, type, ...rest, validated: true }
  }

  // Method with modern JavaScript patterns
  transformData(data) {
    // Array methods and spreading
    const keys = Object.keys(data)
    const transformed = keys.reduce((acc, key) => {
      acc[key] = typeof data[key] === 'string' 
        ? data[key].toLowerCase() 
        : data[key]
      return acc
    }, {})

    return {
      ...transformed,
      processed: true,
      timestamp: new Date().toISOString()
    }
  }

  // Static method
  static createInstance(name) {
    return new TestUtility(name)
  }
}

// Function declarations and exports
export function helperFunction(input) {
  return input * 2
}

export const constants = {
  MAX_RETRY_ATTEMPTS: 3,
  DEFAULT_TIMEOUT: 5000,
  API_VERSION: 'v1'
}

// Default export
export default TestUtility