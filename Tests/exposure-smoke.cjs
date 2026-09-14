// Public release acceptance. No account creation, customer records or analytics transmission.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.QA_BASE_URL || 'http://127.0.0.1:5199';
const output = process.env.QA_OUTPUT_PATH || 'outputs/exposure-2026-09-14/acceptance';
(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext();
    await context.route('https://www.google-analytics.com/**', route => route.abort());
    await context.route('https://www.googletagmanager.com/**', route => route.abort());
    const page = await context.newPage({ viewport: { width: 1440, height: 1000 } });
    const revisions = fs.readdirSync('Content/BlogRevisions').filter(f => f.endsWith('.json')).map(f => JSON.parse(fs.readFileSync('Content/BlogRevisions/' + f, 'utf8')));
    for (const revision of revisions) {
      const response = await page.goto(base + '/blog/' + revision.slug);
      assert.equal(response.status(), 200);
      assert.equal(await page.locator('h1').innerText(), revision.title);
      const schema = await page.locator('script[type="application/ld+json"]').evaluateAll(nodes => nodes.map(n => JSON.parse(n.textContent)).find(s => s['@type'] === 'BlogPosting'));
      assert.equal(new Date(schema.dateModified).toISOString(), '2026-09-14T08:20:00.000Z');
      assert.equal(await page.locator('.blog-faq').count(), 0);
      assert.equal(await page.locator('.article-infographic').count(), 0);
      assert(await page.locator('main a[href="/demo"]').count() > 0);
    }
    console.log('PASS seven existing article URLs serve the reviewed revisions with accurate modification dates and no old FAQ/infographic duplication');
    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.goto(base + '/demo', { waitUntil: 'networkidle' });
      const reject = page.locator('[data-consent=denied]');
      if (await reject.isVisible()) await reject.click();
      const details = await page.evaluate(() => {
        const button = document.querySelector('.product-intro .evidence-button');
        const style = getComputedStyle(button);
        return { width: innerWidth, scroll: document.documentElement.scrollWidth, colour: style.color, background: style.backgroundColor,
          images: [...document.querySelectorAll('main img')].map(i => ({ loaded: i.complete && i.naturalWidth > 0, alt: i.alt })) };
      });
      assert(details.scroll <= details.width);
      assert.equal(details.colour, 'rgb(9, 44, 35)');
      assert.equal(details.background, 'rgb(223, 188, 105)');
      assert(details.images.every(i => i.loaded && i.alt));
      await page.screenshot({ path: `${output}/demo-${width}-top.png` });
      await page.locator('#explore').scrollIntoViewIfNeeded();
      await page.screenshot({ path: `${output}/demo-${width}-example.png` });
    }
    for (const view of ['year/1968', 'trophy', 'person']) {
      await page.goto(base + '/honours.html?demo=1#' + view, { waitUntil: 'networkidle' });
      assert(await page.locator('body').innerText().then(t => t.includes('fictional')));
      assert.equal(await page.locator('[data-view][aria-pressed="true"]').getAttribute('data-view'), view.split('/')[0]);
    }
    console.log('PASS real demo navigation, responsive layout, readable CTA contrast and product images');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
