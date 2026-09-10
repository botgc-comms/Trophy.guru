const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext();
    // Every request is fulfilled locally; this test never sends a real GA event.
    await context.route('**/*', route => {
      const url = new URL(route.request().url());
      if (url.pathname === '/analytics.js') return route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync('wwwroot/analytics.js', 'utf8') });
      if (url.hostname !== 'trophy.guru') return route.fulfill({ contentType: 'application/javascript', body: '' });
      return route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Public guide</title><script src="/analytics.js" defer></script><a href="/archive.html#signup">Start</a>' });
    });
    const page = await context.newPage();
    await page.goto('https://trophy.guru/how-it-works?utm_source=chatgpt.com&utm_medium=referral&utm_campaign=history&token=private&email=person@example.com');
    assert.equal(await page.evaluate(() => window.dataLayer?.length || 0), 0);
    await page.locator('[data-consent=granted]').click();
    const events = await page.evaluate(() => window.dataLayer.map(e => Array.from(e)));
    const view = events.find(e => e[0] === 'event' && e[1] === 'page_view')[2];
    assert.equal(view.page_location, 'https://trophy.guru/how-it-works?utm_source=chatgpt.com&utm_medium=referral&utm_campaign=history');
    assert.equal(view.page_path, '/how-it-works');
    assert(!JSON.stringify(events).includes('person@example.com'));
    await page.evaluate(() => { document.querySelector('a').addEventListener('click', e => e.preventDefault()); document.querySelector('a').click(); });
    assert(await page.evaluate(() => window.dataLayer.some(e => e[1] === 'signup_click')));
    await page.goto('https://trophy.guru/archive.html?utm_source=chatgpt.com');
    assert.equal(await page.evaluate(() => window.dataLayer?.length || 0), 0);
    await page.goto('https://trophy.guru/about?utm_source=person%40example.com&utm_campaign=hello%20world&token=secret');
    const sent = await page.evaluate(() => window.dataLayer.map(e => Array.from(e)));
    assert(!JSON.stringify(sent).includes('token') && !JSON.stringify(sent).includes('example.com'));
    const view2 = sent.find(e => e[1] === 'page_view')[2];
    assert.equal(view2.page_location, 'https://trophy.guru/about');
    console.log('PASS GA4 consent, ChatGPT attribution, safe campaign fields, public signup intent and private-page exclusion');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
