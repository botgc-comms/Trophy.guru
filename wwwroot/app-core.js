const state = {
  trophies: [],
  totals: {},
  current: null,
  missingYears: [],
  filter: 'all',
  aiConfigured: false,
  activeEvidenceId: null,
  analysisStatus: null,
  analysisPollTimer: null,
  analysisNoticeShownFor: null,
};

const elements = {
  catalogueView: document.querySelector('#catalogue-view'),
  detailView: document.querySelector('#detail-view'),
  grid: document.querySelector('#trophy-grid'),
  search: document.querySelector('#search-input'),
  winnerList: document.querySelector('#winner-list'),
  photoStrip: document.querySelector('#photo-strip'),
  toast: document.querySelector('#toast'),
  busy: document.querySelector('#busy-overlay'),
  login: document.querySelector('#login-screen'),
};

async function api(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'same-origin',
    headers: options.body instanceof FormData ? options.headers : { 'Content-Type': 'application/json', ...options.headers },
    ...options,
  });
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (response.status === 401) {
    if (!url.startsWith('/api/auth/')) showLogin();
    throw new Error(data?.message || 'Please sign in again.');
  }
  if (!response.ok) throw new Error(data?.message || data?.error || 'Something went wrong.');
  return data;
}

function installBatchUploadControl() {
  if (!document.querySelector('link[href="/async.css"]')) {
    const stylesheet = document.createElement('link');
    stylesheet.rel = 'stylesheet';
    stylesheet.href = '/async.css';
    document.head.append(stylesheet);
  }

  const copy = document.querySelector('#detail-view .section-copy');
  if (copy) {
    copy.textContent = 'Take several overlapping photos or choose a batch from your phone. They are saved immediately, then read together in the background after you pause.';
  }

  const captureActions = document.querySelector('.capture-actions');
  if (!captureActions || document.querySelector('#photo-library-input')) return;
  captureActions.classList.add('has-batch-upload');
  const batchControl = document.createElement('label');
  batchControl.className = 'capture-button secondary-action batch-photo-action';
  batchControl.innerHTML = `
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16v14H4zM7 15l3-3 2 2 2.5-3 3.5 4M8.5 9.5h.01"/></svg>
    Choose photos
    <input id="photo-library-input" type="file" accept="image/jpeg,image/png,image/webp" multiple hidden>`;
  captureActions.children[0]?.after(batchControl);
  batchControl.querySelector('input').addEventListener('change', event => uploadFiles([...event.target.files], 'photo'));
}

async function loadCatalogue() {
  const data = await api('/api/trophies');
  state.trophies = data.items;
  state.totals = data.totals;
  state.aiConfigured = data.aiConfigured;
  updateCounts();
  renderTrophies();
}

function updateCounts() {
  const totals = state.totals;
  setText('#complete-count', totals.complete ?? 0);
  setText('#total-count', totals.all ?? 0);
  setText('#filter-all-count', totals.all ?? 0);
  setText('#filter-todo-count', totals.notStarted ?? 0);
  setText('#filter-review-count', totals.needsReview ?? 0);
  setText('#filter-progress-count', totals.inProgress ?? 0);
  setText('#filter-complete-count', totals.complete ?? 0);
}

function renderTrophies() {
  const query = elements.search.value.trim().toLowerCase();
  const visible = state.trophies.filter(trophy => {
    const matchesFilter = state.filter === 'all' ||
      (state.filter === 'review' ? trophy.needsReviewCount > 0 : trophy.status === state.filter);
    const searchText = `${trophy.id} ${trophy.name} ${trophy.secondaryName || ''} ${trophy.category}`.toLowerCase();
    return matchesFilter && searchText.includes(query);
  });

  elements.grid.innerHTML = visible.map(trophy => {
    const status = displayStatus(trophy);
    const activity = trophy.evidenceCount
      ? `${plural(trophy.winnerCount, 'winner')} · ${plural(trophy.evidenceCount, 'image')}`
      : 'Ready to start';
    return `
      <button class="trophy-card" data-id="${escapeHtml(trophy.id)}">
        <span class="trophy-image-wrap">
          <img src="${escapeHtml(trophy.referenceImage || '/catalogue/fallback.svg')}" alt="${escapeHtml(trophy.name)}" loading="lazy">
          ${trophy.illustrationState === 'processing' ? '<span class="illustration-holding">✦ Generating illustration</span>' : ''}
          <span class="card-status status-${status.key}">${status.label}</span>
        </span>
        <span class="trophy-card-body">
          <span class="card-kicker">${escapeHtml(trophy.id)} · ${escapeHtml(trophy.category)}</span>
          <strong>${escapeHtml(trophy.name)}</strong>
          ${trophy.secondaryName ? `<em>${escapeHtml(trophy.secondaryName)}</em>` : ''}
          <small>${activity}</small>
        </span>
        <svg class="card-arrow" viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>
      </button>`;
  }).join('') || '<p class="empty-state">No trophies match that search.</p>';

  elements.grid.querySelectorAll('img').forEach(addImageFallback);
}

