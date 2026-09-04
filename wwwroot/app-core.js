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
let engravingInstructionsSavePromise = null;
const evidenceViewer = {
  region: null,
  zoom: 1,
  baseWidth: 0,
  baseHeight: 0,
  loadToken: 0,
  initializedToken: 0,
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
  if (!document.querySelector('link[href^="/async.css"]')) {
    const stylesheet = document.createElement('link');
    stylesheet.rel = 'stylesheet';
    stylesheet.href = '/async.css?v=20260904-empty-reading-1';
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
  document.querySelector('#detail-division').value = trophy.division || 'mixed';
  document.querySelector('#detail-award-format').value = trophy.awardFormat || 'unknown';
  renderEngravingInstructions();
  document.querySelector('#analyse-button').disabled = trophy.evidence.length === 0 || !state.aiConfigured;
  document.querySelector('#ai-setup-note').hidden = state.aiConfigured;
  renderEvidence();
  renderReaderNote();
  renderTeamAwardQuestion();
  renderMissingYears();
  renderWinners();
}

function renderTeamAwardQuestion() {
  const question = document.querySelector('#team-award-question');
  const show = state.current?.awardFormat === 'unknown' && state.current?.teamAwardSuggested;
  question.hidden = !show;
  if (!show) return;
  document.querySelector('#team-award-reason').textContent = state.current.teamAwardSuggestionReason
    || 'Several distinct player names appear to be grouped under the same year.';
}

async function saveAwardFormat(awardFormat) {
  if (!state.current || !await saveEngravingInstructions()) return;
  const trophyId = state.current.id;
  const controls = [document.querySelector('#detail-award-format'), ...document.querySelectorAll('#team-award-question button')];
  controls.forEach(control => { control.disabled = true; });
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(trophyId)}/award-format`, {
      method: 'PUT',
      body: JSON.stringify({ awardFormat }),
    });
    if (state.current?.id !== trophyId) return;
    state.current = data.trophy;
    state.missingYears = data.missingYears || [];
    renderDetail();

    if (state.current.evidence.length && state.aiConfigured) {
      const queued = await api(`/api/trophies/${encodeURIComponent(trophyId)}/analyse`, { method: 'POST', body: '{}' });
      state.analysisStatus = queued.analysis;
      renderReaderNote();
      renderEmptyWinners();
      startAnalysisPolling();
      showToast(awardFormat === 'team'
        ? 'Team award confirmed. The reader will now extract every visible player.'
        : awardFormat === 'individual'
          ? 'Individual award confirmed. The photos are being read again.'
          : 'Automatic team detection restored. The photos are being read again.');
    } else {
      showToast(awardFormat === 'team' ? 'Team award saved.' : awardFormat === 'individual' ? 'Individual award saved.' : 'Automatic team detection restored.');
    }
  } catch (error) {
    showToast(error.message, true, 6500);
    if (state.current?.id === trophyId) document.querySelector('#detail-award-format').value = state.current.awardFormat || 'unknown';
  } finally {
    controls.forEach(control => { control.disabled = false; });
  }
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
    note.innerHTML = '<span class="reader-spark" aria-hidden="true">✦</span><span><strong>Add the first winner-record photo</strong><small>Use a trophy inscription, honours board, plaque, results sheet or historical note. Multiple images will be read together.</small></span>';
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
  const rows = addBlank ? [null, ...winners] : winners;
  elements.winnerList.innerHTML = rows.map(winnerRow).join('');
  renderEmptyWinners(winners.length);
  const reviewCount = winners.filter(winner => winner.reviewState !== 'confirmed').length;
  const gapCount = state.missingYears.length;
  setText('#save-summary', `${plural(winners.length, 'winner')}${reviewCount ? ` · ${reviewCount} to check` : ''}${gapCount ? ` · ${plural(gapCount, 'gap')}` : ''}`);
  if (addBlank) elements.winnerList.querySelector('[data-winner-id="new"] input[name="year"]')?.focus();
}

function renderEngravingInstructions() {
  const panel = document.querySelector('#engraving-instructions-panel');
  const input = document.querySelector('#engraving-instructions');
  const status = document.querySelector('#engraving-instructions-status');
  const button = document.querySelector('#save-engraving-instructions');
  const instructions = state.current?.engravingInstructions || '';
  const editing = panel.contains(document.activeElement);
  if (!editing) {
    input.value = instructions;
    panel.open = Boolean(instructions);
  }
  status.textContent = instructions ? 'Saved with this trophy' : 'Not required';
  status.className = instructions ? 'is-saved' : '';
  button.disabled = input.value.trim() === instructions;
}

async function saveEngravingInstructions(showSuccess = false) {
  if (!state.current) return false;
  if (engravingInstructionsSavePromise) await engravingInstructionsSavePromise;

  const input = document.querySelector('#engraving-instructions');
  const status = document.querySelector('#engraving-instructions-status');
  const button = document.querySelector('#save-engraving-instructions');
  const trophyId = state.current.id;
  const instructions = input.value.trim();
  if (instructions === (state.current.engravingInstructions || '')) {
    renderEngravingInstructions();
    if (showSuccess) showToast(instructions ? 'Source-reading instructions saved.' : 'No source-reading instructions are set.');
    return true;
  }

  status.textContent = 'Saving…';
  status.className = 'is-saving';
  button.disabled = true;
  engravingInstructionsSavePromise = (async () => {
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(trophyId)}/engraving-instructions`, {
        method: 'PUT',
        body: JSON.stringify({ instructions: instructions || null }),
      });
      if (state.current?.id === trophyId) {
        state.current = data.trophy;
        state.missingYears = data.missingYears || [];
        renderEngravingInstructions();
      }
      if (showSuccess) showToast(instructions ? 'Source-reading instructions saved.' : 'Source-reading instructions removed.');
      return true;
    } catch (error) {
      status.textContent = 'Could not save';
      status.className = 'is-error';
      button.disabled = false;
      showToast(error.message, true, 6500);
      return false;
    } finally {
      engravingInstructionsSavePromise = null;
    }
  })();
  return engravingInstructionsSavePromise;
}

