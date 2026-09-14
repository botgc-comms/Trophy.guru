const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const assert = require('node:assert/strict');

const SCOPE = 'https://www.googleapis.com/auth/webmasters.readonly';
const PROPERTIES = ['sc-domain:trophy.guru', 'https://trophy.guru/'];
const STORE = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), '.local', 'share'), 'TrophyGuru', 'SearchConsole');
const AUTH_FILE = path.join(STORE, 'google-readonly.dpapi');
const API = 'https://www.googleapis.com/webmasters/v3';

function protect(value, decrypt = false) {
  assert.equal(process.platform, 'win32', 'This credential store uses Windows user encryption.');
  const operation = decrypt ? 'Unprotect' : 'Protect';
  const command = `Add-Type -AssemblyName System.Security; $inputBytes = [Convert]::FromBase64String([Console]::In.ReadToEnd().Trim()); $resultBytes = [Security.Cryptography.ProtectedData]::${operation}($inputBytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser); [Console]::Out.Write([Convert]::ToBase64String($resultBytes))`;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], {
    input: Buffer.from(value).toString('base64'), encoding: 'utf8', windowsHide: true, timeout: 15000
  });
  assert.equal(result.status, 0, 'Windows credential encryption failed; no credentials were printed.');
  return Buffer.from(result.stdout.trim(), 'base64');
}
function saveCredentials(credentials) {
  fs.mkdirSync(STORE, { recursive: true });
  const encrypted = protect(Buffer.from(JSON.stringify(credentials)));
  const temporary = AUTH_FILE + '.tmp';
  fs.writeFileSync(temporary, encrypted, { mode: 0o600 });
  fs.renameSync(temporary, AUTH_FILE);
}
function loadCredentials() {
  assert(fs.existsSync(AUTH_FILE), 'Not connected. Run auth --client with your downloaded Desktop OAuth client JSON.');
  return JSON.parse(protect(fs.readFileSync(AUTH_FILE), true).toString('utf8'));
}
function validateClient(document) {
  const client = document.installed;
  assert(client && typeof client.client_id === 'string' && client.client_id.endsWith('.apps.googleusercontent.com'), 'Download a Desktop app OAuth client JSON, not a web client or service-account key.');
  assert(typeof client.client_secret === 'string' && client.client_secret.length > 0, 'The Desktop client secret is missing.');
  return { client_id: client.client_id, client_secret: client.client_secret };
}
function validateScope(scope) {
  assert.equal(scope, SCOPE, 'Google did not grant exactly the requested read-only Search Console permission.');
}
async function tokenRequest(parameters, fetcher = fetch) {
  const response = await fetcher('https://oauth2.googleapis.com/token', {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams(parameters), signal: AbortSignal.timeout(30000), redirect: 'error'
  });
  const result = await response.json();
  if (!response.ok) throw new Error(result.error === 'invalid_grant'
    ? 'Google access expired or was revoked. Run auth again. Testing-mode consent can expire after seven days.'
    : `Google token request failed (HTTP ${response.status}). Check the OAuth client configuration.`);
  assert(typeof result.access_token === 'string' && result.access_token, 'Google returned no access token.');
  if (result.scope) validateScope(result.scope);
  return result;
}
function createConsentListener(expectedState, timeoutMs = 300000) {
  let resolveCode, rejectCode;
  const code = new Promise((resolve, reject) => { resolveCode = resolve; rejectCode = reject; });
  // Attach immediately so a timeout while the browser is being opened is handled.
  code.catch(() => {});
  const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Content-Type', 'text/plain; charset=utf-8');
    response.setHeader('Content-Security-Policy', "default-src 'none'; frame-ancestors 'none'");
    if (request.method !== 'GET' || url.pathname !== '/oauth2callback') { response.writeHead(404); response.end('Not found.'); return; }
    const supplied = url.searchParams.get('state') || '';
    const stateMatches = Buffer.byteLength(supplied) === Buffer.byteLength(expectedState) && crypto.timingSafeEqual(Buffer.from(supplied), Buffer.from(expectedState));
    if (!stateMatches) { response.writeHead(400); response.end('Invalid sign-in state.'); return; }
    if (url.searchParams.has('error')) { response.writeHead(400); response.end('Google authorisation was not completed. Return to Codex.'); rejectCode(new Error('Google authorisation was declined or failed.')); return; }
    const authorizationCode = url.searchParams.get('code');
    if (!authorizationCode) { response.writeHead(400); response.end('Missing authorisation code.'); return; }
    response.end('Google consent received. You can close this tab and return to Codex to verify the connection.');
    resolveCode(authorizationCode);
  });
  const timer = setTimeout(() => { rejectCode(new Error('Sign-in timed out after five minutes. Run auth again.')); server.close(); }, timeoutMs);
  return { server, code, close: () => { clearTimeout(timer); server.close(); } };
}
async function authenticate(clientPath, openBrowser = true) {
  const client = validateClient(JSON.parse(fs.readFileSync(path.resolve(clientPath), 'utf8').replace(/^\uFEFF/, '')));
  const state = crypto.randomBytes(32).toString('base64url');
  const verifier = crypto.randomBytes(48).toString('base64url');
  const listener = createConsentListener(state);
  try {
    await new Promise((resolve, reject) => { listener.server.once('error', reject); listener.server.listen(0, '127.0.0.1', resolve); });
    const redirect = `http://127.0.0.1:${listener.server.address().port}/oauth2callback`;
    const consent = new URL('https://accounts.google.com/o/oauth2/v2/auth');
    consent.search = new URLSearchParams({ client_id: client.client_id, redirect_uri: redirect, response_type: 'code',
      scope: SCOPE, state, access_type: 'offline', prompt: 'consent', code_challenge_method: 'S256',
      code_challenge: crypto.createHash('sha256').update(verifier).digest('base64url') }).toString();
    console.log('Approve read-only Search Console access in your browser. No password is entered into this tool.');
    console.log(consent.toString());
    if (openBrowser) spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', 'Start-Process -FilePath $env:TROPHYGURU_CONSENT_URL'], {
      env: { ...process.env, TROPHYGURU_CONSENT_URL: consent.toString() }, windowsHide: true, timeout: 15000, stdio: 'ignore'
    });
    const code = await listener.code;
    const tokens = await tokenRequest({ ...client, code, code_verifier: verifier, redirect_uri: redirect, grant_type: 'authorization_code' });
    assert(tokens.refresh_token, 'Google did not issue offline access. Existing saved access was not replaced.');
    saveCredentials({ ...client, refresh_token: tokens.refresh_token, scope: SCOPE });
    console.log('Read-only authorisation saved using Windows user encryption. Run status to check access to trophy.guru.');
  } finally { listener.close(); }
}
async function getAccessToken() {
  const saved = loadCredentials();
  validateScope(saved.scope);
  return (await tokenRequest({ client_id: saved.client_id, client_secret: saved.client_secret,
    refresh_token: saved.refresh_token, grant_type: 'refresh_token' })).access_token;
}
function validateProperty(property) { assert(PROPERTIES.includes(property), 'This connector is limited to trophy.guru properties.'); return property; }
function validateInspectionUrl(value) {
  const url = new URL(value);
  assert(url.origin === 'https://trophy.guru' && !url.username && !url.password && !url.search && !url.hash, 'Inspect a clean HTTPS trophy.guru URL without query parameters.');
  return url.href;
}
async function apiRequest(token, endpoint, body, fetcher = fetch) {
  assert(endpoint.startsWith(API + '/sites') || endpoint === 'https://searchconsole.googleapis.com/v1/urlInspection/index:inspect', 'Unsupported API endpoint.');
  const response = await fetcher(endpoint, { method: body ? 'POST' : 'GET',
    headers: { Authorization: `Bearer ${token}`, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}), redirect: 'error', signal: AbortSignal.timeout(30000) });
  if (!response.ok) {
    const advice = response.status === 403 ? 'Enable the Search Console API in the OAuth project and check this Google account can read the property.'
      : response.status === 429 ? 'Google rate limit reached; retry later.' : 'Check Google access and retry.';
    throw new Error(`Search Console request failed (HTTP ${response.status}). ${advice}`);
  }
  return response.json();
}
async function selectProperty(token, explicit, request = apiRequest) {
  const sites = await request(token, API + '/sites');
  const permitted = (sites.siteEntry || []).filter(site => PROPERTIES.includes(site.siteUrl) && site.permissionLevel !== 'siteUnverifiedUser');
  const chosen = explicit ? validateProperty(explicit) : PROPERTIES.find(site => permitted.some(entry => entry.siteUrl === site));
  assert(chosen && permitted.some(entry => entry.siteUrl === chosen), 'The authorised account has no verified readable trophy.guru property. Use the Google account that owns your Search Console property.');
  return chosen;
}
function dateOffset(date, delta) {
  assert(/^\d{4}-\d{2}-\d{2}$/.test(date), 'Use YYYY-MM-DD dates.');
  const parsed = new Date(date + 'T12:00:00Z');
  assert(!isNaN(parsed) && parsed.toISOString().slice(0, 10) === date, 'Invalid calendar date.');
  parsed.setUTCDate(parsed.getUTCDate() + delta);
  return parsed.toISOString().slice(0, 10);
}
function reportingPeriods(days = 28, end) {
  assert(Number.isInteger(days) && days >= 1 && days <= 90, 'Choose 1 to 90 days.');
  if (!end) {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
    const part = type => parts.find(p => p.type === type).value;
    end = dateOffset(`${part('year')}-${part('month')}-${part('day')}`, -3);
  }
  end = dateOffset(end, 0);
  const start = dateOffset(end, -(days - 1));
  return { current: { startDate: start, endDate: end }, previous: { startDate: dateOffset(start, -days), endDate: dateOffset(start, -1) } };
}
async function buildReport(token, property, periods, request = apiRequest) {
  validateProperty(property);
  const endpoint = `${API}/sites/${encodeURIComponent(property)}/searchAnalytics/query`;
  const query = (range, dimensions = []) => request(token, endpoint, { ...range, dimensions, type: 'web', dataState: 'final', rowLimit: 1000 });
  // Totals are queried separately: privacy-filtered query rows cannot be summed to recover totals.
  const results = await Promise.all([
    query(periods.current), query(periods.previous), query(periods.current, ['date']),
    query(periods.current, ['query']), query(periods.current, ['page']), query(periods.current, ['country']),
    query(periods.current, ['device']), query(periods.current, ['query', 'page']),
    request(token, `${API}/sites/${encodeURIComponent(property)}/sitemaps`)
  ]);
  const names = ['currentTotals', 'previousTotals', 'daily', 'queries', 'pages', 'countries', 'devices', 'queryPages', 'sitemaps'];
  return { generatedAt: new Date().toISOString(), property, periods, searchType: 'web', dataState: 'final',
    notes: ['Dates use Search Console Pacific Time. Default end date is three days ago to allow reporting lag.',
      'No rows means no data returned, not proof of no traffic. Query privacy filtering means row sums can differ from totals.',
      'Dimension reports return up to 1,000 rows, not a guaranteed complete export. Average position is an aggregate, not a fixed ranking.',
      'This report measures Google Web Search, not appearances across all AI assistants.'],
    ...Object.fromEntries(names.map((name, index) => [name, results[index]])) };
}
function readOptions(argv) {
  const options = {};
  for (let i = 0; i < argv.length; i++) {
    const key = argv[i];
    assert(['--client', '--property', '--days', '--end', '--url', '--no-open'].includes(key), `Unknown option: ${key}`);
    if (key === '--no-open') options[key] = true;
    else { assert(argv[i + 1] && !argv[i + 1].startsWith('--'), `Missing value for ${key}`); options[key] = argv[++i]; }
  }
  return options;
}
async function main() {
  const [command = 'help', ...args] = process.argv.slice(2);
  const options = readOptions(args);
  if (command === 'help') { console.log('Local read-only Trophy Guru Search Console connector (Node 20+, Windows)\n  auth --client "C:\\path\\desktop-client.json" [--no-open]\n  status\n  report [--days 28] [--end YYYY-MM-DD] [--property sc-domain:trophy.guru]\n  inspect --url https://trophy.guru/demo\nReports and encrypted credentials stay under %LOCALAPPDATA%\\TrophyGuru\\SearchConsole.'); return; }
  assert(['auth', 'status', 'report', 'inspect'].includes(command), 'Unknown command; run help.');
  if (command === 'auth') { assert(options['--client'], 'Provide --client with the downloaded Desktop OAuth client file path.'); await authenticate(options['--client'], !options['--no-open']); return; }
  const token = await getAccessToken();
  const property = await selectProperty(token, options['--property']);
  if (command === 'status') { console.log(JSON.stringify({ connected: true, property, permission: 'read-only', credentials: 'Windows CurrentUser encryption' }, null, 2)); return; }
  if (command === 'inspect') {
    assert(options['--url'], 'Provide --url with the public page to inspect.');
    const result = await apiRequest(token, 'https://searchconsole.googleapis.com/v1/urlInspection/index:inspect', { inspectionUrl: validateInspectionUrl(options['--url']), siteUrl: property, languageCode: 'en-GB' });
    console.log(JSON.stringify(result, null, 2)); return;
  }
  const report = await buildReport(token, property, reportingPeriods(Number(options['--days'] || 28), options['--end']));
  const directory = path.join(STORE, 'reports');
  fs.mkdirSync(directory, { recursive: true });
  const destination = path.join(directory, `performance-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
  fs.writeFileSync(destination, JSON.stringify(report, null, 2) + '\n', { mode: 0o600 });
  console.log(JSON.stringify({ report: destination, property, periods: report.periods, current: report.currentTotals, previous: report.previousTotals }, null, 2));
}
module.exports = { SCOPE, API, protect, validateClient, validateScope, createConsentListener, validateProperty,
  validateInspectionUrl, tokenRequest, apiRequest, selectProperty, dateOffset, reportingPeriods, buildReport, readOptions };
if (require.main === module) main().catch(error => { console.error(error.message); process.exitCode = 1; });