async function openTrophy(id, pushHistory = true) {
  stopAnalysisPolling();
  state.analysisStatus = null;
  state.analysisNoticeShownFor = null;
  setBusy(true, 'Opening the trophy…', 'Loading its images and working winners list.');
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(id)}`);
    state.current = data.trophy;
    state.missingYears = data.missingYears || [];
    renderDetail();
    elements.catalogueView.hidden = true;
    elements.detailView.hidden = false;
    elements.login.hidden = true;
    window.scrollTo({ top: 0, behavior: 'instant' });
    if (pushHistory) history.pushState({ trophy: id }, '', `#trophy/${id}`);
    startAnalysisPolling();
  } catch (error) {
    showToast(error.message, true);
  } finally {
    setBusy(false);
  }
}

function closeTrophy(pushHistory = true) {
  stopAnalysisPolling();
  state.current = null;
  state.analysisStatus = null;
  elements.detailView.hidden = true;
  elements.catalogueView.hidden = false;
  if (pushHistory) history.pushState({}, '', '#catalogue');
  window.scrollTo({ top: 0, behavior: 'instant' });
}

function renderDetail() {
  const trophy = state.current;
  if (!trophy) return;
  const status = displayStatus({ ...trophy, needsReviewCount: trophy.winners.filter(winner => winner.reviewState !== 'confirmed').length });
  setText('#detail-code', `${trophy.id} · ${trophy.category}`);
  setText('#detail-title', trophy.name);
  const secondary = document.querySelector('#detail-secondary');
  secondary.textContent = trophy.secondaryName || '';
  secondary.hidden = !trophy.secondaryName;
  setText('#detail-summary', `${plural(trophy.evidence.length, 'image')} · ${plural(trophy.winners.length, 'winner')}`);
  const detailPhoto = document.querySelector('#detail-photo');
  detailPhoto.src = trophy.referenceImage || '/catalogue/fallback.svg';
  detailPhoto.alt = trophy.illustrationState === 'processing' ? `${trophy.name} illustration is being generated` : trophy.name;
  detailPhoto.closest('.detail-photo-wrap')?.classList.toggle('is-generating', trophy.illustrationState === 'processing');
  addImageFallback(detailPhoto);
  const statusElement = document.querySelector('#detail-status');
  statusElement.textContent = status.label;
  statusElement.className = `status-pill status-${status.key}`;
  setText('#evidence-count', plural(trophy.evidence.length, 'image'));
  document.querySelector('#timeline-start').value = trophy.timelineStartYear ?? '';
  document.querySelector('#timeline-end').value = trophy.timelineEndYear ?? '';
  document.querySelector('#analyse-button').disabled = trophy.evidence.length === 0 || !state.aiConfigured;
  document.querySelector('#ai-setup-note').hidden = state.aiConfigured;
  renderEvidence();
  renderReaderNote();
  renderMissingYears();
  renderWinners();
}

function renderEvidence() {
  const evidence = state.current?.evidence || [];
  elements.photoStrip.innerHTML = evidence.map(item => `
    <button class="evidence-thumb ${item.kind === 'rubbing' ? 'rubbing' : ''}" data-evidence-id="${item.id}" aria-label="Open ${escapeHtml(item.kind)} uploaded ${formatDate(item.uploadedAt)}">
      <span>${item.kind}</span>
      <img src="${escapeHtml(item.url)}" alt="${escapeHtml(item.originalName)}">
      ${item.processingState === 'failed' ? '<b class="evidence-alert" title="Reading failed">!</b>' : ''}
    </button>`).join('') + `
    <label class="add-more" aria-label="Add more photographs">+
      <input type="file" accept="image/jpeg,image/png,image/webp" capture="environment" multiple hidden>
    </label>`;
  elements.photoStrip.querySelectorAll('img').forEach(addImageFallback);
  const extraInput = elements.photoStrip.querySelector('.add-more input');
  extraInput?.addEventListener('change', event => uploadFiles([...event.target.files], 'photo'));
}