function renderEmptyWinners(winnerCount = state.current?.winners?.length || 0) {
  const empty = document.querySelector('#empty-winners');
  if (!empty) return;
  const hasManualRow = Boolean(elements.winnerList.querySelector('[data-winner-id="new"]'));
  empty.hidden = winnerCount > 0 || hasManualRow;
  if (empty.hidden) return;

  const analysis = state.analysisStatus;
  const evidence = state.current?.evidence || [];
  const evidenceIsBeingRead = evidence.some(image => ['pending', 'queued', 'processing'].includes(image.processingState));
  const analysisIsActive = ['queued', 'processing'].includes(analysis?.status) || (!analysis && evidenceIsBeingRead);
  empty.className = `empty-winners${analysisIsActive ? ' is-reading' : ''}${analysis?.status === 'failed' ? ' is-warning' : ''}`;

  if (analysisIsActive) {
    const imageCount = analysis?.evidenceCount || evidence.length;
    const detail = analysis?.status === 'queued'
      ? 'Your images are saved and waiting for the background reader.'
      : `The AI is comparing ${plural(imageCount, 'image')}. Names and years will appear here when the reading finishes.`;
    empty.innerHTML = `<span aria-hidden="true">✦</span><strong>Reading names from your images</strong><p>${escapeHtml(detail)}</p>`;
    return;
  }

  if (analysis?.status === 'complete' && evidence.length) {
    empty.innerHTML = '<span aria-hidden="true">✓</span><strong>No winners found in this reading</strong><p>Try another photograph or enter a winner manually.</p>';
    return;
  }

  if (analysis?.status === 'failed') {
    empty.innerHTML = '<span aria-hidden="true">!</span><strong>The images could not be read</strong><p>Try reading all images again or enter a winner manually.</p>';
    return;
  }

  empty.innerHTML = '<span aria-hidden="true">✦</span><strong>No winners recorded yet</strong><p>Add a photograph or enter a winner manually.</p>';
}

