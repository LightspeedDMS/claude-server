/**
 * Unit tests for ApiClient
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import ApiClient, { ApiError, AuthenticationError, NetworkError, TimeoutError } from '../../src/services/api.js';
import AuthService from '../../src/services/auth.js';
import { 
  mockLocalStorage, 
  mockFetch, 
  mockXMLHttpRequest,
  createTestSession, 
  mockApiResponses,
  mockErrorResponses,
  waitFor,
  createTestFile,
  mockConsole
} from '../test-utils.js';

describe('ApiClient', () => {
  let mockStorage;
  let consoleMock;

  beforeEach(() => {
    mockStorage = mockLocalStorage();
    consoleMock = mockConsole();
    vi.clearAllMocks();
    
    // Set up authenticated session for most tests
    const { token, user, expires } = createTestSession();
    AuthService.setToken(token, user, expires);
  });

  afterEach(() => {
    consoleMock.restore();
  });

  describe('URL Construction', () => {
    it('should build correct URLs from endpoints', () => {
      const client = ApiClient;
      
      expect(client.buildURL('auth/login')).toBe('/api/auth/login');
      expect(client.buildURL('/jobs')).toBe('/api/jobs'); // Handle leading slash
      expect(client.buildURL('repositories/test')).toBe('/api/repositories/test');
    });
  });

  describe('Authentication Integration', () => {
    it('should include Authorization header for authenticated requests', async () => {
      const mockFetchFn = mockFetch(mockApiResponses.jobList);
      
      await ApiClient.get('jobs');
      
      expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs', expect.objectContaining({
        headers: expect.objectContaining({
          'Authorization': 'Bearer test-jwt-token-12345'
        })
      }));
    });

    it('should not include Authorization header for unauthenticated requests', async () => {
      const mockFetchFn = mockFetch(mockApiResponses.loginSuccess);
      
      await ApiClient.get('health', {}, false);
      
      expect(mockFetchFn).toHaveBeenCalledWith('/api/health', expect.objectContaining({
        headers: expect.not.objectContaining({
          'Authorization': expect.anything()
        })
      }));
    });

    it('should throw AuthenticationError when token is missing for protected endpoint', async () => {
      AuthService.clearSession();
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(AuthenticationError);
    });

    it('should handle 401 responses and clear session', async () => {
      mockFetch(mockErrorResponses.unauthorized, 401);
      const handleAuthErrorSpy = vi.spyOn(AuthService, 'handleAuthError');
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(AuthenticationError);
      expect(handleAuthErrorSpy).toHaveBeenCalled();
    });
  });

  describe('HTTP Methods', () => {
    describe('GET requests', () => {
      it('should make successful GET request', async () => {
        const mockFetchFn = mockFetch(mockApiResponses.jobList);
        
        const result = await ApiClient.get('jobs');
        
        expect(result).toEqual(mockApiResponses.jobList);
        expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs', expect.objectContaining({
          method: 'GET'
        }));
      });
    });

    describe('POST requests', () => {
      it('should make successful POST request with JSON data', async () => {
        const testData = { prompt: 'Test prompt', repository: 'test-repo' };
        const mockFetchFn = mockFetch(mockApiResponses.createJobResponse);
        
        const result = await ApiClient.post('jobs', testData);
        
        expect(result).toEqual(mockApiResponses.createJobResponse);
        expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs', expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'Content-Type': 'application/json'
          }),
          body: JSON.stringify(testData)
        }));
      });

      it('should handle POST request with FormData', async () => {
        const formData = new FormData();
        formData.append('file', createTestFile());
        const mockFetchFn = mockFetch(mockApiResponses.uploadResponse);
        
        const result = await ApiClient.post('jobs/123/files', formData);
        
        expect(result).toEqual(mockApiResponses.uploadResponse);
        expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs/123/files', expect.objectContaining({
          method: 'POST',
          body: formData
          // Should not have Content-Type header for FormData
        }));
      });

      it('should handle POST request with null data', async () => {
        const mockFetchFn = mockFetch({ success: true });
        
        await ApiClient.post('jobs/123/start', null);
        
        expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs/123/start', expect.objectContaining({
          method: 'POST'
          // Should not have body
        }));
      });
    });

    describe('PUT requests', () => {
      it('should make successful PUT request', async () => {
        const testData = { name: 'Updated Name' };
        const mockFetchFn = mockFetch({ success: true });
        
        await ApiClient.put('repositories/test', testData);
        
        expect(mockFetchFn).toHaveBeenCalledWith('/api/repositories/test', expect.objectContaining({
          method: 'PUT',
          headers: expect.objectContaining({
            'Content-Type': 'application/json'
          }),
          body: JSON.stringify(testData)
        }));
      });
    });

    describe('DELETE requests', () => {
      it('should make successful DELETE request', async () => {
        const mockFetchFn = mockFetch({ success: true });
        
        await ApiClient.delete('jobs/123');
        
        expect(mockFetchFn).toHaveBeenCalledWith('/api/jobs/123', expect.objectContaining({
          method: 'DELETE'
        }));
      });
    });
  });

  describe('Error Handling', () => {
    it('should throw ApiError for client errors (4xx)', async () => {
      mockFetch(mockErrorResponses.badRequest, 400);
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(ApiError);
    });

    it('should throw ApiError for server errors (5xx)', async () => {
      mockFetch(mockErrorResponses.serverError, 500);
      
      // Should retry server errors, but eventually throw
      await expect(ApiClient.get('jobs')).rejects.toThrow(ApiError);
    });

    it('should handle network errors', async () => {
      global.fetch = vi.fn().mockRejectedValue(new Error('Network error'));
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(NetworkError);
    });

    it('should handle timeout errors', async () => {
      global.fetch = vi.fn().mockRejectedValue({ name: 'AbortError' });
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(TimeoutError);
    });
  });

  describe('Retry Logic', () => {
    it('should retry failed requests with exponential backoff', async () => {
      const mockFetchFn = vi.fn()
        .mockRejectedValueOnce(new Error('Network error'))
        .mockRejectedValueOnce(new Error('Network error'))
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          headers: new Map([['content-type', 'application/json']]),
          json: async () => mockApiResponses.jobList
        });

      global.fetch = mockFetchFn;
      
      const result = await ApiClient.get('jobs');
      
      expect(result).toEqual(mockApiResponses.jobList);
      expect(mockFetchFn).toHaveBeenCalledTimes(3);
    });

    it('should not retry authentication errors', async () => {
      const mockFetchFn = mockFetch(mockErrorResponses.unauthorized, 401);
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(AuthenticationError);
      expect(mockFetchFn).toHaveBeenCalledTimes(1); // No retries
    });

    it('should not retry client errors (4xx)', async () => {
      const mockFetchFn = mockFetch(mockErrorResponses.badRequest, 400);
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(ApiError);
      expect(mockFetchFn).toHaveBeenCalledTimes(1); // No retries
    });
  });

  describe('Response Parsing', () => {
    it('should parse JSON responses correctly', async () => {
      mockFetch(mockApiResponses.jobList);
      
      const result = await ApiClient.get('jobs');
      
      expect(result).toEqual(mockApiResponses.jobList);
    });

    it('should handle non-JSON responses', async () => {
      const textResponse = 'Plain text response';
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        headers: new Map([['content-type', 'text/plain']]),
        text: async () => textResponse
      });
      
      const result = await ApiClient.get('health');
      
      expect(result).toEqual({
        ok: true,
        status: 200,
        headers: { 'content-type': 'text/plain' },
        data: textResponse
      });
    });

    it('should handle empty responses gracefully', async () => {
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 204,
        headers: new Map(),
        json: async () => null,
        text: async () => ''
      });
      
      const result = await ApiClient.get('jobs/123/cancel');
      
      expect(result).toBeNull();
    });
  });

  describe('File Upload with Progress', () => {
    it('should upload files with progress tracking', async () => {
      const mockXHR = mockXMLHttpRequest();
      const progressCallback = vi.fn();
      const formData = new FormData();
      formData.append('file', createTestFile());
      
      // Simulate successful upload
      setTimeout(() => {
        mockXHR.status = 200;
        mockXHR.responseText = JSON.stringify(mockApiResponses.uploadResponse);
        mockXHR.addEventListener.mock.calls.find(call => call[0] === 'load')[1]();
      }, 10);
      
      const result = await ApiClient.uploadWithProgress('jobs/123/files', formData, progressCallback);
      
      expect(result).toEqual(mockApiResponses.uploadResponse);
      expect(mockXHR.setRequestHeader).toHaveBeenCalledWith('Authorization', 'Bearer test-jwt-token-12345');
    });

    it('should handle upload progress events', async () => {
      const mockXHR = mockXMLHttpRequest();
      const progressCallback = vi.fn();
      const formData = new FormData();
      
      // Simulate progress event
      setTimeout(() => {
        const progressEvent = { lengthComputable: true, loaded: 500, total: 1000 };
        mockXHR.upload.addEventListener.mock.calls.find(call => call[0] === 'progress')[1](progressEvent);
        
        // Then complete
        mockXHR.status = 200;
        mockXHR.responseText = JSON.stringify({ success: true });
        mockXHR.addEventListener.mock.calls.find(call => call[0] === 'load')[1]();
      }, 10);
      
      await ApiClient.uploadWithProgress('jobs/123/files', formData, progressCallback);
      
      expect(progressCallback).toHaveBeenCalledWith(50); // 500/1000 = 50%
    });

    it('should handle upload errors', async () => {
      const mockXHR = mockXMLHttpRequest();
      const formData = new FormData();
      
      setTimeout(() => {
        mockXHR.addEventListener.mock.calls.find(call => call[0] === 'error')[1]();
      }, 10);
      
      await expect(ApiClient.uploadWithProgress('jobs/123/files', formData))
        .rejects.toThrow(NetworkError);
    });

    it('should require authentication for uploads', async () => {
      AuthService.clearSession();
      const formData = new FormData();
      
      await expect(ApiClient.uploadWithProgress('jobs/123/files', formData))
        .rejects.toThrow(AuthenticationError);
    });
  });

  describe('File Download', () => {
    it('should download files as blob', async () => {
      const mockBlob = new Blob(['file content'], { type: 'text/plain' });
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        blob: async () => mockBlob
      });
      
      const result = await ApiClient.downloadFile('jobs/123/files/test.txt');
      
      expect(result).toBe(mockBlob);
    });

    it('should handle download authentication errors', async () => {
      mockFetch(mockErrorResponses.unauthorized, 401);
      
      await expect(ApiClient.downloadFile('jobs/123/files/test.txt'))
        .rejects.toThrow(AuthenticationError);
    });

    it('should handle download failures', async () => {
      mockFetch(mockErrorResponses.notFound, 404);
      
      await expect(ApiClient.downloadFile('jobs/123/files/missing.txt'))
        .rejects.toThrow(ApiError);
    });
  });

  describe('Timeout Configuration', () => {
    it('should allow timeout configuration', () => {
      const originalTimeout = ApiClient.timeout;
      
      ApiClient.setTimeout(60000);
      expect(ApiClient.timeout).toBe(60000);
      
      // Restore original
      ApiClient.setTimeout(originalTimeout);
    });

    it('should apply timeout to requests', async () => {
      ApiClient.setTimeout(100); // Very short timeout
      
      global.fetch = vi.fn().mockImplementation(() => 
        new Promise(resolve => setTimeout(resolve, 200))
      );
      
      await expect(ApiClient.get('jobs')).rejects.toThrow(TimeoutError);
    });
  });

  describe('Retry Configuration', () => {
    it('should allow retry configuration', () => {
      const originalCount = ApiClient.retryCount;
      const originalDelay = ApiClient.retryDelay;
      
      ApiClient.setRetryConfig(5, 2000);
      expect(ApiClient.retryCount).toBe(5);
      expect(ApiClient.retryDelay).toBe(2000);
      
      // Restore original
      ApiClient.setRetryConfig(originalCount, originalDelay);
    });
  });

  describe('Health Check', () => {
    it('should check server health successfully', async () => {
      mockFetch({ status: 'healthy' });
      
      const isHealthy = await ApiClient.isServerHealthy();
      
      expect(isHealthy).toBe(true);
    });

    it('should handle unhealthy server', async () => {
      mockFetch(mockErrorResponses.serverError, 500);
      
      const isHealthy = await ApiClient.isServerHealthy();
      
      expect(isHealthy).toBe(false);
    });

    it('should handle network errors during health check', async () => {
      global.fetch = vi.fn().mockRejectedValue(new Error('Network error'));
      
      const isHealthy = await ApiClient.isServerHealthy();
      
      expect(isHealthy).toBe(false);
    });
  });
});