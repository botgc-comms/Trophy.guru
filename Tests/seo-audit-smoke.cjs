// Read-only audit of public sitemap URLs. Run against a published build or live site.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const { transform } = require('esbuild');
const base = (process.env.QA_BASE_URL || 'http://127.0.0.1:5197').replace(/\/$/, '');
const origin = process.env.QA_PUBLIC_ORIGIN || base;
const output = process.env.QA_OUTPUT_PATH;
const get = path => fetch(base + path, { redirect: 'manual', signal: AbortSignal.timeout(30000) });
async function main() {
  const xml = await (await get('/sitemap.xml')).text();
  const urls = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => m[1]);
  assert(urls.length > 0);
  const browser = await chromium.launch({ headless: true });
  try {
    // Inspect server-rendered content without running page scripts or analytics.
    const context = await browser.newContext({ javaScriptEnabled: false });
    const page = await context.newPage();
    const pages = [], assets = new Set();
    for (const url of urls) {
      const path = new URL(url).pathname;
      assert.equal(new URL(url).origin, origin);
      const response = await get(path);
      assert.equal(response.status, 200, path);
      assert(!/noindex/i.test(response.headers.get('x-robots-tag') || ''), path);
      if (process.env.QA_REQUIRE_HSTS === 'true') assert.match(response.headers.get('strict-transport-security') || '', /max-age=31536000/, path);
      const html = await response.text();
      assert(!html.includes('{{'), 'Unresolved template token: ' + path);
      await page.setContent(html);
      const details = await page.evaluate(() => ({
        title: document.title,
        description: document.querySelector('meta[name=description]')?.content,
        canonical: document.querySelector('link[rel=canonical]')?.getAttribute('href'),
        robots: document.querySelector('meta[name=robots]')?.content || '',
        headings: [...document.querySelectorAll('h1')].map(e => e.textContent.trim()),
        words: (document.querySelector('main')?.textContent.match(/\b[\p{L}\p{N}]+\b/gu) || []).length,
        links: [...document.querySelectorAll('a[href]')].map(e => e.getAttribute('href')),
        assets: [...document.querySelectorAll('script[src],link[rel=stylesheet]')].map(e => e.getAttribute('src') || e.getAttribute('href')),
        schema: [...document.querySelectorAll('script[type="application/ld+json"]')].flatMap(e => { const g = JSON.parse(e.textContent); return g['@graph'] || [g]; })
      }));
      assert(details.title.length > 10 && details.title.length <= 70, path + ': title length ' + details.title.length);
      assert.equal(details.headings.length, 1, path);
      assert.notEqual(details.title, details.headings[0], path + ': duplicate H1/title');
      assert.equal(details.canonical, url, path);
      assert(details.description, path);
      assert(!/noindex/i.test(details.robots), path);
      assert(details.words >= 200, path + ': word count ' + details.words);
      assert(!details.schema.some(e => ['WebApplication', 'SoftwareApplication'].includes(e['@type'])), path + ': unsupported app rich result');
      for (const item of details.schema) {
        if (item['@type'] === 'Service') assert(item.name && item.provider?.['@id'] && item.url, path);
        if (item['@type'] === 'BreadcrumbList') assert(item.itemListElement.every(e => e['@type'] === 'ListItem' && e.position && e.name && e.item), path);
      }
      for (const asset of details.assets) {
        const target = new URL(asset, url);
        assert.equal(target.origin, origin, 'Uncontrolled third-party stylesheet or script: ' + target);
        assets.add(target.pathname);
      }
      pages.push({ path, ...details, links: [...new Set(details.links.map(link => new URL(link, url)).filter(u => u.origin === origin).map(u => u.pathname))] });
    }
    for (const field of ['title', 'description']) assert.equal(new Set(pages.map(p => p[field])).size, pages.length, 'Duplicate ' + field);
    for (const p of pages) {
      p.incoming = pages.filter(q => q.path !== p.path && q.links.includes(p.path)).map(q => q.path);
      assert(p.incoming.length >= 2, p.path + ': only ' + p.incoming.length + ' incoming pages');
    }
    for (const asset of assets) {
      const response = await get(asset); assert.equal(response.status, 200, asset);
      const code = await response.text();
      const compact = await transform(code, { loader: asset.endsWith('.css') ? 'css' : 'js', minify: true, legalComments: 'inline', charset: 'utf8', target: ['es2020'] });
      assert(Buffer.byteLength(compact.code) / Buffer.byteLength(code) >= 0.98, asset + ': not minified');
      const head = await fetch(base + asset, { method: 'HEAD' }); assert.equal(head.status, 200, asset);
    }
    for (const path of ['/archive.html', '/account-security.html', '/honours.html?demo=1', '/api/trophies']) {
      assert(!urls.some(url => new URL(url).pathname === path.split('?')[0]));
      const response = await get(path);
      assert(/noindex/i.test(response.headers.get('x-robots-tag') || ''), path + ': private page must remain excluded');
    }
    const report = { pages: pages.map(({schema,assets,links,...p}) => p), assetCount: assets.size };
    if (output) { fs.mkdirSync(output, { recursive: true }); fs.writeFileSync(output + '/seo-audit.json', JSON.stringify(report, null, 2)); }
    console.log(`PASS ${pages.length} sitemap pages: concise unique titles, readable content, schema, canonicals, at least two incoming links; ${assets.size} minified assets; private-page exclusions`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
