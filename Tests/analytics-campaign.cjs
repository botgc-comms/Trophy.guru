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
    await page.goto('https://trophy.guru/demo?utm_source=linkedin&utm_medium=organic_social&utm_campaign=club_history_launch&utm_content=founder_demo&email=person@example.com');
    const demoEvents = await page.evaluate(() => window.dataLayer.map(e => Array.from(e)));
    const demoPage = demoEvents.find(e => e[1] === 'page_view')[2];
    assert.equal(demoPage.page_path, '/demo');
    assert.equal(demoPage.page_location, 'https://trophy.guru/demo?utm_source=linkedin&utm_medium=organic_social&utm_campaign=club_history_launch&utm_content=founder_demo');
    assert(!JSON.stringify(demoEvents).includes('person@example.com'));
    await page.evaluate(() => {
      document.addEventListener('click', e => e.preventDefault());
      for (const href of ['/honours.html?demo=1#trophy', '/#pricing']) {
        const a = document.createElement('a'); a.href = href; document.body.append(a); a.click();
      }
    });
    const clicks = await page.evaluate(() => window.dataLayer.map(e => Array.from(e)));
    assert(clicks.some(e => e[1] === 'demo_open' && e[2].view === 'trophy'));
    assert(clicks.some(e => e[1] === 'pricing_click'));
    await page.evaluate(() => window.trophyAnalytics.openSettings());
    await page.locator('[data-consent=denied]').click();
    const before = await page.evaluate(() => window.dataLayer.length);
    await page.evaluate(() => { window.trophyAnalytics.track('demo_open', { view: 'person' }); window.trophyAnalytics.track('pricing_click'); });
    assert.equal(await page.evaluate(() => window.dataLayer.length), before);
    console.log('PASS GA4 consent, safe campaign fields, public signup/demo/pricing intent, consent withdrawal and private-page exclusion');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
