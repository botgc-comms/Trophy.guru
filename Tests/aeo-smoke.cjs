// Read-only HTTP and browser QA. Screenshots and reports contain public fixture data only.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.QA_BASE_URL || 'http://127.0.0.1:5197';
const origin = process.env.QA_PUBLIC_ORIGIN || 'https://127.0.0.1:5197';
const output = process.env.QA_OUTPUT_PATH || 'outputs/aeo';
const guidePaths = ['/electronic-honours-boards', '/for-golf-clubs', '/digitise-trophy-records', '/how-it-works', '/faq', '/about', '/privacy-and-security'];
const paths = ['/', '/privacy.html', '/integrations/intelligent-golf/', '/uk/how-to-catalogue-trophy-winners/', '/us/how-to-catalog-trophy-winners/', ...guidePaths, '/blog'];
async function get(path, options = {}) { return fetch(base + path, { redirect: 'manual', ...options }); }
async function main() {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage();
    const docs = new Map();
    const metas = [];
    const localLinks = new Set();
    for (const path of paths) {
      const response = await get(path);
      assert.equal(response.status, 200, path);
      assert(!/noindex/i.test(response.headers.get('x-robots-tag') || ''), path);
      const html = await response.text(); docs.set(path, html);
      await page.setContent(html);
      const details = await page.evaluate(() => ({
        h1s: [...document.querySelectorAll('h1')].map(e => e.textContent.trim().replace(/\s+/g, ' ')),
        title: document.title,
        description: document.querySelector('meta[name=description]')?.content,
        canonical: document.querySelector('link[rel=canonical]')?.getAttribute('href'),
        robots: document.querySelector('meta[name=robots]')?.content,
        og: document.querySelector('meta[property="og:image"]')?.content,
        twitter: document.querySelector('meta[name="twitter:card"]')?.content,
        graphs: [...document.querySelectorAll('script[type="application/ld+json"]')].map(e => JSON.parse(e.textContent)),
        links: [...document.querySelectorAll('a[href]')].map(e => e.getAttribute('href'))
      }));
      assert.equal(details.h1s.length, 1, path);
      assert(details.description && details.og && details.twitter, path);
      assert.equal(details.canonical, origin + path, path);
      assert(!/noindex/i.test(details.robots || ''), path);
      if (path !== '/blog') assert(details.graphs.length, path);
      assert(!html.includes('{{PUBLIC_SITE_URL}}'));
      for (const link of details.links) if (link.startsWith('/') && !link.startsWith('//')) localLinks.add(link.split('#')[0]);
      metas.push({ path, title: details.title, h1: details.h1s[0], description: details.description });
    }
    const xml = await (await get('/sitemap.xml')).text();
    await page.setContent('<html></html>');
    const locations = await page.evaluate(xml => {
      const doc = new DOMParser().parseFromString(xml, 'application/xml');
      if (doc.querySelector('parsererror')) throw Error('Invalid sitemap XML');
      return [...doc.querySelectorAll('loc')].map(e => e.textContent);
    }, xml);
    assert.deepEqual(locations.sort(), paths.map(p => origin + p).sort());
    assert(!xml.includes('<lastmod>'), 'No invented modification dates');
    const robots = await (await get('/robots.txt')).text();
    for (const agent of ['OAI-SearchBot', 'ChatGPT-User', 'GPTBot', 'Googlebot', 'Bingbot']) assert(robots.includes('User-agent: ' + agent));
    for (const path of ['/api/', '/archive.html', '/account-security.html', '/honours', '/mcp']) assert(robots.includes('Disallow: ' + path));
    assert(robots.includes('Sitemap: ' + origin + '/sitemap.xml'));
    const llms = await (await get('/llms.txt')).text();
    for (const path of guidePaths) assert(llms.includes(origin + path));
    for (const path of ['/archive.html', '/account-security.html', '/honours.html?demo=1', '/honours-preview', '/api/trophies']) {
      const response = await get(path); assert(/noindex/i.test(response.headers.get('x-robots-tag') || ''), path);
    }
    assert.equal((await get('/api/trophies')).status, 401);
    assert.equal((await get('/aeo-does-not-exist')).status, 404);
    assert.equal((await get('/blog/aeo-does-not-exist')).status, 404);
    assert.equal((await get('/blog?page=999')).status, 404);
    assert.equal((await get('/blog?page=invalid')).status, 404);
    assert.equal((await get('/blog', { method: 'HEAD' })).status, 200);
    assert.equal((await get('/qa-indexnow-key-2026.txt')).status, 200);
    assert.equal((await (await get('/qa-indexnow-key-2026.txt')).text()).trim(), 'qa-indexnow-key-2026');
    for (const path of guidePaths) {
      const response = await get(path + '/?utm_source=chatgpt.com');
      assert.equal(response.status, 301); assert.equal(response.headers.get('location'), path + '?utm_source=chatgpt.com');
    }
    for (const link of localLinks) assert([200, 301, 302].includes((await get(link)).status), 'Broken link: ' + link);
    console.log('PASS sitemap, robots, llms, metadata, JSON-LD, private routes, aliases and internal links');

    // Real browser layout and progressive enhancement.
    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 960 });
      for (const path of ['/', ...guidePaths]) {
        await page.goto(base + path);
        await page.evaluate(() => document.fonts.ready);
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Horizontal overflow ' + path + ' at ' + width);
        if (['/', '/electronic-honours-boards', '/faq'].includes(path)) await page.screenshot({ path: `${output}/${path === '/' ? 'home' : path.slice(1)}-${width}.png`, fullPage: true });
      }
    }
    const web = await browser.newPage();
    await web.addInitScript(() => {
      window.registeredTools = [];
      Object.defineProperty(document, 'modelContext', { value: { registerTool: tool => { window.registeredTools.push(tool); } } });
    });
    await web.goto(base + '/how-it-works');
    await web.waitForFunction(() => window.registeredTools.length === 4);
    const results = await web.evaluate(async () => Promise.all(window.registeredTools.map(t => t.execute({}))));
    assert.equal(results.length, 4); assert(JSON.stringify(results).includes('engraved'));
    console.log('PASS desktop/mobile layouts and four WebMCP executions');

    // SDK protocol handshake, list, calls and invalid method (not a hand-written MCP imitation).
    let id = 0;
    async function rpc(method, params) {
      const response = await get('/mcp', { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' }, body: JSON.stringify({ jsonrpc: '2.0', id: ++id, method, params }) });
      assert.equal(response.status, 200, method);
      const body = await response.text();
      return response.headers.get('content-type').includes('text/event-stream') ? JSON.parse(body.split('\n').find(l => l.startsWith('data: ')).slice(6)) : JSON.parse(body);
    }
    const init = await rpc('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'aeo-qa', version: '1.0' } });
    assert(init.result.capabilities.tools);
    const list = await rpc('tools/list', {});
    assert.equal(list.result.tools.length, 4);
    for (const tool of list.result.tools) {
      assert.equal(tool.annotations.readOnlyHint, true);
      assert.equal(tool.annotations.destructiveHint, false);
      const call = await rpc('tools/call', { name: tool.name, arguments: {} });
      assert(!call.error && !call.result.isError, tool.name);
    }
    const invalid = await rpc('tools/call', { name: 'delete_customer', arguments: {} });
    assert(invalid.error || invalid.result.isError);
    assert.equal((await get('/mcp', { method: 'POST', headers: { Origin: 'https://hostile.example' } })).status, 403);
    assert.equal((await get('/mcp', { method: 'POST', body: 'x'.repeat(17000) })).status, 413);
    console.log('PASS MCP handshake, metadata, tool execution, unknown tool, origin and body limits');
    fs.writeFileSync(output + '/page-metadata.json', JSON.stringify(metas, null, 2));
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