function winnerRow(winner) {
  const isNew = !winner;
  const reviewState = winner?.reviewState || 'needs-review';
  const confidence = winner?.confidence ?? 1;
  const uncertain = !isNew && confidence < 0.75;
  return `
    <article class="winner-row ${uncertain ? 'is-uncertain' : ''} ${isNew ? 'is-new' : ''}" data-winner-id="${winner?.id || 'new'}">
      <label><span>Year</span><input name="year" type="number" min="1800" max="2200" inputmode="numeric" value="${winner?.year || ''}" aria-label="Winning year"></label>
      <div class="winner-fields">
        <label class="winner-name"><span>Winner</span><input name="name" maxlength="200" value="${escapeHtml(winner?.name || '')}" aria-label="Winner name" placeholder="Name shown in the source">${winnerEvidenceButton(winner)}</label>
        <label class="winner-description"><span>Description</span><input name="description" maxlength="500" value="${escapeHtml(winner?.description || '')}" aria-label="Public winner description" placeholder="Optional public wording for this result"></label>
        ${winner?.extractionNotes ? `<aside class="winner-extraction-notes" aria-label="Read-only AI reading notes"><strong>AI reading notes</strong><p>${escapeHtml(winner.extractionNotes)}</p></aside>` : ''}
      </div>
      <div class="confidence ${reviewState} ${uncertain ? 'uncertain' : ''}">
        ${isNew ? '<span>Manual</span><small>New</small>' : `<span>${Math.round(confidence * 100)}%</span><small>${reviewState === 'confirmed' ? 'Confirmed' : uncertain ? 'Uncertain' : 'Check'}</small>`}
      </div>
      <div class="winner-actions">
        <button class="confirm-winner" data-action="confirm" type="button" title="Save and confirm">${isNew ? 'Add' : reviewState === 'confirmed' ? 'Save' : 'Confirm'}</button>
        ${isNew ? '<button class="cancel-winner" data-action="cancel" type="button" aria-label="Cancel new winner">×</button>' : '<button class="delete-winner" data-action="delete" type="button" aria-label="Delete winner">×</button>'}
      </div>
    </article>`;
}

function winnerEvidenceButton(winner) {
  if (!winner) return '';
  const reference = winner.evidenceReference;
  const availableIds = new Set((state.current?.evidence || []).map(image => image.id));
  const evidenceId = reference?.imageId && availableIds.has(reference.imageId)
    ? reference.imageId
    : (winner.evidenceImageIds || []).find(id => availableIds.has(id));
  if (!evidenceId) return '';
  const region = reference?.imageId === evidenceId
    ? ` data-region-x="${Number(reference.x)}" data-region-y="${Number(reference.y)}" data-region-width="${Number(reference.width)}" data-region-height="${Number(reference.height)}"`
    : '';
  const label = reference?.imageId === evidenceId ? 'View AI source' : 'View source photo';
  return `<button class="winner-evidence-link" data-action="evidence" data-evidence-id="${escapeHtml(evidenceId)}"${region} type="button" title="${label}"><span aria-hidden="true">⌖</span>${label}</button>`;
}

