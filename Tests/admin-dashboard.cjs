// Self-contained loopback test. Creates only synthetic accounts and uploads in a temporary directory.
const assert = require('node:assert/strict');
const fs = require('node:fs'); const path = require('node:path'); const os = require('node:os');
const crypto = require('node:crypto'); const {spawn} = require('node:child_process');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'trophy-admin-qa-'));
const base = 'http://127.0.0.1:5298', password = 'FixtureOnlyPassword123!';
const output = path.resolve('outputs/aeo-admin'); fs.mkdirSync(output,{recursive:true});
function hash() { const salt=crypto.randomBytes(16), prefix=Buffer.alloc(13); prefix[0]=1;prefix.writeUInt32BE(2,1);prefix.writeUInt32BE(100000,5);prefix.writeUInt32BE(16,9);return Buffer.concat([prefix,salt,crypto.pbkdf2Sync(password,salt,100000,32,'sha512')]).toString('base64'); }
const now='2026-09-10T10:00:00Z';
const accounts=[['admin','Owner','owner@example.test',true,null],['customer','Club customer','customer@example.test',true,'club-a'],['pending','Pending <script>alert(1)</script>','pending@example.test',false,null]].map(([id,displayName,email,verified,clubId])=>({id,displayName,email,normalizedEmail:email.toUpperCase(),passwordHash:hash(),emailVerifiedAt:verified?now:null,clubId,role:'owner',createdAt:now,securityVersion:1}));
fs.writeFileSync(path.join(root,'identity.json'),JSON.stringify({accounts,clubs:[{id:'club-a',name:'Fixture Golf Club',sport:'Golf',country:'UK',logoStoredName:'logo.png',logoContentType:'image/png',createdAt:now}]}));
const clubRoot=path.join(root,'clubs','club-a');fs.mkdirSync(clubRoot,{recursive:true});
const image={id:'evidence-one',originalName:'Engraving <img src=x>.png',contentType:'image/png',kind:'photo',uploadedAt:now};
const photo={...image,id:'photo-one',originalName:'Whole trophy.png'};
fs.writeFileSync(path.join(clubRoot,'catalogue-state.json'),JSON.stringify({version:9,trophies:[{id:'cup',name:'Fixture Championship Cup',category:'Golf',evidence:[image],trophyPhotos:[photo],winners:[]}]}));
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jx0sAAAAASUVORK5CYII=','base64');
for(const [dir,id] of [['uploads','evidence-one'],['trophy-photos','photo-one']]){fs.mkdirSync(path.join(clubRoot,dir,'cup'),{recursive:true});fs.writeFileSync(path.join(clubRoot,dir,'cup',id+'.png'),png);}
let server, logs='', browser;
const ok = s => console.log('PASS '+s);
async function start(email='owner@example.test',environment='Development') {
 server=spawn('dotnet',['bin/Debug/net9.0/Trophy.Catalogue.dll'],{cwd:process.cwd(),windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:environment,ASPNETCORE_URLS:base,PUBLIC_SITE_URL:base,DATA_PATH:root,SITE_ADMIN_EMAIL:email,INDEXNOW_ENABLED:'false',SKIP_SEED_CATALOGUE:'true',OPENAI_API_KEY:'',STRIPE_SECRET_KEY:'',SMTP_HOST:'',APP_PASSWORD:''},stdio:['ignore','pipe','pipe']});
 server.stdout.on('data',b=>logs+=b);server.stderr.on('data',b=>logs+=b);
 for(let i=0;i<100;i++){try{if((await fetch(base+'/admin')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('Server failed: '+logs);
}
async function stop(){if(server&&server.exitCode===null){const ended=new Promise(r=>server.once('exit',r));server.kill();await ended;}}
async function api(url,{method='GET',cookie='',data,origin=base}={}) {return fetch(base+url,{method,headers:{Origin:origin,...(cookie?{Cookie:cookie}:{}),...(data?{'Content-Type':'application/json'}:{})},...(data?{body:JSON.stringify(data)}:{})});}
const login=(email='owner@example.test',extra={})=>api('/admin/login',{method:'POST',data:{email,password},...extra});
const cookie=r=>r.headers.getSetCookie().map(s=>s.split(';')[0]).join('; ');
(async()=>{try{
 await start();
 const page=await api('/admin');assert.equal(page.headers.get('cache-control'),'no-store');assert.match(page.headers.get('x-robots-tag'),/noindex/);assert(!(await page.text()).includes('google-analytics'));ok('Unlisted login has no-store, noindex and no analytics');
 for(const url of ['/admin/data/registrations','/admin/data/clubs/club-a','/admin/data/clubs/club-a/trophies/cup/evidence/evidence-one','/ADMIN/DATA/registrations/'])assert.equal((await api(url)).status,401,url);
 ok('Anonymous requests cannot read registrations, galleries or originals, including route variants');
 const ordinary=await api('/api/auth/login',{method:'POST',data:{email:'customer@example.test',password}});assert.equal(ordinary.status,200);
 assert.equal((await api('/admin/data/registrations',{cookie:cookie(ordinary)})).status,401);
 assert.equal((await login('customer@example.test')).status,401);
 assert.equal((await login('pending@example.test')).status,401);
 assert.equal((await login('owner@example.test',{data:{email:'owner@example.test',password:'wrong'}})).status,401);
 assert.equal((await login('owner@example.test',{origin:'https://evil.example'})).status,403);
 ok('Club owners, ordinary sessions, wrong passwords and cross-origin logins are denied');
 const signed=await login();assert.equal(signed.status,200);let adminCookie=cookie(signed);
 assert.match(signed.headers.get('set-cookie'),/httponly/i);assert.match(signed.headers.get('set-cookie'),/samesite=strict/i);assert(!/expires=/i.test(signed.headers.get('set-cookie')));
 const list=await api('/admin/data/registrations',{cookie:adminCookie});const text=await list.text();assert.equal(JSON.parse(text).length,3);assert(!/passwordHash|securityVersion|token|storedName/i.test(text));
 const gallery=await api('/admin/data/clubs/club-a',{cookie:adminCookie});const data=await gallery.json();assert.equal(data.trophies[0].images.length,2);
 for(const i of data.trophies[0].images){const img=await api(i.url,{cookie:adminCookie});assert.equal(img.status,200);assert.equal(img.headers.get('cache-control'),'no-store');assert.deepEqual(Buffer.from(await img.arrayBuffer()),png);}
 assert.equal((await api('/admin/data/clubs/missing',{cookie:adminCookie})).status,404);
 assert.equal((await api('/admin/data/clubs/club-a/trophies/missing/evidence/evidence-one',{cookie:adminCookie})).status,404);
 assert.equal((await api('/admin/data/clubs/club-a/trophies/cup/invalid/evidence-one',{cookie:adminCookie})).status,404);
 ok('Owner sees minimal registration details and both upload categories through protected image routes');
 browser=await chromium.launch({headless:true});
 const context=await browser.newContext();const p=await context.newPage();const errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.goto(base+'/admin');await p.locator('input[name=email]').fill('owner@example.test');await p.locator('input[name=password]').fill(password);await p.locator('#login button').click();await p.locator('#dashboard').waitFor({state:'visible'});
 assert.equal(await p.locator('#registrations tr').count(),3);assert.equal(await p.locator('#registrations script').count(),0);
 await p.getByRole('button',{name:'View uploads'}).click();await p.locator('#gallery').waitFor({state:'visible'});assert.equal(await p.locator('.photos img').count(),2);await p.waitForFunction(()=>[...document.querySelectorAll('.photos img')].every(i=>i.complete&&i.naturalWidth>0));
 for(const width of [1400,390]){await p.setViewportSize({width,height:1000});await p.screenshot({path:path.join(output,'dashboard-'+width+'.png'),fullPage:true});assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);}
 await p.locator('#search').fill('no match');assert.match(await p.locator('#registrations').innerText(),/No registrations/);await p.locator('#search').fill('customer');assert.equal(await p.locator('#registrations tr').count(),1);
 await p.locator('#logout').click();await p.locator('#login-panel').waitFor({state:'visible'});assert.equal(await p.locator('#trophies').innerText(),'');assert.equal((await context.request.get(base+'/admin/data/registrations')).status(),401);
 assert.deepEqual(errors,[]);ok('Desktop/mobile login, search, safe text rendering, gallery and logout work');
 const ownNormal=await api('/api/auth/login',{method:'POST',data:{email:'owner@example.test',password}});assert.equal(ownNormal.status,200);
 assert.equal((await api('/admin/data/registrations',{cookie:cookie(ownNormal)})).status,401);
 assert.equal((await api('/api/auth/logout-all',{method:'POST',cookie:cookie(ownNormal)})).status,200);
 assert.equal((await api('/admin/data/registrations',{cookie:adminCookie})).status,401);ok('Revoking account sessions invalidates admin cookie');
 const fresh=await login();assert.equal(fresh.status,200);adminCookie=cookie(fresh);
 assert.equal((await api('/admin/logout',{method:'POST',cookie:adminCookie,origin:'https://evil.example'})).status,403);
 assert.equal((await api('/admin/data/registrations',{cookie:adminCookie})).status,200);
 await stop();await start('pending@example.test');assert.equal((await api('/admin/data/registrations',{cookie:adminCookie})).status,401);assert.equal((await login('pending@example.test')).status,401);ok('Configured but unverified email cannot administer');
 await stop();await start('');assert.equal((await login()).status,401);assert.equal((await api('/admin/data/registrations',{cookie:adminCookie})).status,401);ok('Missing configuration disables access');
 await stop();await start('owner@example.test','Production');const prod=await login();assert.equal(prod.status,200);assert.match(prod.headers.get('set-cookie'),/__Host-trophy_guru_admin=/);assert.match(prod.headers.get('set-cookie'),/; secure/i);ok('Production session uses secure host-only cookie');
 for(let i=0;i<12;i++)await login('customer@example.test');assert.equal((await login()).status,429);ok('Admin login rate limit enforced');
 console.log('All admin dashboard checks passed.');
}finally{if(browser)await browser.close();await stop();fs.writeFileSync(path.join(output,'server.log'),logs);}})().catch(e=>{console.error(e);process.exitCode=1;});
