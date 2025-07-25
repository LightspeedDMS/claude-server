/**
 * Unit tests for AuthService
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import AuthService from '../../src/services/auth.js';
import { mockLocalStorage, mockFetch, createTestSession, mockApiResponses, waitFor } from '../test-utils.js';

describe('AuthService', () => {
  let mockStorage;

  beforeEach(() => {
    mockStorage = mockLocalStorage();
    vi.clearAllMocks();
  });

  describe('Token Management', () => {
    it('should set and get token correctly', () => {
      const { token, user, expires } = createTestSession();
      
      AuthService.setToken(token, user, expires);
      
      expect(mockStorage.setItem).toHaveBeenCalledWith('claude_token', token);
      expect(mockStorage.setItem).toHaveBeenCalledWith('claude_user', user);
      expect(mockStorage.setItem).toHaveBeenCalledWith('claude_expires', expires);
      
      expect(AuthService.getToken()).toBe(token);
      expect(AuthService.getUser()).toBe(user);
      expect(AuthService.getExpires()).toEqual(new Date(expires));
    });

    it('should return null for missing token', () => {
      expect(AuthService.getToken()).toBeNull();
      expect(AuthService.getUser()).toBeNull();
      expect(AuthService.getExpires()).toBeNull();
    });

    it('should clear session data', () => {
      const { token, user, expires } = createTestSession();
      AuthService.setToken(token, user, expires);
      
      AuthService.clearSession();
      
      expect(mockStorage.removeItem).toHaveBeenCalledWith('claude_token');
      expect(mockStorage.removeItem).toHaveBeenCalledWith('claude_user');
      expect(mockStorage.removeItem).toHaveBeenCalledWith('claude_expires');
    });
  });

  describe('Authentication Status', () => {
    it('should return false when no token exists', () => {
      expect(AuthService.isAuthenticated()).toBe(false);
    });

    it('should return false when token is expired', () => {
      const expiredDate = new Date(Date.now() - 3600000).toISOString(); // 1 hour ago
      AuthService.setToken('expired-token', 'user', expiredDate);
      
      expect(AuthService.isAuthenticated()).toBe(false);
    });

    it('should return true when token is valid and not expired', () => {
      const { token, user, expires } = createTestSession();
      AuthService.setToken(token, user, expires);
      
      expect(AuthService.isAuthenticated()).toBe(true);
    });

    it('should return false when token expires within buffer time', () => {
      // Token expires in 2 minutes (less than 5 minute buffer)
      const nearExpiry = new Date(Date.now() + 2 * 60 * 1000).toISOString();
      AuthService.setToken('near-expired-token', 'user', nearExpiry);
      
      expect(AuthService.isAuthenticated()).toBe(false);
    });

    it('should detect expired tokens correctly', () => {
      const expiredDate = new Date(Date.now() - 1000).toISOString(); // 1 second ago
      AuthService.setToken('expired-token', 'user', expiredDate);
      
      expect(AuthService.isTokenExpired()).toBe(true);
    });
  });

  describe('Login', () => {
    it('should login successfully with valid credentials', async () => {
      const mockFetchFn = mockFetch(mockApiResponses.loginSuccess);
      
      const result = await AuthService.login({
        username: 'testuser',
        password: 'testpass'
      });
      
      expect(result).toBe(true);
      expect(mockFetchFn).toHaveBeenCalledWith('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: 'testuser', password: 'testpass' })
      });
      
      expect(AuthService.getToken()).toBe(mockApiResponses.loginSuccess.token);
      expect(AuthService.getUser()).toBe(mockApiResponses.loginSuccess.user);
    });

    it('should handle login failure with invalid credentials', async () => {
      mockFetch({ message: 'Invalid credentials' }, 401);
      
      const result = await AuthService.login({
        username: 'baduser',
        password: 'badpass'
      });
      
      expect(result).toBe(false);
      expect(AuthService.getToken()).toBeNull();
    });

    it('should handle network errors gracefully', async () => {
      global.fetch = vi.fn().mockRejectedValue(new Error('Network error'));
      
      const result = await AuthService.login({
        username: 'testuser',
        password: 'testpass'
      });
      
      expect(result).toBe(false);
    });

    it('should handle response without token', async () => {
      mockFetch({ success: true }); // No token in response
      
      const result = await AuthService.login({
        username: 'testuser',
        password: 'testpass'
      });
      
      expect(result).toBe(false);
    });
  });

  describe('Logout', () => {
    beforeEach(() => {
      const { token, user, expires } = createTestSession();
      AuthService.setToken(token, user, expires);
    });

    it('should logout successfully and clear session', async () => {
      const mockFetchFn = mockFetch({ success: true });
      
      const result = await AuthService.logout();
      
      expect(result).toBe(true);
      expect(mockFetchFn).toHaveBeenCalledWith('/api/auth/logout', {
        method: 'POST',
        headers: {
          'Authorization': 'Bearer test-jwt-token-12345',
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({})
      });
      
      expect(AuthService.getToken()).toBeNull();
    });

    it('should clear session even if server logout fails', async () => {
      global.fetch = vi.fn().mockRejectedValue(new Error('Server error'));
      
      const result = await AuthService.logout();
      
      expect(result).toBe(true); // Should still return true
      expect(AuthService.getToken()).toBeNull();
    });

    it('should handle logout when no token exists', async () => {
      AuthService.clearSession(); // Clear any existing session
      
      const result = await AuthService.logout();
      
      expect(result).toBe(true);
    });
  });

  describe('Auth Header', () => {
    it('should return Bearer token header', () => {
      const { token } = createTestSession();
      AuthService.setToken(token);
      
      expect(AuthService.getAuthHeader()).toBe(`Bearer ${token}`);
    });

    it('should return null when no token exists', () => {
      expect(AuthService.getAuthHeader()).toBeNull();
    });
  });

  describe('Error Handling', () => {
    beforeEach(() => {
      // Mock window.location
      delete window.location;
      window.location = { pathname: '/dashboard', href: '' };
    });

    it('should handle auth error and redirect to login', () => {
      const { token, user, expires } = createTestSession();
      AuthService.setToken(token, user, expires);
      
      AuthService.handleAuthError();
      
      expect(AuthService.getToken()).toBeNull();
      expect(window.location.href).toBe('/');
    });

    it('should not redirect if already on login page', () => {
      window.location.pathname = '/';
      const originalHref = window.location.href;
      
      AuthService.handleAuthError();
      
      expect(window.location.href).toBe(originalHref);
    });

    it('should not redirect if on login path', () => {
      window.location.pathname = '/login';
      const originalHref = window.location.href;
      
      AuthService.handleAuthError();
      
      expect(window.location.href).toBe(originalHref);
    });
  });

  describe('Token Validation', () => {
    it('should validate token with server successfully', async () => {
      const { token } = createTestSession();
      AuthService.setToken(token);
      
      const mockFetchFn = mockFetch({ user: 'testuser' });
      
      const result = await AuthService.validateToken();
      
      expect(result).toBe(true);
      expect(mockFetchFn).toHaveBeenCalledWith('/api/user/profile', {
        method: 'GET',
        headers: { 'Authorization': `Bearer ${token}` }
      });
    });

    it('should handle token validation failure', async () => {
      const { token } = createTestSession();
      AuthService.setToken(token);
      
      mockFetch({ message: 'Unauthorized' }, 401);
      
      const result = await AuthService.validateToken();
      
      expect(result).toBe(false);
      expect(AuthService.getToken()).toBeNull(); // Should clear session
    });

    it('should return false when no token exists', async () => {
      const result = await AuthService.validateToken();
      expect(result).toBe(false);
    });

    it('should handle network errors during validation', async () => {
      const { token } = createTestSession();
      AuthService.setToken(token);
      
      global.fetch = vi.fn().mockRejectedValue(new Error('Network error'));
      
      const result = await AuthService.validateToken();
      expect(result).toBe(false);
    });
  });

  describe('Session Info', () => {
    it('should return complete session information', () => {
      const { token, user, expires } = createTestSession();
      AuthService.setToken(token, user, expires);
      
      const sessionInfo = AuthService.getSessionInfo();
      
      expect(sessionInfo).toEqual({
        isAuthenticated: true,
        user: user,
        expires: new Date(expires),
        token: token
      });
    });

    it('should return empty session info when not authenticated', () => {
      const sessionInfo = AuthService.getSessionInfo();
      
      expect(sessionInfo).toEqual({
        isAuthenticated: false,
        user: null,
        expires: null,
        token: null
      });
    });
  });
});