function renderReaderNote() {
  const note = document.querySelector('#reader-note');
  const evidence = state.current?.evidence || [];
  const analysis = state.analysisStatus;

  if (!evidence.length) {
    note.className = 'reader-note is-neutral';
    note.innerHTML = '<span class="reader-spark" aria-hidden="true">✦</span><span><strong>Add the first engraving photo</strong><small>Take several angles if needed; they will be read together after you pause.</small></span>';
    return;
  }
  if (analysis?.status === 'queued') {
    note.className = 'reader-note is-neutral is-queued';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">✦</span><span><strong>Photos saved — gathering the set</strong><small>${escapeHtml(analysis.message)}</small></span>`;
    return;
  }
  if (analysis?.status === 'processing') {
    note.className = 'reader-note is-processing';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">✦</span><span><strong>Reading all ${analysis.evidenceCount || evidence.length} images in the background</strong><small>${escapeHtml(analysis.message)}</small></span>`;
    return;
  }
  if (analysis?.status === 'failed') {
    note.className = 'reader-note is-warning';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">!</span><span><strong>Images saved; reading needs another try</strong><small>${escapeHtml(analysis.message || 'Use “Read all images again” when ready.')}</small></span>`;
    return;
  }
  if (analysis?.status === 'complete') {
    note.className = 'reader-note';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">✓</span><span><strong>Background reading complete</strong><small>${escapeHtml(analysis.message)}</small></span>`;
    return;
  }

  const pendingCount = evidence.filter(item => ['pending', 'queued', 'processing'].includes(item.processingState)).length;
  if (pendingCount) {
    note.className = 'reader-note is-neutral is-queued';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">✦</span><span><strong>${plural(pendingCount, 'image')} waiting for the background reader</strong><small>You can add more photos while they wait.</small></span>`;
    return;
  }
  const latest = evidence[evidence.length - 1];
  if (latest.processingState === 'failed') {
    note.className = 'reader-note is-warning';
    note.innerHTML = `<span class="reader-spark" aria-hidden="true">!</span><span><strong>Images saved; reading needs another try</strong><small>${escapeHtml(latest.processingMessage || 'Use “Read all images again” when ready.')}</small></span>`;
    return;
  }
  const reviewCount = state.current.winners.filter(winner => winner.reviewState !== 'confirmed').length;
  const observation = latest.processingMessage || `${plural(reviewCount, 'reading')} waiting for your check.`;
  note.className = 'reader-note';
  note.innerHTML = `<span class="reader-spark" aria-hidden="true">✦</span><span><strong>The reader has compared ${plural(evidence.length, 'image')}</strong><small>${escapeHtml(observation)}</small></span>`;
}

function renderMissingYears() {
  const box = document.querySelector('#missing-years');
  const years = state.missingYears || [];
  if (!years.length) {
    box.hidden = true;
    return;
  }
  const visibleYears = years.slice(0, 12).join(', ');
  const remainder = years.length > 12 ? ` and ${years.length - 12} more` : '';
  box.innerHTML = `<div><span aria-hidden="true">!</span><strong>${plural(years.length, 'year')} may be missing</strong></div><p>${visibleYears}${remainder} ${years.length === 1 ? 'has' : 'have'} no winner yet.</p>`;
  box.hidden = false;
}

function renderWinners(addBlank = false) {
  const winners = [...(state.current?.winners || [])].sort((a, b) => a.year - b.year || a.name.localeCompare(b.name));
  const empty = document.querySelector('#empty-winners');
  empty.hidden = winners.length > 0 || addBlank;
  const rows = addBlank ? [null, ...winners] : winners;
  elements.winnerList.innerHTML = rows.map(winnerRow).join('');
  const reviewCount = winners.filter(winner => winner.reviewState !== 'confirmed').length;
  const gapCount = state.missingYears.length;
  setText('#save-summary', `${plural(winners.length, 'winner')}${reviewCount ? ` · ${reviewCount} to check` : ''}${gapCount ? ` · ${plural(gapCount, 'gap')}` : ''}`);
  if (addBlank) elements.winnerList.querySelector('[data-winner-id="new"] input[name="year"]')?.focus();
}

