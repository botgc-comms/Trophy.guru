// Local full-application routing test; no external analytics is sent.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
(async () => {
  const listener = net.createServer();
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise(resolve => listener.close(resolve));
  const base = 'http://127.0.0.1:' + port;
  const origin = 'https://127.0.0.1:' + port;
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'trophy-writesonic-test-'));
  const child = spawn('dotnet', ['bin/Debug/net9.0/Trophy.Catalogue.dll', '--urls', base], {
    windowsHide: true, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DATA_PATH: directory,
      PUBLIC_SITE_URL: origin, WRITESONIC_ANALYTICS_API_KEY: '', WRITESONIC_ANALYTICS_ENABLED: 'false',
      LINKARTEMIS_ENABLED: 'false', INDEXNOW_ENABLED: 'false', OPENAI_API_KEY: '', SENDGRID_API_KEY: '' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let logs = ''; child.stdout.on('data', b => logs += b); child.stderr.on('data', b => logs += b);
  const exited = new Promise(resolve => child.on('exit', resolve));
  try {
    let health;
    for (let i = 0; i < 80; i++) {
      try { health = await (await fetch(base + '/health')).json(); break; } catch {}
      if (child.exitCode !== null) throw Error(logs.slice(-1200));
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert.equal(health?.writesonicAnalytics.enabled, false);
    const headers = { 'Content-Type': 'application/json', Origin: origin, Referer: origin + '/for-golf-clubs' };
    const post = (body, extra = {}) => fetch(base + '/api/public/analytics/visit', {
      method: 'POST', headers: { ...headers, ...extra }, body: JSON.stringify(body)
    });
    assert.equal((await post({ path: '/for-golf-clubs', referrer: 'https://chatgpt.com/', consent: true })).status, 204);
    assert.equal((await post({ path: '/for-golf-clubs', consent: false })).status, 400);
    assert.equal((await post({ path: '/for-golf-clubs', consent: true }, { Origin: 'https://attacker.example' })).status, 403);
    assert.equal((await post({ path: '/archive.html', consent: true })).status, 400);
    assert.equal((await post({ path: '/for-golf-clubs?token=secret', consent: true })).status, 400);
    assert.equal((await post({ path: '/for-golf-clubs', consent: true, referrer: 'x'.repeat(5000) })).status, 413);
    const page = await (await fetch(base + '/for-golf-clubs')).text();
    assert(page.includes('/analytics.js?v=20260928-1'));
    assert.equal((await fetch(base + '/api/trophies')).status, 401);
    console.log('PASS live local pipeline: public beacon, consent, origin, private paths, body limit, versioned assets, private API auth');
  } finally {
    child.kill(); await exited;
    // Remove only this test-created temporary directory.
    assert(directory.startsWith(path.join(os.tmpdir(), 'trophy-writesonic-test-')));
    fs.rmSync(directory, { recursive: true, force: true });
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
