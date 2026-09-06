// Requires a separate local QA data directory; never targets the original archive.
const { chromium, request } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs'); const path = require('node:path'); const http = require('node:http');
const assert = require('node:assert/strict');
const base = process.env.QA_BASE_URL || 'http://127.0.0.1:5192';
const root = process.env.QA_DATA_PATH; const output = process.env.QA_OUTPUT_PATH;
if (new URL(base).hostname !== '127.0.0.1' || !root || !path.basename(root).startsWith('trophy-launch-qa-') || !output) throw Error('Separate loopback QA environment required');
(async () => {
  fs.mkdirSync(output, {recursive:true});
  const checks=[]; const ok=s=>{checks.push(s);console.log('PASS '+s);};
  const owner = await request.newContext({baseURL:base,extraHTTPHeaders:{Origin:base}});
  async function api(url, method='GET', data) { const r=await owner.fetch(url,{method,...(data===undefined?{}:{data})}); assert(r.ok(),`${method} ${url}: ${r.status()} ${await r.text()}`); return r.json(); }
  const email=`security-${Date.now()}@archive.example`;
  await api('/api/auth/signup','POST',{displayName:'Security QA owner',email,password:'FixturePassword123!'});
  await api('/api/club','PUT',{name:'Security QA club',sport:'Golf',country:'United Kingdom'});
  const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jx0sAAAAASUVORK5CYII=','base64');
  assert((await owner.post('/api/club/logo',{multipart:{logo:{name:'fixture.png',mimeType:'image/png',buffer:png}}})).ok());
  const variants=['/api/trophies/missing/IMAGES/','/api/trophies/missing/ANALYSE','/API/TROPHIES/missing/ILLUSTRATION/','/api/trophies/missing/illustration/BACKGROUND/','/api/trophies/missing/TROPHY-PHOTOS/','/API/MEMBERS/IMPORT/'];
  for(const url of variants){const r=await owner.post(url,{data:{}});assert.equal(r.status(),403,url);assert.equal((await r.json()).error,'email_verification_required');}
  ok('Uppercase/trailing-slash upload, AI and import routes require verification before handling data');
  const huge=await owner.post('/api/trophies',{data:{name:'A cup',secondaryName:'x'.repeat(140000)}});assert.equal(huge.status(),413);
  const cookies=(await owner.storageState()).cookies.map(c=>`${c.name}=${c.value}`).join('; ');
  const chunkedStatus=await new Promise((resolve,reject)=>{const req=http.request(base+'/api/trophies',{method:'POST',headers:{Origin:base,Cookie:cookies,'Content-Type':'application/json','Transfer-Encoding':'chunked'}},res=>{res.resume();res.on('end',()=>resolve(res.statusCode));});req.on('error',reject);req.write('{"name":"A cup","secondaryName":"');req.write('x'.repeat(140000));req.end('"}');});
  assert.equal(chunkedStatus,413);ok('Oversized JSON is rejected with known length and chunked transfer');
  const mail=fs.readdirSync(path.join(root,'mail')).map(n=>fs.readFileSync(path.join(root,'mail',n),'utf8')).find(s=>s.includes(email));
  const token=mail.replace(/=\r?\n/g,'').replace(/=3D/g,'=').match(/#verify=([A-Za-z0-9_-]+)/)[1];await api('/api/auth/verify-email','POST',{token});
  const created=[];for(let i=0;i<5;i++)created.push(await api('/api/trophies','POST',{name:'Fixture '+i,code:'QA'+i}));
  assert.equal((await owner.post('/api/trophies',{data:{name:'Excess trophy'}})).status(),402);
  assert.equal((await api('/api/trophies')).items.length,5);
  const trophy=created[0].trophy;
  await api(`/api/trophies/${trophy.id}/winners`,'POST',{year:2024,name:'=1+1',reviewState:'confirmed'});
  const csv=await owner.get('/api/export.csv');assert(csv.ok());assert((await csv.text()).includes('"\'=1+1"'));
  assert.equal((await api(`/api/trophies/${trophy.id}`)).trophy.winners[0].name,'=1+1');
  ok('Free draft count is bounded and CSV export protects formulas without altering archive text');
  const browser=await chromium.launch({headless:true,channel:'msedge'}); const errors=[];
  async function pageFor(locale,timezoneId,state){const ctx=await browser.newContext({locale,timezoneId,storageState:state,viewport:{width:1440,height:1000},reducedMotion:'reduce'});const page=await ctx.newPage();page.on('pageerror',e=>errors.push(e.message));return{ctx,page};}
  for(const [locale,zone,expected] of [['en-US','America/New_York','us'],['en-US','Europe/London','uk'],['en-GB','Europe/London','uk']]){
    const {ctx,page}=await pageFor(locale,zone);const response=await page.goto(base+'/');
    const guide=page.locator('a[data-regional-guide]');await guide.waitFor();assert.equal(await guide.count(),1);assert.equal(await guide.getAttribute('data-guide-region'),expected);
    const policy=response.headers()['content-security-policy'];assert(!policy.includes("script-src 'self' 'unsafe-inline'"));
    const actualNonce=await page.locator('script[type="application/ld+json"]').first().evaluate(el=>el.nonce);assert(actualNonce && policy.includes(`'nonce-${actualNonce}'`));
    assert.equal(await page.locator('.illustration-gallery figcaption').count(),0);
    for(const img of await page.locator('.illustration-gallery img').all())assert(!/Challenge|Lookers|Foursomes|Committee/.test(await img.getAttribute('alt')));
    if(expected==='uk') { const reject=page.getByRole('button',{name:'Reject analytics',exact:true});if(await reject.isVisible())await reject.click();await page.locator('#illustrations').scrollIntoViewIfNeeded();await page.waitForFunction(()=>[...document.querySelectorAll('.illustration-gallery img')].every(img=>img.complete&&img.naturalWidth>0));await page.locator('#illustrations').screenshot({path:path.join(output,'illustration-gallery-desktop.png')});await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.locator('#illustrations').screenshot({path:path.join(output,'illustration-gallery-mobile.png')}); }
    await page.goto(base+'/us/how-to-catalog-trophy-winners/');assert.equal(await page.locator('[data-regional-guide]').getAttribute('data-guide-region'),'us');assert.equal(await page.locator('.guide-region-switch').count(),0);
    await ctx.close();
  }
  ok('One guide is selected for UK/US visitors; explicit guide links are respected; gallery names are removed');
  const {ctx,page}=await pageFor('en-US','America/New_York',await owner.storageState());
  await page.goto(base+'/');await page.waitForFunction(()=>document.querySelector('[data-regional-guide]').dataset.guideRegion==='uk');
  const archive=await page.goto(base+'/archive.html');await page.locator('#login-screen').waitFor({state:'hidden'});
  const policy=archive.headers()['content-security-policy'];assert(policy.includes("script-src 'self';"));assert(!policy.includes('script-src \'self\' \'unsafe-inline\''));assert.equal(await page.locator('script[src*="analytics"]').count(),0);
  await page.locator('#header-plan-button').click();await page.locator('#billing-intelligent-golf').waitFor();await page.locator('#plan-dialog .commercial-dialog-close').click();
  await page.screenshot({path:path.join(output,'hardened-archive-desktop.png'),fullPage:true});
  ok('Club country overrides browser fallback and the private archive works under its stricter script policy');
  assert.deepEqual(errors,[]);ok('No JavaScript errors on tested public or private screens');
  fs.writeFileSync(path.join(output,'security-smoke-results.json'),JSON.stringify({base,checks},null,2));
  await ctx.close();await browser.close();await owner.dispose();
})().catch(e=>{console.error(e);process.exit(1);});
