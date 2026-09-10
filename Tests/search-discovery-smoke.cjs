// Read-only HTTP audit. Run locally before release, or against the public site after release.
const assert = require('node:assert/strict');
const base = (process.env.QA_BASE_URL || 'http://127.0.0.1:5197').replace(/\/$/, '');
const origin = process.env.QA_PUBLIC_ORIGIN || base;
const local = new URL(base).hostname === '127.0.0.1';
const paths = ['/', '/integrations/intelligent-golf/', '/uk/how-to-catalogue-trophy-winners/', '/us/how-to-catalog-trophy-winners/'];
const agents = ['Googlebot', 'bingbot', 'OAI-SearchBot', 'ChatGPT-User', 'Claude-SearchBot', 'Claude-User', 'PerplexityBot', 'Applebot'];
let checks = 0;
function ok(label) { console.log('PASS ' + label); checks++; }
async function get(path, options = {}) {
  return fetch(base + path, { redirect: 'manual', signal: AbortSignal.timeout(30000), ...options });
}
async function alternateHost(path) {
  // Node fetch normalises Host; use HTTP directly to exercise proxy-host routing.
  return new Promise((resolve, reject) => {
    const req = require('node:http').get(base + path, { headers: { Host: 'old-service.onrender.com' } }, response => {
      response.resume(); resolve({ status: response.statusCode, location: response.headers.location });
    });
    req.setTimeout(10000, () => req.destroy(new Error('Host-routing check timed out')));
    req.on('error', reject);
  });
}
async function main() {
  const documents = new Map();
  for (const path of paths) {
    const response = await get(path);
    assert.equal(response.status, 200, path);
    assert(!/noindex|nosnippet/i.test(response.headers.get('x-robots-tag') || ''));
    const html = await response.text(); documents.set(path, html);
    assert(!html.includes('{{PUBLIC_SITE_URL}}'));
    assert(html.includes('rel="canonical" href="' + origin + path + '"'));
    assert.equal(response.headers.get('link'), '<' + origin + path + '>; rel="canonical"');
    assert(!/<meta[^>]+name="robots"[^>]+content="[^"]*(noindex|nosnippet)/i.test(html));
    assert(html.includes('property="og:site_name" content="Trophy Guru"'));
    assert.equal((html.match(/<h1[ >]/g) || []).length, 1);
    assert((html.match(/<title>([^<]+)<\/title>/) || [])[1]?.includes('Trophy Guru'));
    const graphs = [...html.matchAll(/<script type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)];
    assert(graphs.length > 0);
    for (const block of graphs) JSON.parse(block[1]);
    const head = await get(path, { method: 'HEAD' });
    assert.equal(head.status, 200); assert.equal(await head.text(), '');
    assert.equal(head.headers.get('link'), response.headers.get('link'));
    ok(path + ': readable HTML, valid JSON-LD, canonical, indexable GET/HEAD');
  }
  const home = documents.get('/');
  const graph = JSON.parse([...home.matchAll(/<script type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)][0][1]);
  const app = graph['@graph'].find(item => item['@type'] === 'WebApplication');
  const prices = [...home.matchAll(/<article class="price-card[^>]*>([\s\S]*?)<\/article>/g)].map(match => {
    const amount = match[1].match(/<strong>([^<]+)<\/strong>/)[1];
    return amount === 'Free' ? 0 : Number(amount.replace(/[^0-9.]/g, ''));
  });
  assert.deepEqual(app.offers.map(offer => Number(offer.price)), prices);
  assert(home.includes('AI-assisted transcription'));
  assert(home.includes('electronic honours board'));
  ok('Service description and pricing agree with visible homepage content');
  for (const path of paths.filter(path => /^\/(uk|us)\//.test(path))) {
    const html = documents.get(path);
    for (const [lang, target] of [['en-GB', paths[2]], ['en-US', paths[3]], ['x-default', paths[2]]]) {
      assert(html.includes('hreflang="' + lang + '" href="' + origin + target + '"'));
    }
  }
  ok('Both regional guides have matching, reciprocal language references');
  const robots = await get('/robots.txt'); assert.equal(robots.status, 200);
  const rules = await robots.text();
  assert(rules.includes('User-agent: *')); assert(rules.includes('Allow: /')); assert(rules.includes('Disallow: /api/'));
  assert(rules.includes('Sitemap: ' + origin + '/sitemap.xml')); assert(!rules.includes('{{'));
  const sitemap = await get('/sitemap.xml'); assert.equal(sitemap.status, 200);
  const xml = await sitemap.text();
  const urls = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(match => match[1]);
  const extraPaths = ['/privacy.html', '/blog', '/electronic-honours-boards', '/for-golf-clubs', '/digitise-trophy-records', '/how-it-works', '/faq', '/about', '/privacy-and-security'];
  assert.deepEqual(urls.sort(), [...paths, ...extraPaths].map(path => origin + path).sort());
  assert(!xml.includes('<lastmod>'), 'Build timestamps must not masquerade as content updates');
  ok('Sitemap lists canonical public pages; robots advertises it');
  for (const agent of agents) {
    for (const path of ['/', '/robots.txt', '/sitemap.xml']) {
      const response = await get(path, { headers: { 'User-Agent': agent } });
      assert.equal(response.status, 200, agent + ' ' + path);
      assert(!/noindex/i.test(response.headers.get('x-robots-tag') || ''));
    }
    ok(agent + ': public pages and discovery documents accessible from this test network');
  }
  for (const [from, to] of [['/index.html?utm_source=qa','/?utm_source=qa'], ['/UK/HOW-TO-CATALOGUE-TROPHY-WINNERS/','/uk/how-to-catalogue-trophy-winners/'], ['/us/how-to-catalog-trophy-winners/index.html','/us/how-to-catalog-trophy-winners/']]) {
    const response = await get(from); assert.equal(response.status, 301); assert.equal(response.headers.get('location'), to);
  }
  ok('Aliases and mixed-case public paths redirect to one URL; query parameters survive');
  assert.equal((await get('/search-discovery-nonexistent-page')).status, 404);
  for (const path of ['/archive.html', '/account-security.html', '/honours.html?demo=1']) {
    const response = await get(path); const html = await response.text();
    assert(/noindex/i.test(response.headers.get('x-robots-tag') || '') || /<meta[^>]+name="robots"[^>]+content="[^"]*noindex/i.test(html), path);
  }
  ok('Missing pages return 404; account and demo pages remain excluded');
  if (local) {
    const alias = await alternateHost('/index.html?source=alias');
    assert.equal(alias.status, 301); assert.equal(alias.location, origin + '/?source=alias');
    const health = await alternateHost('/health'); assert.equal(health.status, 200);
    assert(home.includes('name="google-site-verification" content="qa-google-proof"'));
    assert(home.includes('name="msvalidate.01" content="qa-bing-proof"'));
    ok('Alternate host redirects only public discovery pages; owner verification tags are rendered');
  }
  console.log(checks + ' search-discovery checks passed. Crawler identity/IP allowlisting and actual indexing require provider-side evidence.');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