function winnerRow(winner) {
  const isNew = !winner;
  const reviewState = winner?.reviewState || 'needs-review';
  const confidence = winner?.confidence ?? 1;
  const uncertain = !isNew && confidence < 0.75;
  return `
    <article class="winner-row ${uncertain ? 'is-uncertain' : ''} ${isNew ? 'is-new' : ''}" data-winner-id="${winner?.id || 'new'}">
      <label><span>Year</span><input name="year" type="number" min="1800" max="2200" inputmode="numeric" value="${winner?.year || ''}" aria-label="Winning year"></label>
      <label class="winner-name"><span>Winner${winner?.notes ? ` · ${escapeHtml(winner.notes)}` : ''}</span><input name="name" maxlength="200" value="${escapeHtml(winner?.name || '')}" aria-label="Winner name" placeholder="Name on trophy"></label>
      <div class="confidence ${reviewState} ${uncertain ? 'uncertain' : ''}">
        ${isNew ? '<span>Manual</span><small>New</small>' : `<span>${Math.round(confidence * 100)}%</span><small>${reviewState === 'confirmed' ? 'Confirmed' : uncertain ? 'Uncertain' : 'Check'}</small>`}
      </div>
      <div class="winner-actions">
        <button class="confirm-winner" data-action="confirm" type="button" title="Save and confirm">${isNew ? 'Add' : reviewState === 'confirmed' ? 'Save' : 'Confirm'}</button>
        ${isNew ? '<button class="cancel-winner" data-action="cancel" type="button" aria-label="Cancel new winner">×</button>' : '<button class="delete-winner" data-action="delete" type="button" aria-label="Delete winner">×</button>'}
      </div>
    </article>`;
}

async function saveWinner(row) {
  const id = row.dataset.winnerId;
  const year = Number(row.querySelector('[name="year"]').value);
  const name = row.querySelector('[name="name"]').value.trim();
  if (!year || !name) {
    showToast('Enter both the year and winner’s name.', true);
    return;
  }
  const payload = JSON.stringify({ year, name, reviewState: 'confirmed', notes: null });
  try {
    if (id === 'new') {
      await api(`/api/trophies/${encodeURIComponent(state.current.id)}/winners`, { method: 'POST', body: payload });
    } else {
      await api(`/api/trophies/${encodeURIComponent(state.current.id)}/winners/${encodeURIComponent(id)}`, { method: 'PUT', body: payload });
    }
    await refreshCurrent();
    await loadCatalogue();
    showToast(id === 'new' ? 'Winner added.' : 'Winner confirmed.');
  } catch (error) {
    showToast(error.message, true);
  }
}

async function deleteWinner(id) {
  if (!confirm('Delete this winner from the working list?')) return;
  try {
    await api(`/api/trophies/${encodeURIComponent(state.current.id)}/winners/${encodeURIComponent(id)}`, { method: 'DELETE' });
    await refreshCurrent();
    await loadCatalogue();
    showToast('Winner removed.');
  } catch (error) {
    showToast(error.message, true);
  }
}

async function saveTimeline() {
  const startValue = document.querySelector('#timeline-start').value;
  const endValue = document.querySelector('#timeline-end').value;
  const body = JSON.stringify({ startYear: startValue ? Number(startValue) : null, endYear: endValue ? Number(endValue) : null });
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}/timeline`, { method: 'PUT', body });
    state.current = data.trophy;
    state.missingYears = data.missingYears;
    renderDetail();
    showToast('Expected year range saved.');
  } catch (error) {
    showToast(error.message, true);
  }
}

async function uploadFiles(files, kind) {
  if (!files.length || !state.current) return;
  const trophyId = state.current.id;
  setBusy(true, `Preparing ${plural(files.length, 'image')}…`, 'Optimising the batch for a clear, quick mobile upload.');
  try {
    const preparedFiles = [];
    for (let index = 0; index < files.length; index += 1) {
      setBusy(true, `Preparing image ${index + 1} of ${files.length}…`, 'Optimising the batch for a clear, quick mobile upload.');
      preparedFiles.push(await optimiseImage(files[index]));
    }

    const form = new FormData();
    preparedFiles.forEach(file => form.append('files', file, file.name));
    form.append('kind', kind);
    setBusy(true, `Saving ${plural(preparedFiles.length, 'image')}…`, 'You can keep working as soon as the upload finishes.');
    const data = await api(`/api/trophies/${encodeURIComponent(trophyId)}/images`, { method: 'POST', body: form });
    if (state.current?.id !== trophyId) return;
    state.current = data.trophy;
    state.missingYears = data.missingYears || [];
    state.analysisStatus = data.analysis;
    renderDetail();
    startAnalysisPolling();
    showToast(`${plural(preparedFiles.length, 'image')} saved. The reader will process the full set in the background.`);
    await loadCatalogue();
  } catch (error) {
    showToast(error.message, true, 6500);
  } finally {
    setBusy(false);
    clearUploadInputs();
  }
}

function clearUploadInputs() {
  ['#photo-input', '#photo-library-input', '#rubbing-input'].forEach(selector => {
    const input = document.querySelector(selector);
    if (input) input.value = '';
  });
}

async function optimiseImage(file) {
  if (!file.type.startsWith('image/')) throw new Error('Choose an image file.');
  try {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    const maxDimension = 2400;
    const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    const context = canvas.getContext('2d', { alpha: false });
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    if (!blob) return file;
    const baseName = file.name.replace(/\.[^.]+$/, '') || 'engraving';
    return new File([blob], `${baseName}.jpg`, { type: 'image/jpeg', lastModified: Date.now() });
  } catch {
    if (file.size > 12 * 1024 * 1024) throw new Error('This image cannot be resized on the phone and is larger than 12 MB.');
    return file;
  }
}

async function analyseAll() {
  if (!state.current) return;
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}/analyse`, { method: 'POST', body: '{}' });
    state.analysisStatus = data.analysis;
    renderReaderNote();
    startAnalysisPolling();
    showToast('A fresh background reading has been queued.');
  } catch (error) {
    showToast(error.message, true, 6500);
  }
}