async function saveWinner(row) {
  const id = row.dataset.winnerId;
  const year = Number(row.querySelector('[name="year"]').value);
  const name = row.querySelector('[name="name"]').value.trim();
  const description = row.querySelector('[name="description"]').value.trim();
  if (!year || !name) {
    showToast('Enter both the year and winner’s name.', true);
    return;
  }
  const payload = JSON.stringify({ year, name, reviewState: 'confirmed', description: description || null });
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
  if (!await saveEngravingInstructions()) {
    clearUploadInputs();
    return;
  }
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
  ['#photo-input', '#photo-library-input'].forEach(selector => {
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
    const baseName = file.name.replace(/\.[^.]+$/, '') || 'winner-record';
    return new File([blob], `${baseName}.jpg`, { type: 'image/jpeg', lastModified: Date.now() });
  } catch {
    if (file.size > 12 * 1024 * 1024) throw new Error('This image cannot be resized on the phone and is larger than 12 MB.');
    return file;
  }
}

async function analyseAll() {
  if (!state.current) return;
  if (!await saveEngravingInstructions()) return;
  try {
    const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}/analyse`, { method: 'POST', body: '{}' });
    state.analysisStatus = data.analysis;
    renderReaderNote();
    renderEmptyWinners();
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
    renderEmptyWinners();

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
          data.analysis.status === 'complete'
            ? state.current?.teamAwardSuggested
              ? 'Several names may belong to one winning team. Please answer the team trophy question.'
              : 'Background reading finished. Check the proposed winners below.'
            : data.analysis.message,
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

function normalizeEvidenceRegion(region) {
  if (!region || ![region.x, region.y, region.width, region.height].every(Number.isFinite)) return null;
  const x = Math.max(0, Math.min(999, Number(region.x)));
  const y = Math.max(0, Math.min(999, Number(region.y)));
  return {
    x,
    y,
    width: Math.max(1, Math.min(1000 - x, Number(region.width))),
    height: Math.max(1, Math.min(1000 - y, Number(region.height))),
  };
}

function evidenceRegionCentre() {
  const region = evidenceViewer.region;
  return region
    ? { x: (region.x + region.width / 2) / 1000, y: (region.y + region.height / 2) / 1000 }
    : { x: .5, y: .5 };
}

function updateEvidenceZoomControls() {
  const percentage = Math.round(evidenceViewer.zoom * 100);
  document.querySelector('#image-zoom-level').value = `${percentage}%`;
  document.querySelector('#zoom-image-out-button').disabled = evidenceViewer.zoom <= 1.01;
  document.querySelector('#zoom-image-in-button').disabled = evidenceViewer.zoom >= 7.99;
  document.querySelector('#show-whole-image-button').disabled = evidenceViewer.zoom <= 1.01;
}

function applyEvidenceZoom(nextZoom, targetPoint = null) {
  const viewport = document.querySelector('#image-dialog-viewport');
  const canvas = document.querySelector('#image-dialog-canvas');
  const stage = document.querySelector('#image-dialog-stage');
  if (!evidenceViewer.baseWidth || !evidenceViewer.baseHeight || !viewport.clientWidth || !viewport.clientHeight) return;

  let point = targetPoint;
  if (!point && stage.offsetWidth && stage.offsetHeight) {
    point = {
      x: Math.max(0, Math.min(1, (viewport.scrollLeft + viewport.clientWidth / 2 - stage.offsetLeft) / stage.offsetWidth)),
      y: Math.max(0, Math.min(1, (viewport.scrollTop + viewport.clientHeight / 2 - stage.offsetTop) / stage.offsetHeight)),
    };
  }
  point ||= { x: .5, y: .5 };

  evidenceViewer.zoom = Math.max(1, Math.min(8, nextZoom));
  const stageWidth = Math.round(evidenceViewer.baseWidth * evidenceViewer.zoom);
  const stageHeight = Math.round(evidenceViewer.baseHeight * evidenceViewer.zoom);
  const canvasWidth = Math.max(viewport.clientWidth, stageWidth);
  const canvasHeight = Math.max(viewport.clientHeight, stageHeight);
  canvas.style.width = `${canvasWidth}px`;
  canvas.style.height = `${canvasHeight}px`;
  stage.style.width = `${stageWidth}px`;
  stage.style.height = `${stageHeight}px`;
  stage.style.left = `${Math.round((canvasWidth - stageWidth) / 2)}px`;
  stage.style.top = `${Math.round((canvasHeight - stageHeight) / 2)}px`;

  requestAnimationFrame(() => {
    const left = stage.offsetLeft + stageWidth * point.x - viewport.clientWidth / 2;
    const top = stage.offsetTop + stageHeight * point.y - viewport.clientHeight / 2;
    viewport.scrollTo({ left: Math.max(0, left), top: Math.max(0, top), behavior: 'auto' });
  });
  updateEvidenceZoomControls();
}

function focusEvidenceSource() {
  const region = evidenceViewer.region;
  if (!region) return;
  const viewport = document.querySelector('#image-dialog-viewport');
  const regionWidth = evidenceViewer.baseWidth * region.width / 1000;
  const regionHeight = evidenceViewer.baseHeight * region.height / 1000;
  const horizontalZoom = viewport.clientWidth * .72 / Math.max(1, regionWidth);
  const verticalZoom = viewport.clientHeight * .62 / Math.max(1, regionHeight);
  applyEvidenceZoom(Math.max(1, Math.min(8, horizontalZoom, verticalZoom)), evidenceRegionCentre());
}

function initializeEvidenceViewer(focusSource = false) {
  const viewport = document.querySelector('#image-dialog-viewport');
  const image = document.querySelector('#dialog-image');
  if (!image.naturalWidth || !image.naturalHeight || !viewport.clientWidth || !viewport.clientHeight) return;
  const fitScale = Math.min(viewport.clientWidth / image.naturalWidth, viewport.clientHeight / image.naturalHeight);
  evidenceViewer.baseWidth = Math.max(1, image.naturalWidth * fitScale);
  evidenceViewer.baseHeight = Math.max(1, image.naturalHeight * fitScale);
  evidenceViewer.zoom = 1;
  if (focusSource && evidenceViewer.region) focusEvidenceSource();
  else applyEvidenceZoom(1, { x: .5, y: .5 });
}

function updateEvidenceNavigation() {
  const images = state.current?.evidence || [];
  const index = images.findIndex(item => item.id === state.activeEvidenceId);
  const total = images.length;
  const previous = document.querySelector('#previous-image-button');
  const next = document.querySelector('#next-image-button');
  const position = document.querySelector('#dialog-image-position');
  const hasMultiple = total > 1;

  previous.hidden = !hasMultiple;
  next.hidden = !hasMultiple;
  previous.disabled = index <= 0;
  next.disabled = index < 0 || index >= total - 1;
  position.value = index >= 0 ? `Photo ${index + 1} of ${total}` : '';
  position.textContent = position.value;
  const image = document.querySelector('#dialog-image');
  if (image) image.alt = index >= 0 ? `Uploaded winner-record photo ${index + 1} of ${total}` : 'Uploaded winner-record photo';
}

function navigateEvidence(direction) {
  const images = state.current?.evidence || [];
  const index = images.findIndex(item => item.id === state.activeEvidenceId);
  const target = images[index + direction];
  if (target) openEvidence(target.id);
}

function openEvidence(id, region = null, contextLabel = '') {
  const evidence = state.current?.evidence.find(item => item.id === id);
  if (!evidence) return;
  state.activeEvidenceId = id;
  updateEvidenceNavigation();
  const image = document.querySelector('#dialog-image');
  const marker = document.querySelector('#dialog-evidence-region');
  const dialog = document.querySelector('#image-dialog');
  evidenceViewer.region = normalizeEvidenceRegion(region);
  evidenceViewer.baseWidth = 0;
  evidenceViewer.baseHeight = 0;
  evidenceViewer.zoom = 1;
  const loadToken = ++evidenceViewer.loadToken;
  evidenceViewer.initializedToken = 0;
  marker.hidden = !evidenceViewer.region;
  document.querySelector('#focus-source-button').hidden = !evidenceViewer.region;
  if (evidenceViewer.region) {
    marker.style.left = `${evidenceViewer.region.x / 10}%`;
    marker.style.top = `${evidenceViewer.region.y / 10}%`;
    marker.style.width = `${evidenceViewer.region.width / 10}%`;
    marker.style.height = `${evidenceViewer.region.height / 10}%`;
  }
  elements.photoStrip.querySelectorAll('.evidence-thumb').forEach(button => button.classList.toggle('is-source', button.dataset.evidenceId === id));
  setText('#dialog-image-label', contextLabel || `${capitalize(evidence.kind)} · ${formatDate(evidence.uploadedAt)}`);
  const initialize = () => {
    if (loadToken !== evidenceViewer.loadToken || evidenceViewer.initializedToken === loadToken) return;
    evidenceViewer.initializedToken = loadToken;
    requestAnimationFrame(() => initializeEvidenceViewer(Boolean(evidenceViewer.region)));
  };
  image.onload = initialize;
  image.src = evidence.url;
  if (!dialog.open) dialog.showModal();
  updateEvidenceZoomControls();
  if (image.complete && image.naturalWidth) initialize();
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
  if (action.dataset.action === 'evidence') {
    event.preventDefault();
    event.stopPropagation();
    const values = ['regionX', 'regionY', 'regionWidth', 'regionHeight'].map(key => Number(action.dataset[key]));
    const hasRegion = values.every(Number.isFinite) && values.every(value => value >= 0);
    const name = row.querySelector('[name="name"]')?.value.trim();
    const year = row.querySelector('[name="year"]')?.value;
    openEvidence(action.dataset.evidenceId, hasRegion ? { x: values[0], y: values[1], width: values[2], height: values[3] } : null, hasRegion ? `Highlighted source for ${year} · ${name}` : `Source photo for ${year} · ${name}`);
    return;
  }
  if (action.dataset.action === 'confirm') saveWinner(row);
  if (action.dataset.action === 'delete') deleteWinner(row.dataset.winnerId);
  if (action.dataset.action === 'cancel') renderWinners();
});
document.querySelector('.club-mark').addEventListener('click', event => {
  event.preventDefault();
  closeTrophy();
});
document.querySelector('#back-button').addEventListener('click', () => closeTrophy());
document.querySelector('#photo-input').addEventListener('change', event => uploadFiles([...event.target.files], 'photo'));
document.querySelector('#photo-library-input').addEventListener('change', event => uploadFiles([...event.target.files], 'photo'));
document.querySelector('#engraving-instructions').addEventListener('input', event => {
  const saved = state.current?.engravingInstructions || '';
  const changed = event.currentTarget.value.trim() !== saved;
  const status = document.querySelector('#engraving-instructions-status');
  status.textContent = changed ? 'Not saved' : saved ? 'Saved with this trophy' : 'Not required';
  status.className = changed ? 'is-unsaved' : saved ? 'is-saved' : '';
  document.querySelector('#save-engraving-instructions').disabled = !changed;
});
document.querySelector('#engraving-instructions').addEventListener('blur', () => saveEngravingInstructions());
document.querySelector('#save-engraving-instructions').addEventListener('click', () => saveEngravingInstructions(true));
document.querySelector('#detail-award-format').addEventListener('change', event => saveAwardFormat(event.currentTarget.value));
document.querySelector('#team-award-question').addEventListener('click', event => {
  const button = event.target.closest('[data-award-format]');
  if (button) saveAwardFormat(button.dataset.awardFormat);
});
document.querySelector('#analyse-button').addEventListener('click', analyseAll);
document.querySelector('#add-winner-button').addEventListener('click', () => renderWinners(true));
document.querySelector('#save-range-button').addEventListener('click', saveTimeline);
document.querySelector('#complete-button').addEventListener('click', markComplete);
document.querySelector('#close-image-button').addEventListener('click', () => document.querySelector('#image-dialog').close());
document.querySelector('#previous-image-button').addEventListener('click', () => navigateEvidence(-1));
document.querySelector('#next-image-button').addEventListener('click', () => navigateEvidence(1));
document.querySelector('#delete-image-button').addEventListener('click', deleteEvidence);
document.querySelector('#focus-source-button').addEventListener('click', focusEvidenceSource);
document.querySelector('#show-whole-image-button').addEventListener('click', () => applyEvidenceZoom(1, { x: .5, y: .5 }));
document.querySelector('#zoom-image-out-button').addEventListener('click', () => applyEvidenceZoom(evidenceViewer.zoom / 1.5));
document.querySelector('#zoom-image-in-button').addEventListener('click', () => applyEvidenceZoom(evidenceViewer.zoom * 1.5));
document.querySelector('#image-dialog').addEventListener('click', event => {
  if (event.target === event.currentTarget) event.currentTarget.close();
});
document.querySelector('#image-dialog').addEventListener('keydown', event => {
  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
  const viewport = document.querySelector('#image-dialog-viewport');
  if (event.target === viewport && evidenceViewer.zoom > 1.01) return;
  event.preventDefault();
  navigateEvidence(event.key === 'ArrowLeft' ? -1 : 1);
});
window.addEventListener('resize', () => {
  if (document.querySelector('#image-dialog').open) initializeEvidenceViewer(Boolean(evidenceViewer.region));
});
window.addEventListener('popstate', () => {
  const id = trophyIdFromHash();
  if (id) openTrophy(id, false); else closeTrophy(false);
});
