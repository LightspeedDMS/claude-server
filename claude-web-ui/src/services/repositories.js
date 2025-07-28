import { api } from './api.js';

/**
 * Repository management service
 */
export class RepositoryService {
  /**
   * Get list of repositories
   */
  async getRepositories() {
    return await api.request('/repositories');
  }

  /**
   * Register a new repository
   */
  async registerRepository(repoData) {
    return await api.request('/repositories/register', {
      method: 'POST',
      body: JSON.stringify({
        name: repoData.name,
        gitUrl: repoData.gitUrl,
        description: repoData.description || '',
        cidxAware: repoData.cidxAware !== undefined ? repoData.cidxAware : true,
      }),
    });
  }

  /**
   * Unregister a repository
   */
  async unregisterRepository(repoName) {
    return await api.request(`/repositories/${encodeURIComponent(repoName)}`, {
      method: 'DELETE',
    });
  }

  /**
   * Get repository files
   */
  async getRepositoryFiles(repoName, path = '') {
    const query = path ? `?path=${encodeURIComponent(path)}` : '';
    return await api.request(`/repositories/${encodeURIComponent(repoName)}/files${query}`);
  }

  /**
   * Get file content from repository
   */
  async getFileContent(repoName, filePath) {
    return await api.request(`/repositories/${encodeURIComponent(repoName)}/files/content?path=${encodeURIComponent(filePath)}`);
  }
}

// Create singleton instance
export const repositoryService = new RepositoryService();