import { chromium } from 'playwright';

async function debugLoginForm() {
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();
  
  try {
    await page.goto('http://localhost:5173');
    await page.waitForTimeout(2000);
    
    // Get the login form HTML to see what fields exist
    const formHTML = await page.locator('form').innerHTML();
    console.log('🔍 Login form HTML:');
    console.log(formHTML);
    
    // Try different selectors
    const inputs = await page.locator('input').all();
    console.log(`📝 Found ${inputs.length} input fields:`);
    
    for (let i = 0; i < inputs.length; i++) {
      const type = await inputs[i].getAttribute('type');
      const id = await inputs[i].getAttribute('id');
      const placeholder = await inputs[i].getAttribute('placeholder');
      console.log(`  Input ${i}: type="${type}", id="${id}", placeholder="${placeholder}"`);
    }
    
    await page.waitForTimeout(10000); // Keep open for inspection
    
  } catch (error) {
    console.error('Error:', error);
  } finally {
    await browser.close();
  }
}

debugLoginForm();