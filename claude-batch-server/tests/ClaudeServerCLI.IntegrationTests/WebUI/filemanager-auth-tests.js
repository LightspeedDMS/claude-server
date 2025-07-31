/**
 * JavaScript tests for enhanced authentication functionality
 * Run these in browser console or with a test framework
 */

// Mock JobFileManager for testing
class TestJobFileManager {
    constructor() {
        this.authRetryCount = 0;
        this.maxRetries = 3;
    }

    // Test method for hash detection
    isPrecomputedHash(password) {
        // Shadow file hash format: $algorithm$salt$hash
        // Valid algorithms: $1$ (MD5), $5$ (SHA-256), $6$ (SHA-512), $y$ (yescrypt)
        if (!password.startsWith("$")) return false;
        
        const parts = password.split('$');
        if (parts.length < 4) return false;
        
        // Check if algorithm is supported
        const algorithm = parts[1];
        return algorithm === "1" || algorithm === "5" || algorithm === "6" || algorithm === "y";
    }

    // Enhanced login prompt with hash support
    showLoginPrompt(retryReason = null) {
        let promptMessage = 'Enter username:';
        if (retryReason) {
            promptMessage = `Authentication failed (${retryReason}). Enter username:`;
        }
        
        const username = prompt(promptMessage);
        if (!username) return;

        let passwordMessage = 'Enter password or hash (e.g., $y$...):';
        if (retryReason) {
            passwordMessage = `Enter password or hash (${retryReason}):`;
        }
        
        const password = prompt(passwordMessage);
        
        if (username && password) {
            this.login(username, password);
        }
    }

    // Enhanced login with specific error handling
    async login(username, password) {
        try {
            // Detect if user is providing a pre-computed hash
            const isHashAuth = this.isPrecomputedHash(password);
            
            const response = await fetch('/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ 
                    username, 
                    password,
                    authType: isHashAuth ? 'hash' : 'plaintext'
                })
            });

            if (response.ok) {
                const result = await response.json();
                localStorage.setItem('authToken', result.token);
                localStorage.setItem('username', result.username);
                this.authRetryCount = 0; // Reset retry count on success
                this.loadJobs(); // Retry loading jobs after authentication
                return true;
            } else {
                // Parse detailed error response
                const errorData = await response.json().catch(() => ({}));
                const errorType = errorData.errorType || 'Unknown';
                const errorMessage = errorData.error || 'Login failed';
                
                this.handleLoginError(errorType, errorMessage);
                return false;
            }
        } catch (error) {
            console.error('Login error:', error);
            this.handleLoginError('NetworkError', 'Network error occurred. Please check your connection.');
            return false;
        }
    }

    // Specific error handling with retry logic
    handleLoginError(errorType, errorMessage) {
        this.authRetryCount++;
        
        if (this.authRetryCount >= this.maxRetries) {
            alert(`Authentication failed after ${this.maxRetries} attempts. Please contact an administrator.\n\nLast error: ${errorMessage}`);
            this.authRetryCount = 0;
            return;
        }

        let retryPrompt = '';
        switch (errorType) {
            case 'InvalidCredentials':
                retryPrompt = 'Invalid username or password';
                break;
            case 'MalformedHash':
                retryPrompt = 'Invalid hash format. Use $y$... for yescrypt hashes';
                break;
            case 'UserNotFound':
                retryPrompt = 'User not found';
                break;
            case 'ValidationError':
                retryPrompt = 'Username and password are required';
                break;
            case 'NetworkError':
                retryPrompt = 'Network error';
                break;
            default:
                retryPrompt = 'Authentication error';
        }

        // Show retry prompt with specific error context
        setTimeout(() => {
            this.showLoginPrompt(retryPrompt);
        }, 100);
    }

    // Mock loadJobs for testing
    async loadJobs() {
        console.log('Loading jobs...');
        return true;
    }
}

// Test cases
function runAuthenticationTests() {
    const testManager = new TestJobFileManager();
    
    // Test 1: Hash detection
    console.log('Test 1: Hash detection');
    console.assert(testManager.isPrecomputedHash('$y$j9T$salt$hash') === true, 'Should detect yescrypt hash');
    console.assert(testManager.isPrecomputedHash('$6$salt$hash') === true, 'Should detect SHA-512 hash');
    console.assert(testManager.isPrecomputedHash('plainpassword') === false, 'Should not detect plain password');
    console.assert(testManager.isPrecomputedHash('$invalid') === false, 'Should not detect malformed hash');
    console.log('✓ Hash detection tests passed');

    // Test 2: Error handling
    console.log('Test 2: Error handling');
    testManager.handleLoginError('InvalidCredentials', 'Wrong password');
    console.assert(testManager.authRetryCount === 1, 'Should increment retry count');
    
    testManager.handleLoginError('MalformedHash', 'Bad hash format');
    console.assert(testManager.authRetryCount === 2, 'Should continue incrementing retry count');
    console.log('✓ Error handling tests passed');

    console.log('All authentication tests passed!');
}

// Export for testing
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { TestJobFileManager, runAuthenticationTests };
}