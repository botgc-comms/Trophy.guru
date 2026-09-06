(() => {
  'use strict';
  const root = document.querySelector('#publication-panel');
  if (!root) return;
  let data = null;
  let preview = null;
  let selected = new Set();
  let page = 0;
  let loading = false;
  const pageSize = 80;
  root.classList.add('publication-panel');
  root.innerHTML = `
    <div class="publication-heading"><div><p class="publication-eyebrow">Your public honours board</p><h2>Share your club’s honours board.</h2>
      <p>Use the winners you’ve already confirmed. Preview the board, then publish it for anyone to view.</p></div><span id="publication-state" class="publication-state">Private</span></div>
    <p id="publication-summary">Loading publication settings…</p>
    <p id="publication-feedback" role="status" aria-live="polite"></p>
    <p id="publication-ready-summary"></p>
    <details id="publication-controls"><summary>Board settings &amp; exclusions</summary>
      <p>These names are already confirmed. Only change the selection if you want to leave records off the public board.</p>
      <div class="publication-settings">
        <label>Names on the public board<select id="publication-name-policy"><option value="inscription">Names as inscribed</option><option value="approved-identities">Use manually approved identities where available</option></select></label>
        <label class="publication-check"><input id="publication-descriptions" type="checkbox"> Include winner descriptions in the public board</label>
        <label class="publication-check"><input id="publication-juniors" type="checkbox"> Allow junior trophies in the selection</label>
        <p class="publication-help">AI member suggestions are never published as identities. Descriptions and junior trophies start excluded; review any personal information before including them.</p>
        <label>Websites allowed to embed this public board<textarea id="publication-origins" rows="2" placeholder="https://www.yourclub.co.uk"></textarea></label>
        <p class="publication-help">One HTTPS website origin per line. Include www only if your website uses it. This controls where the board may be embedded; the published board and its records are still public.</p>
      </div>
      <div class="publication-record-tools"><label>Find a confirmed record<input id="publication-search" type="search" placeholder="Trophy, year or name"></label>
        <div><button id="publication-select-visible" type="button">Select these results</button><button id="publication-clear-visible" type="button">Clear these results</button><button id="publication-refresh" type="button">Refresh records</button></div></div>
      <p id="publication-selection-count"></p>
      <div class="publication-table-wrap"><table><thead><tr><th scope="col">Include</th><th scope="col">Trophy / year</th><th scope="col">Public name</th></tr></thead><tbody id="publication-candidates"></tbody></table></div>
      <div class="publication-pagination"><button id="publication-previous" type="button">Previous</button><span id="publication-page"></span><button id="publication-next" type="button">Next</button></div>
    </details>
      <div class="publication-actions"><button id="publication-preview-button" type="button" class="publication-primary" disabled>Preview honours board</button><button id="publication-withdraw" type="button" class="publication-danger" hidden>Withdraw public board</button></div>
      <div id="publication-preview-area" hidden>
        <h3>Your honours board preview</h3><p id="publication-preview-summary"></p>
        <iframe id="publication-preview-frame" title="Private preview of selected honours board records" loading="lazy"></iframe>
        <p class="publication-approval">Publishing makes this board public. By publishing, you confirm you’re authorised to share the included records on behalf of your club.</p>
        <button id="publication-publish" type="button" class="publication-primary" disabled>Publish honours board</button>
      </div>
      <div id="publication-sharing" hidden><h3>Share your published board</h3>
        <label>Public board link<input id="publication-link" readonly></label><button type="button" data-copy="publication-link">Copy link</button>
        <details><summary>Add to your club website</summary>
        <label>Embed without JavaScript<textarea id="publication-iframe-code" readonly rows="3"></textarea></label><button type="button" data-copy="publication-iframe-code">Copy iframe</button>
        <label>Embed with automatic height<textarea id="publication-script-code" readonly rows="2"></textarea></label><button type="button" data-copy="publication-script-code">Copy script</button>
        <p class="publication-help">Add your website origin above and publish a reviewed version before embedding. A protected CMS page does not make this public board members-only. Withdrawing stops future access from the hosted board, API and embeds; copies already taken by visitors cannot be recalled.</p>
        </details>
      </div>`;
  const el = id => root.querySelector(`#${id}`);
  const feedback = message => { el('publication-feedback').textContent = message; };
  const request = async (url, body) => {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(body === undefined ? {} : { method: 'POST', body: JSON.stringify(body) }) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.message || (response.status === 401 ? 'Sign in to review publication.' : 'Publication settings could not be loaded.'));
    return result;
  };

  async function refresh() {
    if (loading) return;
    loading = true;
    try {
      data = await request('/api/publication');
      const options = data.publication.options;
      el('publication-name-policy').value = options.namePolicy;
      el('publication-descriptions').checked = options.includeDescriptions;
      el('publication-juniors').checked = options.includeJuniorTrophies;
      el('publication-origins').value = options.allowedEmbedOrigins.join('\n');
      selected = new Set(options.selectedWinnerKeys.length ? options.selectedWinnerKeys :
        data.candidates.filter(item => item.division !== 'junior').map(item => item.key));
      const validKeys = new Set(data.candidates.map(item => item.key));
      selected = new Set([...selected].filter(key => validKeys.has(key)));
      page = 0;
      invalidatePreview();
      renderStatus();
      renderCandidates();
    } catch (error) { el('publication-summary').textContent = error.message; }
    finally { loading = false; }
  }

  function renderStatus() {
    const publication = data.publication;
    el('publication-state').textContent = publication.isPublic ? 'Public' : 'Private';
    el('publication-state').classList.toggle('is-public', publication.isPublic);
    el('publication-summary').textContent = publication.isPublic
      ? `${publication.summary.honours.toLocaleString('en-GB')} ${publication.summary.honours === 1 ? 'honour' : 'honours'} published on ${new Date(publication.publishedAt).toLocaleDateString('en-GB')}. Use Preview honours board to publish an updated version.`
      : 'Your board is private until you publish it.';
    el('publication-withdraw').hidden = !publication.isPublic || !data.canWithdraw;
    el('publication-sharing').hidden = !publication.isPublic;
    const board = new URL(data.publicUrl, location.origin).href;
    const embed = new URL(data.embedUrl, location.origin).href;
    el('publication-link').value = board;
    el('publication-iframe-code').value = `<iframe src="${embed}" title="Club honours board" width="100%" height="900" style="border:0" loading="lazy" referrerpolicy="no-referrer"></iframe>`;
    const club = data.publicUrl.split('/').pop();
    el('publication-script-code').value = `<script src="${location.origin}/embed/v1.js" data-club="${club}" defer><\/script>`;
  }

  function filteredCandidates() {
    const search = el('publication-search').value.trim().toLocaleLowerCase('en-GB');
    return data.candidates.filter(item => (el('publication-juniors').checked || item.division !== 'junior') &&
      (!search || `${item.trophyName} ${item.year} ${item.inscriptionName} ${item.approvedIdentityName || ''}`.toLocaleLowerCase('en-GB').includes(search)));
  }

  function renderCandidates() {
    if (!data) return;
    const filtered = filteredCandidates();
    const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
    page = Math.max(0, Math.min(page, pages - 1));
    const shown = filtered.slice(page * pageSize, (page + 1) * pageSize);
    const approvedNames = el('publication-name-policy').value === 'approved-identities';
    el('publication-candidates').innerHTML = shown.map(item => {
      const name = approvedNames && item.approvedIdentityName ? item.approvedIdentityName : item.inscriptionName;
      return `<tr><td><input type="checkbox" data-key="${escapeHtml(item.key)}" aria-label="Publish ${escapeHtml(item.trophyName)} ${item.year}: ${escapeHtml(name)}" ${selected.has(item.key) ? 'checked' : ''}></td><td><strong>${escapeHtml(item.trophyName)}</strong><small>${item.year}${item.division === 'junior' ? ' · Junior' : ''}</small></td><td>${escapeHtml(name)}${approvedNames && item.approvedIdentityName ? '<small>Manually approved identity</small>' : ''}${el('publication-descriptions').checked && item.description ? `<small>${escapeHtml(item.description)}</small>` : ''}</td></tr>`;
    }).join('') || '<tr><td colspan="3">No confirmed winners match this selection.</td></tr>';
    el('publication-ready-summary').textContent = `${selected.size.toLocaleString('en-GB')} confirmed records included. You don’t need to confirm the names again. Junior trophies and descriptions are optional in board settings.`;
    el('publication-selection-count').textContent = `${selected.size.toLocaleString('en-GB')} confirmed ${selected.size === 1 ? 'record' : 'records'} selected · ${filtered.length.toLocaleString('en-GB')} matching results`;
    el('publication-page').textContent = `Page ${page + 1} of ${pages}`;
    el('publication-previous').disabled = page === 0;
    el('publication-next').disabled = page + 1 >= pages;
    el('publication-preview-button').disabled = selected.size === 0;
  }

  function options() {
    return { namePolicy: el('publication-name-policy').value, includeDescriptions: el('publication-descriptions').checked,
      includeJuniorTrophies: el('publication-juniors').checked, selectedWinnerKeys: [...selected],
      allowedEmbedOrigins: el('publication-origins').value.split(/[\n,]+/).map(value => value.trim()).filter(Boolean) };
  }

  function invalidatePreview() {
    preview = null;
    el('publication-preview-area').hidden = true;
    el('publication-publish').disabled = true;
    el('publication-preview-frame').removeAttribute('src');
  }

  function sendPreview() {
    if (!preview) return;
    const snapshot = structuredClone(preview.snapshot);
    if (snapshot.club.logoUrl) snapshot.club.logoUrl = '/api/publication/preview-assets/logo';
    snapshot.trophies.forEach(trophy => { if (trophy.imageUrl) trophy.imageUrl = `/api/publication/preview-assets/trophies/${encodeURIComponent(trophy.id)}`; });
    el('publication-preview-frame').contentWindow?.postMessage({ type: 'trophy-archive:publication-preview', snapshot }, location.origin);
  }

  el('publication-preview-button').addEventListener('click', async () => {
    el('publication-preview-button').disabled = true;
    feedback('Preparing your private preview…');
    try {
      preview = await request('/api/publication/preview', options());
      el('publication-preview-summary').textContent = `${preview.snapshot.summary.honours.toLocaleString('en-GB')} honours across ${preview.snapshot.summary.trophies} trophies. This preview is private. ${data.canPublish ? 'Publish below to get your public link.' : 'A club owner with a verified email address must approve publication.'}`;
      el('publication-preview-area').hidden = false;
      el('publication-publish').disabled = !data.canPublish;
      el('publication-preview-frame').src = '/honours-preview';
      feedback('Preview ready below. Publish the board to get a public link you can share.');
      el('publication-preview-area').scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch (error) { feedback(error.message); }
    finally { el('publication-preview-button').disabled = selected.size === 0; }
  });
  window.addEventListener('message', event => {
    if (event.origin === location.origin && event.source === el('publication-preview-frame').contentWindow &&
        event.data?.type === 'trophy-archive:publication-preview-ready') sendPreview();
  });
  el('publication-publish').addEventListener('click', async () => {
    if (!preview || !data.canPublish) return;
    el('publication-publish').disabled = true;
    try {
      await request('/api/publication/publish', { options: preview.options, previewFingerprint: preview.fingerprint, publicationApproved: true });
      await refresh();
      feedback('Your honours board is public. Copy your link below to share it.');
      el('publication-sharing').scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch (error) { invalidatePreview(); feedback(error.message); }
  });
  el('publication-withdraw').addEventListener('click', async () => {
    el('publication-withdraw').disabled = true;
    try {
      await request('/api/publication/withdraw', {});
      await refresh();
      feedback('Public access withdrawn. Your archive and its source records remain available to your club.');
    } catch (error) { feedback(error.message); }
    finally { el('publication-withdraw').disabled = false; }
  });
  root.addEventListener('change', event => {
    if (event.target.matches('[data-key]')) {
      event.target.checked ? selected.add(event.target.dataset.key) : selected.delete(event.target.dataset.key);
      invalidatePreview(); renderCandidates();
    } else if (['publication-name-policy', 'publication-descriptions', 'publication-juniors', 'publication-origins'].includes(event.target.id)) {
      if (!el('publication-juniors').checked) data?.candidates.filter(item => item.division === 'junior').forEach(item => selected.delete(item.key));
      invalidatePreview(); renderCandidates();
    }
  });
  el('publication-origins').addEventListener('input', invalidatePreview);
  el('publication-search').addEventListener('input', () => { page = 0; renderCandidates(); });
  el('publication-select-visible').addEventListener('click', () => { filteredCandidates().forEach(item => selected.add(item.key)); invalidatePreview(); renderCandidates(); });
  el('publication-clear-visible').addEventListener('click', () => { filteredCandidates().forEach(item => selected.delete(item.key)); invalidatePreview(); renderCandidates(); });
  el('publication-refresh').addEventListener('click', refresh);
  el('publication-previous').addEventListener('click', () => { page--; renderCandidates(); });
  el('publication-next').addEventListener('click', () => { page++; renderCandidates(); });
  root.addEventListener('click', async event => {
    const button = event.target.closest('[data-copy]');
    if (!button) return;
    try { await navigator.clipboard.writeText(el(button.dataset.copy).value); feedback('Copied to your clipboard.'); }
    catch { el(button.dataset.copy).select(); feedback('Select and copy the highlighted text.'); }
  });
  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
  }
  window.trophyPublication = Object.freeze({ refresh });
  window.addEventListener('trophy-app-ready', refresh);
  refresh();
})();
