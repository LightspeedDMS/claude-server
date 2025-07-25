import { chromium } from 'playwright';

async function checkSimple() {
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();
  
  // Go to your already logged-in session
  await page.goto('http://localhost:5173');
  await page.waitForTimeout(3000);
  
  // Just check what's actually in the DOM
  const pageSource = await page.content();
  
  // Look for specific things
  const hasJobCard = pageSource.includes('data-testid="job-item"');
  const hasResultsSection = pageSource.includes('job-results-section');
  const hasUpdatedTitle = pageSource.includes('Jobs (UPDATED)');
  const hasOutput = pageSource.includes('output');
  
  console.log('🔍 DOM Analysis:');
  console.log(`  Job card exists: ${hasJobCard}`);
  console.log(`  Results section exists: ${hasResultsSection}`);
  console.log(`  Updated title: ${hasUpdatedTitle}`);
  console.log(`  Contains "output": ${hasOutput}`);
  
  // Check for JavaScript errors
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  
  await page.waitForTimeout(2000);
  
  console.log(`📝 JavaScript errors: ${errors.length}`);
  errors.forEach(err => console.log(`  ❌ ${err}`));
  
  await browser.close();
}

checkSimple();