// Public read-only HTTP checks; no AI calls, accounts or customer data required.
const assert = require('node:assert/strict');
const base = (process.env.QA_BASE_URL || 'http://127.0.0.1:5197').replace(/\/$/, '');
const origin = process.env.QA_PUBLIC_ORIGIN || base;
const get = (path, options = {}) => fetch(base + path, {
  redirect: 'manual', signal: AbortSignal.timeout(30000), ...options
});

async function main() {
  for (const agent of ['OAI-SearchBot', 'ChatGPT-User', 'bingbot']) {
    for (const [path, contentType] of [['/llms.txt', 'text/plain'], ['/product-facts.json', 'application/json']]) {
      const response = await get(path, { headers: { 'User-Agent': agent } });
      assert.equal(response.status, 200, agent + ' ' + path);
      assert(response.headers.get('content-type').startsWith(contentType));
      assert(!/noindex|nosnippet/i.test(response.headers.get('x-robots-tag') || ''));
      assert.equal(response.headers.get('cache-control'), 'no-cache');
      const head = await get(path, { method: 'HEAD' });
      assert.equal(head.status, 200);
      assert.equal(head.headers.get('content-type'), response.headers.get('content-type'));
      assert.equal(await head.text(), '');
    }
  }
  const facts = await (await get('/product-facts.json')).json();
  const markdown = await (await get('/llms.txt')).text();
  assert.equal(facts.name, 'Trophy Guru');
  assert.equal(facts.url, origin + '/');
  assert.equal(facts.workflowUrl, origin + '/how-it-works');
  assert.equal(facts.faqUrl, origin + '/faq');
  assert(markdown.includes(origin + '/product-facts.json'));
  assert(markdown.includes(facts.overview));
  assert.equal(facts.workflow.length, 4);
  for (const step of facts.workflow) assert(markdown.includes(step.text));

  const faqHtml = await (await get('/faq')).text();
  const graphs = [...faqHtml.matchAll(/<script type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)]
    .map(match => JSON.parse(match[1]));
  const faqSchema = graphs.find(graph => graph['@type'] === 'FAQPage');
  assert(faqSchema, 'FAQ must be available in initial HTML without JavaScript');
  assert.deepEqual(facts.faqs, faqSchema.mainEntity.map(question => ({
    heading: question.name, text: question.acceptedAnswer.text
  })));
  for (const faq of facts.faqs) assert(markdown.includes(faq.text), faq.heading);
  for (const phrase of ['decades of old sports trophies', 'batch of trophy photographs', 'export the archive to Excel', 'photographs kept with the records']) {
    assert(facts.faqs.some(faq => faq.heading.includes(phrase)), phrase);
  }
  for (const path of ['/', '/faq', '/digitise-trophy-records', '/how-it-works']) {
    const response = await get(path);
    assert.equal(response.status, 200, path);
    const html = await response.text();
    for (const file of ['/llms.txt', '/product-facts.json']) {
      assert(new RegExp('<link rel="describedby"[^>]+href="' + origin.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + file.replace('.', '\\.') + '"').test(html), path + ' links ' + file);
    }
  }
  for (const page of facts.pages) assert(page.url.startsWith(origin + '/'));
  const robots = await (await get('/robots.txt')).text();
  assert(robots.includes('Allow: /'));
  assert(robots.includes('Disallow: /api/'));
  assert(!/Disallow:\s*\/(?:llms\.txt|product-facts\.json)/.test(robots));
  assert.equal((await get('/api/trophies')).status, 401);
  assert(/noindex/i.test((await get('/api/public/product')).headers.get('x-robots-tag')));

  if (new URL(base).hostname === '127.0.0.1') {
    // Fetch normalises Host, so use the HTTP client to test the canonical-host redirect.
    for (const path of ['/llms.txt', '/product-facts.json']) {
      const redirected = await new Promise((resolve, reject) => {
        const request = require('node:http').get(base + path, { headers: { Host: 'old-service.onrender.com' } }, response => {
          response.resume(); resolve({ status: response.statusCode, location: response.headers.location });
        });
        request.setTimeout(10000, () => request.destroy(new Error('Redirect check timed out')));
        request.on('error', reject);
      });
      assert.equal(redirected.status, 301);
      assert.equal(redirected.location, origin + path);
    }
  }
  console.log('PASS public Markdown/JSON GET and HEAD, crawler user agents, canonical host, HTML discovery links, FAQ agreement and private API exclusions');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
