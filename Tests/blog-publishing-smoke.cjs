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
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'trophy-publishing-http-'));
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
  async function send(body, authenticated = true) {
    const response = await fetch(base + '/api/webhooks/writesonic', {
      method: 'POST', headers: { 'Content-Type': 'application/json', ...(authenticated ? { Authorization: 'Bearer ' + token } : {}) },
      body: JSON.stringify(body), signal: AbortSignal.timeout(15000)
    });
    const text = await response.text();
    return { status: response.status, body: text ? JSON.parse(text) : null };
  }
  try {
    let ready = false;
    for (let attempt = 0; attempt < 80; attempt++) {
      try { ready = (await fetch(base + '/health')).ok; } catch {}
      if (ready) break;
      if (child.exitCode !== null) throw Error('App exited before ready: ' + output.slice(-1200));
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert(ready, 'Local app became ready');
    assert.equal((await send({ mode: 'test' }, false)).status, 401);
    assert.equal((await send({ mode: 'test' })).body.status, 'connected');
    const payload = { mode: 'validate', documentId: 'http-fixture', updatedAt: '2026-09-01T12:00:00Z',
      title: 'Publishing fixture', slug: 'publishing-fixture', contentHtml: '<h1>Publishing fixture</h1><p>Read the source records and check each winner.</p>',
      metaDescription: 'Local publishing fixture for verification.' };
    assert.equal((await send(payload)).body.status, 'validated');
    assert.equal((await fetch(base + '/blog/publishing-fixture')).status, 404);
    const published = await send({ ...payload, mode: 'publish' });
    assert.equal(published.status, 200); assert.equal(published.body.url, origin + '/blog/publishing-fixture');
    const response = await fetch(base + '/blog/publishing-fixture');
    const html = await response.text();
    assert.equal(response.status, 200);
    assert.equal((html.match(/<h1[ >]/g) || []).length, 1);
    assert(html.includes('BlogPosting')); assert(html.includes('rel="canonical" href="' + published.body.url + '"'));
    assert((await (await fetch(base + '/sitemap.xml')).text()).includes(published.body.url));
    assert.equal((await send({ ...payload, mode: 'publish' })).body.status, 'unchanged');
    assert.equal((await send({ ...payload, mode: 'publish', title: 'Conflicting revision' })).status, 409);
    const textPayload = { mode: 'publish', text: '# Text export fixture\n\nPhotograph the club trophies and review each engraved winner against the original record. Keep the source images available for future checks by club members.' };
    const textPublished = await send(textPayload);
    assert.equal(textPublished.status, 200);
    assert.equal(textPublished.body.status, 'published');
    assert.equal((await send(textPayload)).body.status, 'unchanged');
    const textPage = await fetch(base + '/blog/text-export-fixture');
    assert.equal(textPage.status, 200);
    const textHtml = await textPage.text();
    assert(textHtml.includes('Photograph the club trophies'));
    assert(textHtml.includes('BlogPosting'));
    assert((await (await fetch(base + '/sitemap.xml')).text()).includes(textPublished.body.url));
    assert.equal((await fetch(base + '/api/trophies')).status, 401);
    assert.equal((await fetch(base + '/api/webhooks/writesonic/other', { method: 'POST' })).status, 403);
    console.log('PASS real HTTP authentication, validation, publication, existing blog template, sitemap, retries, revision conflicts and private-route isolation.');
  } finally {
    child.kill();
    await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 5000))]);
    console.log('Isolated fixture data retained at ' + directory);
  }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
