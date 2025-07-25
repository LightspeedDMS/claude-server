import { chromium } from 'playwright';

async function debugExpandableResults() {
  console.log('🐛 Starting Playwright debug session...');
  
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();
  
  // Listen for console messages
  page.on('console', msg => {
    console.log(`📝 BROWSER LOG [${msg.type()}]:`, msg.text());
  });
  
  // Listen for errors
  page.on('pageerror', error => {
    console.log(`❌ PAGE ERROR:`, error.message);
  });
  
  try {
    console.log('🌐 Navigating to localhost:5173...');
    await page.goto('http://localhost:5173');
    
    console.log('⏳ Waiting for page to load...');
    await page.waitForTimeout(2000);
    
    // Check what title is actually displayed
    const title = await page.textContent('h1');
    console.log(`📋 Page title: "${title}"`);
    
    // Check if job card exists
    const jobCard = await page.locator('[data-testid="job-item"]').first();
    const jobExists = await jobCard.count() > 0;
    console.log(`💼 Job card exists: ${jobExists}`);
    
    if (jobExists) {
      // Get job status
      const status = await page.textContent('[data-testid="job-status"]');
      console.log(`📊 Job status: "${status}"`);
      
      // Check if expandable results section exists
      const resultsSection = await page.locator('.job-results-section').count();
      console.log(`📄 Results sections found: ${resultsSection}`);
      
      // Check if job has output by looking at the job data
      const jobData = await page.evaluate(() => {
        // Try to find the job list component and get its data
        const jobContainer = document.querySelector('#jobsContainer');
        if (jobContainer) {
          return jobContainer.innerHTML.slice(0, 500); // First 500 chars
        }
        return 'No job container found';
      });
      console.log(`🔍 Job container HTML preview:`, jobData);
    }
    
    // Check if there are any network errors
    const responses = [];
    page.on('response', response => {
      if (response.url().includes('jobs')) {
        responses.push(`${response.status()} ${response.url()}`);
      }
    });
    
    // Trigger refresh to see network calls
    console.log('🔄 Clicking refresh button...');
    await page.click('button:has-text("Refresh")');
    await page.waitForTimeout(3000);
    
    console.log('🌐 Network responses for jobs:', responses);
    
    // Check JavaScript errors in detail
    const jsErrors = await page.evaluate(() => {
      return window.performance.getEntriesByType('navigation')[0];
    });
    
    console.log('📊 Page load performance:', jsErrors);
    
  } catch (error) {
    console.error('💥 Debug error:', error);
  } finally {
    await browser.close();
  }
}

debugExpandableResults();