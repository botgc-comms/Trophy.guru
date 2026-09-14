const test = require('node:test');
const assert = require('node:assert/strict');
const { once } = require('node:events');
const gsc = require('../scripts/search-console.cjs');

test('calendar ranges are inclusive, adjacent, and handle leap years', () => {
  assert.deepEqual(gsc.reportingPeriods(28, '2024-03-01'), {
    current: { startDate: '2024-02-03', endDate: '2024-03-01' },
    previous: { startDate: '2024-01-06', endDate: '2024-02-02' }
  });
  assert.throws(() => gsc.reportingPeriods(0));
  assert.throws(() => gsc.reportingPeriods(28, '2026-02-30'));
});
test('only Desktop clients and the exact read-only scope are accepted', () => {
  assert.throws(() => gsc.validateClient({ web: {} }));
  assert.throws(() => gsc.validateClient({ type: 'service_account' }));
  assert.throws(() => gsc.validateScope('https://www.googleapis.com/auth/webmasters'));
  assert.throws(() => gsc.validateScope(gsc.SCOPE + ' https://www.googleapis.com/auth/drive'));
  assert.doesNotThrow(() => gsc.validateScope(gsc.SCOPE));
  assert.deepEqual(gsc.validateClient({ installed: { client_id: 'example.apps.googleusercontent.com', client_secret: 'test-only', token_uri: 'https://untrusted.invalid' } }), { client_id: 'example.apps.googleusercontent.com', client_secret: 'test-only' });
});
test('properties and inspected URLs cannot target another site or carry personal query fields', () => {
  assert.throws(() => gsc.validateProperty('sc-domain:example.com'));
  for (const url of ['https://example.com/', 'https://trophy.guru.evil.test/', 'http://trophy.guru/', 'https://trophy.guru/demo?email=person@example.com', 'https://user:pass@trophy.guru/demo']) assert.throws(() => gsc.validateInspectionUrl(url));
  assert.equal(gsc.validateInspectionUrl('https://trophy.guru/demo'), 'https://trophy.guru/demo');
});
test('property detection prefers domain access and rejects unverified entries', async () => {
  const request = async () => ({ siteEntry: [
    { siteUrl: 'https://trophy.guru/', permissionLevel: 'siteRestrictedUser' },
    { siteUrl: 'sc-domain:trophy.guru', permissionLevel: 'siteOwner' },
    { siteUrl: 'sc-domain:unrelated.example', permissionLevel: 'siteOwner' }
  ] });
  assert.equal(await gsc.selectProperty('test-only', undefined, request), 'sc-domain:trophy.guru');
  assert.equal(await gsc.selectProperty('test-only', 'https://trophy.guru/', request), 'https://trophy.guru/');
  await assert.rejects(gsc.selectProperty('test-only', undefined, async () => ({ siteEntry: [{ siteUrl: 'sc-domain:trophy.guru', permissionLevel: 'siteUnverifiedUser' }] })));
});
test('reports obtain totals separately from privacy-filtered query rows', async () => {
  const calls = [];
  const request = async (_token, endpoint, body) => {
    calls.push({ endpoint, body });
    if (endpoint.endsWith('/sitemaps')) return { sitemap: [] };
    if (!body.dimensions.length) return { rows: [{ clicks: 10, impressions: 100, ctr: .1, position: 20 }] };
    return { rows: [{ keys: ['example'], clicks: 1, impressions: 5, ctr: .2, position: 10 }] };
  };
  const report = await gsc.buildReport('test-only', 'sc-domain:trophy.guru', gsc.reportingPeriods(28, '2026-09-11'), request);
  assert.equal(calls.length, 9);
  assert.equal(report.currentTotals.rows[0].impressions, 100);
  assert.equal(report.queries.rows[0].impressions, 5);
  assert(calls.filter(call => call.body).every(call => call.body.dataState === 'final' && call.body.type === 'web'));
});
test('read APIs never follow redirects and never print Google error payloads', async () => {
  let captured;
  const result = await gsc.apiRequest('test-only', gsc.API + '/sites', undefined, async (url, options) => {
    captured = options; return { ok: true, json: async () => ({ siteEntry: [] }) };
  });
  assert.deepEqual(result, { siteEntry: [] });
  assert.equal(captured.method, 'GET');
  assert.equal(captured.redirect, 'error');
  await assert.rejects(gsc.apiRequest('test-only', gsc.API + '/sites', undefined, async () => ({ ok: false, status: 403 })), /Enable the Search Console API/);
  await assert.rejects(gsc.apiRequest('test-only', 'https://untrusted.invalid', undefined));
  await assert.rejects(gsc.tokenRequest({}, async () => ({ ok: false, status: 400, json: async () => ({ error: 'invalid_grant', error_description: 'sensitive-do-not-print' }) })), error => /expired or was revoked/.test(error.message) && !error.message.includes('sensitive'));
});
test('loopback consent rejects a mismatched state before accepting the valid callback', async () => {
  const listener = gsc.createConsentListener('expected-state');
  try {
    listener.server.listen(0, '127.0.0.1');
    await once(listener.server, 'listening');
    const base = `http://127.0.0.1:${listener.server.address().port}/oauth2callback`;
    const invalid = await fetch(base + '?state=wrong&code=wrong');
    assert.equal(invalid.status, 400);
    const unicode = await fetch(base + '?state=' + encodeURIComponent('é'.repeat(14)) + '&code=wrong');
    assert.equal(unicode.status, 400);
    const valid = await fetch(base + '?state=expected-state&code=test-only-code');
    assert.equal(valid.status, 200);
    assert.equal(valid.headers.get('cache-control'), 'no-store');
    assert.equal(await listener.code, 'test-only-code');
  } finally { listener.close(); }
});
test('Windows credential encryption round-trips without storing plaintext', { skip: process.platform !== 'win32' }, () => {
  const original = Buffer.from('fake-test-token-not-a-credential');
  const encrypted = gsc.protect(original);
  assert(!encrypted.includes(original));
  assert.deepEqual(gsc.protect(encrypted, true), original);
});
