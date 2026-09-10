(() => {
'use strict';
const $ = id => document.getElementById(id);
let registrations = [], selection = 0, accountRequest = 0;
const countLabel = (n, label) => `${n} ${label}${n === 1 ? "" : "s"}`;
const date = value => new Date(value).toLocaleString('en-GB');
function showLogin() {
  selection++; accountRequest++;
  registrations = [];
  $('registrations').replaceChildren(); $('trophies').replaceChildren();
  $('dashboard').hidden = true; $('gallery').hidden = true;
  $('login-panel').hidden = false; $('logout').hidden = true;
}
async function request(url, options = {}) {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options });
  if (response.status === 401) { showLogin(); throw new Error('Please sign in with your verified site-owner account.'); }
  if (!response.ok) throw new Error(response.status === 429 ? 'Too many attempts. Wait a minute and try again.' : 'Unable to load this information. Please try again.');
  return response;
}
function node(tag, text, className) {
  const el = document.createElement(tag);
  if (text !== undefined) el.textContent = text;
  if (className) el.className = className;
  return el;
}
function renderAccounts() {
  const q = $('search').value.trim().toLowerCase();
  const matches = registrations.filter(a => [a.name, a.email, a.clubName].some(v => (v || '').toLowerCase().includes(q)));
  $('registrations').replaceChildren();
  for (const a of matches) {
    const row = node('tr'), person = node('td');
    person.append(node('strong', a.name), node('span', a.email, 'email'));
    row.append(person, node('td', date(a.createdAt)), node('td', a.emailVerified ? 'Verified' : 'Not yet verified'), node('td', a.clubName || 'Club not set up'));
    const action = node('td');
    if (a.clubId) {
      const button = node('button', 'View uploads');
      button.addEventListener('click', () => loadClub(a.clubId).catch(e => $('message').textContent = e.message));
      action.append(button);
    } else action.textContent = 'No club yet';
    row.append(action);
    ['Name and email', 'Registered', 'Email verified', 'Club', 'Uploads'].forEach((label, i) => row.children[i].dataset.label = label);
    $('registrations').append(row);
  }
  if (!matches.length) { const row = node('tr'), cell = node('td', 'No registrations found.'); cell.colSpan = 5; row.append(cell); $('registrations').append(row); }
}
async function loadAccounts() {
  const version = ++accountRequest;
  const result = await (await request('/admin/data/registrations')).json();
  if (version !== accountRequest) return;
  registrations = result;
  $('login-panel').hidden = true; $('dashboard').hidden = false; $('logout').hidden = false;
  $('message').textContent = '';
  $('totals').textContent = `${countLabel(registrations.length, "registration")} · ${countLabel(new Set(registrations.map(a => a.clubId).filter(Boolean)).size, "club")}`;
  renderAccounts();
}
async function loadClub(id) {
  const version = ++selection;
  $('message').textContent = 'Loading uploads…';
  $('gallery').hidden = true; $('trophies').replaceChildren();
  const club = await (await request('/admin/data/clubs/' + encodeURIComponent(id))).json();
  if (selection !== version) return;
  $('message').textContent = '';
  $('gallery-title').textContent = club.name;
  const count = club.trophies.reduce((n, t) => n + t.images.length, 0);
  $('gallery-summary').textContent = `${club.trophies.length} ${club.trophies.length === 1 ? "trophy" : "trophies"} · ${countLabel(count, "uploaded image")}. Select an image to open the original.`;
  for (const trophy of club.trophies) {
    const section = node('article'); section.append(node('h3', trophy.name + (trophy.archived ? ' (archived)' : '')));
    const grid = node('div', undefined, 'photos');
    for (const image of trophy.images) {
      const figure = node('figure'), link = node('a'), img = node('img');
      link.href = image.url; link.target = '_blank'; link.rel = 'noopener';
      img.src = image.url; img.alt = image.name || 'Uploaded trophy photograph'; img.loading = 'lazy';
      img.addEventListener('error', () => { img.replaceWith(node('span', 'Image unavailable. Refresh or sign in again.')); });
      link.append(img); figure.append(link, node('figcaption', `${image.name} · ${image.kind} · ${date(image.uploadedAt)}`)); grid.append(figure);
    }
    section.append(trophy.images.length ? grid : node('p', 'No photos uploaded for this trophy.'));
    $('trophies').append(section);
  }
  if (!club.trophies.length) $('trophies').append(node('p', 'This club has not added any trophies yet.'));
  $('gallery').hidden = false; $('gallery-title').focus(); $('gallery').scrollIntoView({behavior:'smooth'});
}
$('login').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget, button = form.querySelector('button'); button.disabled = true;
  try {
    const values = new FormData(form);
    await request('/admin/login', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({email:values.get('email'),password:values.get('password')})});
    form.reset(); await loadAccounts();
  } catch (e) { $('message').textContent = e.message; }
  finally { button.disabled = false; }
});
$('logout').addEventListener('click', async () => {
  try { await request('/admin/logout', {method:'POST'}); showLogin(); $('message').textContent = 'Signed out.'; }
  catch (e) { $('message').textContent = e.message; }
});
$('search').addEventListener('input', renderAccounts);
$('refresh').addEventListener('click', () => loadAccounts().catch(e => $('message').textContent = e.message));
loadAccounts().catch(e => { $('message').textContent = e.message; });
})();