function startAnalysisPolling() {
  stopAnalysisPolling();
  if (!state.current) return;
  pollAnalysisStatus();
}

function stopAnalysisPolling() {
  if (state.analysisPollTimer) clearTimeout(state.analysisPollTimer);
  state.analysisPollTimer = null;
}

async function pollAnalysisStatus() {
  const trophyId = state.current?.id;
  if (!trophyId) return;
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(trophyId)}/analysis-status`);
    if (state.current?.id !== trophyId) return;
    state.analysisStatus = data.analysis;
    renderReaderNote();

    if (['queued', 'processing'].includes(data.analysis.status)) {
      state.analysisPollTimer = setTimeout(pollAnalysisStatus, 1800);
      return;
    }

    if (['complete', 'failed'].includes(data.analysis.status)) {
      await refreshCurrent();
      await loadCatalogue();
      const noticeKey = `${data.analysis.status}:${data.analysis.updatedAt}`;
      if (state.analysisNoticeShownFor !== noticeKey) {
        state.analysisNoticeShownFor = noticeKey;
        showToast(
          data.analysis.status === 'complete' ? 'Background reading finished. Check the proposed winners below.' : data.analysis.message,
          data.analysis.status === 'failed',
          data.analysis.status === 'failed' ? 6500 : 4500,
        );
      }
    }
  } catch {
    if (state.current?.id === trophyId) state.analysisPollTimer = setTimeout(pollAnalysisStatus, 3500);
  }
}

async function markComplete() {
  const reviewCount = state.current.winners.filter(winner => winner.reviewState !== 'confirmed').length;
  const warnings = [];
  if (reviewCount) warnings.push(`${plural(reviewCount, 'reading')} still ${reviewCount === 1 ? 'needs' : 'need'} confirmation`);
  if (state.missingYears.length) warnings.push(`${plural(state.missingYears.length, 'year')} ${state.missingYears.length === 1 ? 'is' : 'are'} missing`);
  if (warnings.length && !confirm(`Save this list as complete?\n\n${warnings.join(' and ')}.`)) return;
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}/complete`, { method: 'POST', body: '{}' });
    state.current = data.trophy;
    state.missingYears = data.missingYears || [];
    renderDetail();
    await loadCatalogue();
    showToast('Confirmed list saved to the archive.');
  } catch (error) {
    showToast(error.message, true);
  }
}

