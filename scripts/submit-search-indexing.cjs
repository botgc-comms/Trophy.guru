// Submit public marketing URLs only, after their deployment. Node 20+; no account needed.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const origin = 'https://trophy.guru';
const key = '1a95e1c535538a960b7c30ca8d723027';
async function main() {
  const keyResponse = await fetch(origin + '/' + key + '.txt', { signal: AbortSignal.timeout(30000) });
  assert.equal(keyResponse.status, 200, 'Deploy the IndexNow ownership file before submitting.');
  assert.equal((await keyResponse.text()).trim(), key, 'The deployed ownership key must match.');
  const sitemap = await fetch(origin + '/sitemap.xml', { signal: AbortSignal.timeout(30000) });
  assert.equal(sitemap.status, 200);
  const xml = await sitemap.text();
  const urlList = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(match => match[1]);
  const publicPaths = ['/', '/uk/how-to-catalogue-trophy-winners/', '/us/how-to-catalog-trophy-winners/', '/integrations/intelligent-golf/'];
  assert.equal(urlList.length, publicPaths.length, 'Review changed sitemap contents before submission.');
  assert.equal(new Set(urlList).size, publicPaths.length);
  for (const url of urlList) {
    const parsed = new URL(url);
    assert.equal(parsed.origin, origin);
    assert(publicPaths.includes(parsed.pathname) && !parsed.search && !parsed.hash);
    const response = await fetch(url, { redirect: 'manual', signal: AbortSignal.timeout(30000) });
    assert.equal(response.status, 200, url);
    assert(!/noindex/i.test(response.headers.get('x-robots-tag') || ''), url);
    const html = await response.text();
    assert(!/<meta[^>]+name=["']robots["'][^>]+content=["'][^"']*noindex/i.test(html), url);
    assert(html.includes('rel="canonical" href="' + url + '"'), 'Canonical: ' + url);
  }
  const response = await fetch('https://api.indexnow.org/indexnow', {
    method: 'POST', headers: { 'Content-Type': 'application/json; charset=utf-8' },
    body: JSON.stringify({ host: new URL(origin).host, key, keyLocation: origin + '/' + key + '.txt', urlList }),
    signal: AbortSignal.timeout(30000)
  });
  const body = await response.text();
  assert([200, 202].includes(response.status), 'IndexNow: ' + response.status + ' ' + body);
  console.log(JSON.stringify({ submittedAt: new Date().toISOString(), status: response.status,
    result: response.status === 202 ? 'Received; ownership validation pending' : 'Received',
    urlList, note: 'Receipt is not confirmation of indexing. Google Search Console requires separate owner access.' }, null, 2));
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
