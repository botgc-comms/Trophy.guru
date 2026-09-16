// End-to-end fixture test. Starts only a local app with an isolated temporary data directory.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
async function main() {
  const listener = net.createServer();
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise(resolve => listener.close(resolve));
  const base = 'http://127.0.0.1:' + port;
  const origin = 'https://127.0.0.1:' + port;
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'trophy-seo-headers-'));
  const token = 'local-test-only-publishing-secret-over-32';
  const child = spawn('dotnet', ['bin/Debug/net9.0/Trophy.Catalogue.dll', '--urls', base], {
    cwd: path.resolve(__dirname, '..'), windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DATA_PATH: directory,
      PUBLIC_SITE_URL: origin, BLOG_PUBLISH_TOKEN: token, INDEXNOW_ENABLED: 'false',
      BILLING_MODE: 'disabled', APP_PASSWORD: '', OPENAI_API_KEY: '', SENDGRID_API_KEY: '' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  let output = ''; child.stdout.on('data', b => output += b); child.stderr.on('data', b => output += b);
  const exited = new Promise(resolve => child.on('exit', resolve));
  child.on('error', e => { output += e.message; });
  try {
    let ready = false;
    for (let attempt = 0; attempt < 80; attempt++) {
      try { ready = (await fetch(base + '/health')).ok; } catch {}
      if (ready) break;
      if (child.exitCode !== null) throw Error('App exited before ready: ' + output.slice(-1200));
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert(ready, 'Local app became ready');
    const pages = ['/', '/blog', '/demo', '/about', '/for-golf-clubs', '/how-it-works', '/faq', '/privacy.html', '/electronic-honours-boards', '/digitise-trophy-records', '/trophy-archive-project-plan', '/privacy-and-security', '/integrations/intelligent-golf/', '/uk/how-to-catalogue-trophy-winners/', '/us/how-to-catalog-trophy-winners/'];
    for (const route of pages) {
      const response = await fetch(base + route);
      assert.equal(response.status, 200, route);
      assert.equal(response.headers.get('x-frame-options'), 'DENY', route);
      assert(response.headers.get('content-security-policy').includes("frame-ancestors 'none'"), route);
      const html = await response.text();
      const title = html.match(/<title>([^<]+)<\/title>/)[1];
      assert(title.length >= 40 && title.length <= 70, route + ': ' + title);
      for (const block of html.matchAll(/<script[^>]+type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)) JSON.parse(block[1]);
      if (route === '/') assert.equal(response.headers.get('cache-control'), 'no-cache');
      if (route === '/integrations/intelligent-golf/') {
        const logo = html.match(/<img[^>]+intelligentgolf\.png[^>]*>/)[0];
        assert(logo.includes('width="250"') && logo.includes('height="47"'));
      }
    }
    for (const asset of ['/branding.css', '/analytics.js']) {
      const response = await fetch(base + asset);
      assert.equal(response.status, 200);
      assert.match(response.headers.get('cache-control'), /public.*max-age=3600/);
      assert(response.headers.get('last-modified'), 'Static asset validators preserved');
    }
    const demo = await fetch(base + '/honours.html?demo=1');
    assert.equal(demo.status, 200);
    assert.equal(demo.headers.get('x-frame-options'), 'SAMEORIGIN');
    assert(demo.headers.get('content-security-policy').includes("frame-ancestors 'self'"));
    const privateApi = await fetch(base + '/api/trophies');
    assert.equal(privateApi.status, 401);
    assert(privateApi.headers.get('cache-control').includes('no-store'));
    console.log('PASS 15 public pages, titles, JSON-LD, security headers, asset caching, demo framing and private API cache isolation.');
  } finally {
    child.kill();
    await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 5000))]);
    console.log('Isolated fixture data retained at ' + directory);
  }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