async function refreshCurrent() {
  if (!state.current) return;
  const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}`);
  state.current = data.trophy;
  state.missingYears = data.missingYears || [];
  renderDetail();
}

function openEvidence(id) {
  const evidence = state.current?.evidence.find(item => item.id === id);
  if (!evidence) return;
  state.activeEvidenceId = id;
  const image = document.querySelector('#dialog-image');
  image.src = evidence.url;
  setText('#dialog-image-label', `${capitalize(evidence.kind)} · ${formatDate(evidence.uploadedAt)}`);
  document.querySelector('#image-dialog').showModal();
}

async function deleteEvidence() {
  if (!state.activeEvidenceId || !confirm('Delete this evidence image? This cannot be undone.')) return;
  try {
    await api(`/api/trophies/${encodeURIComponent(state.current.id)}/images/${encodeURIComponent(state.activeEvidenceId)}`, { method: 'DELETE' });
    document.querySelector('#image-dialog').close();
    state.activeEvidenceId = null;
    await refreshCurrent();
    await loadCatalogue();
    showToast('Image deleted.');
  } catch (error) {
    showToast(error.message, true);
  }
}

function showLogin(message = '') {
  elements.login.hidden = false;
  document.querySelector('#login-error').hidden = !message;
  document.querySelector('#login-error').textContent = message;
  setTimeout(() => document.querySelector('#login-password')?.focus(), 50);
}

function displayStatus(trophy) {
  if (trophy.status === 'complete') return { key: 'complete', label: 'Complete' };
  if ((trophy.needsReviewCount || 0) > 0) return { key: 'review', label: 'Needs review' };
  if (trophy.status === 'in-progress') return { key: 'progress', label: 'In progress' };
  return { key: 'todo', label: 'To do' };
}

function setBusy(visible, title = '', copy = '') {
  elements.busy.hidden = !visible;
  if (visible) {
    setText('#busy-title', title);
    setText('#busy-copy', copy);
  }
}

let toastTimer;
function showToast(message, isError = false, duration = 3500) {
  clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.className = `toast is-visible ${isError ? 'is-error' : ''}`;
  toastTimer = setTimeout(() => { elements.toast.className = 'toast'; }, duration);
}

function setText(selector, value) {
  const element = document.querySelector(selector);
  if (element) element.textContent = value;
}

function plural(count, noun) { return `${count} ${noun}${count === 1 ? '' : 's'}`; }
function capitalize(value) { return value.charAt(0).toUpperCase() + value.slice(1); }
function formatDate(value) { return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(value)); }
function trophyIdFromHash() { return location.hash.startsWith('#trophy/') ? decodeURIComponent(location.hash.slice(8)) : null; }
function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]);
}
function addImageFallback(image) {
  image.addEventListener('error', () => {
    if (!image.src.endsWith('/catalogue/fallback.svg')) image.src = '/catalogue/fallback.svg';
  }, { once: true });
}

document.querySelectorAll('.filter-chip').forEach(button => {
  button.addEventListener('click', () => {
    document.querySelector('.filter-chip.is-active')?.classList.remove('is-active');
    button.classList.add('is-active');
    state.filter = button.dataset.filter;
    renderTrophies();
  });
});
elements.search.addEventListener('input', renderTrophies);
elements.grid.addEventListener('click', event => {
  const card = event.target.closest('.trophy-card');
  if (card) openTrophy(card.dataset.id);
});
elements.photoStrip.addEventListener('click', event => {
  const evidence = event.target.closest('.evidence-thumb');
  if (evidence) openEvidence(evidence.dataset.evidenceId);
});
elements.winnerList.addEventListener('click', event => {
  const action = event.target.closest('[data-action]');
  if (!action) return;
  const row = action.closest('.winner-row');
  if (action.dataset.action === 'confirm') saveWinner(row);
  if (action.dataset.action === 'delete') deleteWinner(row.dataset.winnerId);
  if (action.dataset.action === 'cancel') renderWinners();
});
document.querySelector('#back-button').addEventListener('click', () => closeTrophy());
document.querySelector('#photo-input').addEventListener('change', event => uploadFiles([...event.target.files], 'photo'));
document.querySelector('#rubbing-input').addEventListener('change', event => uploadFiles([...event.target.files], 'rubbing'));
document.querySelector('#analyse-button').addEventListener('click', analyseAll);
document.querySelector('#add-winner-button').addEventListener('click', () => renderWinners(true));
document.querySelector('#save-range-button').addEventListener('click', saveTimeline);
document.querySelector('#complete-button').addEventListener('click', markComplete);
document.querySelector('#close-image-button').addEventListener('click', () => document.querySelector('#image-dialog').close());
document.querySelector('#delete-image-button').addEventListener('click', deleteEvidence);
document.querySelector('#image-dialog').addEventListener('click', event => {
  if (event.target === event.currentTarget) event.currentTarget.close();
});
window.addEventListener('popstate', () => {
  const id = trophyIdFromHash();
  if (id) openTrophy(id, false); else closeTrophy(false);
});
