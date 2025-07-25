import { chromium } from 'playwright';

async function debugWithLogin() {
  console.log('🐛 Starting Playwright debug with login...');
  
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();
  
  // Listen for console messages
  page.on('console', msg => {
    console.log(`📝 BROWSER [${msg.type()}]:`, msg.text());
  });
  
  try {
    console.log('🌐 Navigating to localhost:5173...');
    await page.goto('http://localhost:5173');
    await page.waitForTimeout(2000);
    
    // Check if we're on login page
    const loginForm = await page.locator('form').count();
    if (loginForm > 0) {
      console.log('🔐 Login form detected, logging in...');
      
      // Fill login form
      await page.fill('input[name="username"]', 'jsbattig');
      await page.fill('input[name="password"]', 'test123');
      await page.click('button[type="submit"]');
      
      console.log('⏳ Waiting for login to complete...');
      await page.waitForTimeout(3000);
    }
    
    // Now check the jobs page
    const title = await page.textContent('h1');
    console.log(`📋 Page title after login: "${title}"`);
    
    // Check for job cards
    const jobCards = await page.locator('[data-testid="job-item"]').count();
    console.log(`💼 Job cards found: ${jobCards}`);
    
    if (jobCards > 0) {
      // Get job status
      const status = await page.textContent('[data-testid="job-status"]');
      console.log(`📊 Job status: "${status}"`);
      
      // Check for expandable results
      const resultsSection = await page.locator('.job-results-section').count();
      console.log(`📄 Results sections found: ${resultsSection}`);
      
      // Check the actual job card HTML to see what's rendered
      const jobCardHTML = await page.locator('[data-testid="job-item"]').first().innerHTML();
      console.log(`🔍 Job card HTML (first 800 chars):`);
      console.log(jobCardHTML.slice(0, 800));
      
      // Check if there are any click handlers
      const expandable = await page.locator('.job-results-header').count();
      console.log(`🖱️ Expandable result headers: ${expandable}`);
    }
    
    await page.waitForTimeout(5000); // Keep browser open for inspection
    
  } catch (error) {
    console.error('💥 Debug error:', error);
  } finally {
    await browser.close();
  }
}

debugWithLogin();