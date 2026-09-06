(() => {
  'use strict';
  const root = document.querySelector('#publication-panel');
  if (!root) return;
  let data = null;
  let preview = null;
  let selected = new Set();
  let loading = false;
  let previewRevision = 0;
  let previewReady = false;
  root.classList.add('publication-panel');
  root.innerHTML = `
    <div class="publication-heading"><div><p class="publication-eyebrow">Your public honours board</p><h2>Your honours board.</h2>
      <p>Preview your confirmed winners, then publish a link to share.</p></div><span id="publication-state" class="publication-state">Private</span></div>
    <p id="publication-summary">Loading publication settings…</p>
    <p id="publication-feedback" role="status" aria-live="polite"></p>
    <p id="publication-ready-summary"></p>
    <details id="publication-controls"><summary>Board settings</summary>
      <div class="publication-settings">
        <label class="publication-check"><input id="publication-descriptions" type="checkbox"> Include winner descriptions in the public board</label>
        <label class="publication-check"><input id="publication-juniors" type="checkbox"> Include junior trophies</label>
        <p class="publication-help">Junior trophies and winner descriptions are optional.</p>
        <label>Websites allowed to embed this public board<textarea id="publication-origins" rows="2" placeholder="https://www.yourclub.co.uk"></textarea></label>
        <p class="publication-help">One HTTPS website origin per line. Include www only if your website uses it. This controls where the board may be embedded; the published board and its records are still public.</p>
      </div>
    </details>
      <div class="publication-actions"><button id="publication-preview-button" type="button" class="publication-primary" disabled>Preview honours board</button><button id="publication-withdraw" type="button" class="publication-danger" hidden>Withdraw public board</button></div>
      <div id="publication-preview-area" hidden>
        <div class="publication-preview-toolbar"><div><h3>Your honours board</h3><p id="publication-preview-summary"></p></div><button id="publication-publish" type="button" class="publication-primary" disabled>Publish honours board</button></div>
        <p class="publication-approval">Publish to make this board public and get your share link. Only publish records you’re authorised to share for your club.</p>
        <iframe id="publication-preview-frame" title="Honours board preview"></iframe>
      </div>
      <div id="publication-sharing" hidden><h3>Share your published board</h3>
        <label>Public board link<input id="publication-link" readonly></label><a id="publication-open-link" target="_blank" rel="noopener">Open public board ↗</a><button type="button" data-copy="publication-link">Copy link</button>
        <details><summary>Add to your club website</summary>
        <label>Embed without JavaScript<textarea id="publication-iframe-code" readonly rows="3"></textarea></label><button type="button" data-copy="publication-iframe-code">Copy iframe</button>
        <label>Embed with automatic height<textarea id="publication-script-code" readonly rows="2"></textarea></label><button type="button" data-copy="publication-script-code">Copy script</button>
        <p class="publication-help">Add your website origin above and publish a reviewed version before embedding. A protected CMS page does not make this public board members-only. Withdrawing stops future access from the hosted board, API and embeds; copies already taken by visitors cannot be recalled.</p>
        </details>
      </div>`;
  const el = id => root.querySelector(`#${id}`);
  root.insertBefore(el('publication-sharing'), el('publication-controls'));
  root.append(el('publication-controls'));
  const feedback = message => { el('publication-feedback').textContent = message; };
  const request = async (url, body) => {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(body === undefined ? {} : { method: 'POST', body: JSON.stringify(body) }) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.message || (response.status === 401 ? 'Sign in to review publication.' : 'Publication settings could not be loaded.'));
    return result;
  };

  async function refresh(showPreview = false) {
    if (loading) return;
    loading = true;
    try {
      data = await request('/api/publication');
      const options = data.publication.options;
      el('publication-descriptions').checked = options.includeDescriptions;
      el('publication-juniors').checked = options.includeJuniorTrophies;
      el('publication-origins').value = options.allowedEmbedOrigins.join('\n');
      selectConfirmedWinners();
      invalidatePreview();
      renderStatus();
      renderCandidates();
      if (showPreview === true && selected.size > 0) await preparePreview(false);
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
    el('publication-summary').hidden = !publication.isPublic;
    el('publication-withdraw').hidden = !publication.isPublic || !data.canWithdraw;
    el('publication-sharing').hidden = !publication.isPublic;
    const board = new URL(data.publicUrl, location.origin).href;
    const embed = new URL(data.embedUrl, location.origin).href;
    el('publication-link').value = board;
    el('publication-open-link').href = board;
    el('publication-iframe-code').value = `<iframe src="${embed}" title="Club honours board" width="100%" height="900" style="border:0" loading="lazy" referrerpolicy="no-referrer"></iframe>`;
    const club = data.publicUrl.split('/').pop();
    el('publication-script-code').value = `<script src="${location.origin}/embed/v1.js" data-club="${club}" defer><\/script>`;
  }

  function selectConfirmedWinners() {
    selected = new Set(data.candidates.filter(item => el('publication-juniors').checked || item.division !== 'junior').map(item => item.key));
  }
  function renderCandidates() {
    if (!data) return;
    selectConfirmedWinners();
    el('publication-ready-summary').textContent = selected.size
      ? `${selected.size.toLocaleString('en-GB')} confirmed ${selected.size === 1 ? 'winner' : 'winners'} included automatically.`
      : 'No confirmed winners yet. Confirm your trophy records in the archive to create your board.';
    el('publication-preview-button').disabled = selected.size === 0;
  }

  function options() {
    return { namePolicy: data.publication.options.namePolicy || 'inscription', includeDescriptions: el('publication-descriptions').checked,
      includeJuniorTrophies: el('publication-juniors').checked, selectedWinnerKeys: [...selected],
      allowedEmbedOrigins: el('publication-origins').value.split(/[\n,]+/).map(value => value.trim()).filter(Boolean) };
  }

  function invalidatePreview() {
    preview = null;
    previewReady = false;
    previewRevision++;
    el('publication-preview-button').hidden = false;
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

  async function preparePreview(scroll = true) {
    invalidatePreview();
    const revision = previewRevision;
    el('publication-preview-button').disabled = true;
    feedback('Preparing your private preview…');
    try {
      const result = await request('/api/publication/preview', options());
      if (revision !== previewRevision) return;
      preview = result;
      el('publication-preview-summary').textContent = `${preview.snapshot.summary.honours.toLocaleString('en-GB')} ${preview.snapshot.summary.honours === 1 ? 'honour' : 'honours'} across ${preview.snapshot.summary.trophies} ${preview.snapshot.summary.trophies === 1 ? 'trophy' : 'trophies'}. ${data.canPublish ? '' : 'A club owner with a verified email address must publish this board.'}`;
      el('publication-preview-area').hidden = false;
      el('publication-publish').disabled = true;
      el('publication-publish').textContent = data.publication.isPublic ? 'Publish updates' : 'Publish honours board';
      el('publication-preview-button').hidden = true;
      el('publication-preview-frame').src = '/honours-preview';
      feedback('');
      if (scroll) el('publication-preview-area').scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch (error) { if (revision === previewRevision) feedback(error.message); }
    finally { if (revision === previewRevision) el('publication-preview-button').disabled = selected.size === 0; }
  }
  el('publication-preview-button').addEventListener('click', () => preparePreview());
  window.addEventListener('message', event => {
    if (event.origin === location.origin && event.source === el('publication-preview-frame').contentWindow &&
        event.data?.type === 'trophy-archive:publication-preview-ready' && preview) {
      sendPreview();
      previewReady = true;
      el('publication-publish').disabled = !data.canPublish;
    }
  });
  el('publication-publish').addEventListener('click', async () => {
    if (!preview || !previewReady || !data.canPublish) return;
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
    if (['publication-descriptions', 'publication-juniors', 'publication-origins'].includes(event.target.id)) {
      invalidatePreview(); renderCandidates();
    }
  });
  el('publication-origins').addEventListener('input', invalidatePreview);
  root.addEventListener('click', async event => {
    const button = event.target.closest('[data-copy]');
    if (!button) return;
    try { await navigator.clipboard.writeText(el(button.dataset.copy).value); feedback('Copied to your clipboard.'); }
    catch { el(button.dataset.copy).select(); feedback('Select and copy the highlighted text.'); }
  });
  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
  }
  window.trophyPublication = Object.freeze({ refresh, open: () => {
    el('publication-controls').open = false;
    feedback('');
    return refresh(true);
  } });
})();
