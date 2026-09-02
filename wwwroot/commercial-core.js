(() => {
  const commercial = {
    illustrationConfigured: false,
    memberDirectory: null,
  };

  installHeaderLink();
  installBalanceIndicator();
  installNewTrophyFlow();
  installMemberDirectory();
  installTrophyPhotoManager();
  installIllustrationControl();
  installMatchEnhancer();
  refreshCapabilities();

  function installHeaderLink() {
    const actions = document.querySelector('.header-actions');
    if (!actions || actions.querySelector('.plans-link')) return;
    const link = document.createElement('a');
    link.className = 'plans-link';
    link.href = '/';
    link.textContent = 'Plans';
    actions.prepend(link);
  }

  function catalogueActions() {
    const heading = document.querySelector('.catalogue-heading');
    if (!heading) return null;
    let actions = heading.querySelector('.catalogue-actions');
    if (!actions) {
      actions = document.createElement('div');
      actions.className = 'catalogue-actions';
      heading.append(actions);
    }
    return actions;
  }

  function installBalanceIndicator() {
    const actions = catalogueActions();
    if (!actions || actions.querySelector('#credit-balance')) return;
    const balance = document.createElement('a');
    balance.id = 'credit-balance';
    balance.className = 'credit-balance';
    balance.href = '/#pricing';
    balance.innerHTML = '<span><small>Trophy credits</small><strong id="credit-balance-value">—</strong></span><em>View plans</em>';
    balance.setAttribute('aria-label', 'Trophy credit balance. View plans.');
    actions.append(balance);
  }

  function updateBalanceIndicator(balance) {
    const value = document.querySelector('#credit-balance-value');
    const link = document.querySelector('#credit-balance');
    if (!value || !link) return;
    if (balance?.unlimited) {
      value.textContent = 'Unlimited';
      link.setAttribute('aria-label', 'Trophy credit balance: unlimited. View plans.');
      return;
    }
    const credits = Number(balance?.trophyCredits || 0);
    value.textContent = credits + (credits === 1 ? ' credit' : ' credits');
    link.setAttribute('aria-label', 'Trophy credit balance: ' + credits + '. View plans.');
  }

  function installNewTrophyFlow() {
    const actions = catalogueActions();
    if (!actions || document.querySelector('#new-trophy-button')) return;
    const button = document.createElement('button');
    button.id = 'new-trophy-button';
    button.className = 'new-trophy-button';
    button.type = 'button';
    button.innerHTML = '<span aria-hidden="true">+</span><span><strong>Add trophy</strong><small>Photograph a new piece</small></span>';
    actions.append(button);

    const dialog = document.createElement('dialog');
    dialog.id = 'new-trophy-dialog';
    dialog.className = 'commercial-dialog';
    dialog.innerHTML = `
      <form id="new-trophy-form">
        <button class="commercial-dialog-close" type="button" aria-label="Close">×</button>
        <p class="step-label">New archive record</p>
        <h2>Add a trophy</h2>
        <p>Create the record first, then upload several photographs for inscription reading and a generated catalogue illustration.</p>
        <label><span>Trophy name</span><input name="name" maxlength="160" required placeholder="e.g. Ladies Challenge Cup"></label>
        <div class="commercial-form-grid">
          <label><span>Category</span><input name="category" maxlength="80" required placeholder="e.g. Golf, Rugby, Cricket"></label>
          <label><span>Reference code <em>optional</em></span><input name="code" maxlength="24" placeholder="Auto-generated"></label>
        </div>
        <label><span>Alternative name <em>optional</em></span><input name="secondaryName" maxlength="160" placeholder="Name engraved on the base"></label>
        <button class="commercial-submit" type="submit">Create trophy record</button>
        <p class="commercial-form-error" role="alert" hidden></p>
      </form>`;
    document.body.append(dialog);

    button.addEventListener('click', () => dialog.showModal());
    dialog.querySelector('.commercial-dialog-close').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
    dialog.querySelector('form').addEventListener('submit', createTrophy);
  }

  async function createTrophy(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const error = form.querySelector('.commercial-form-error');
    const submit = form.querySelector('[type="submit"]');
    error.hidden = true;
    submit.disabled = true;
    try {
      const values = new FormData(form);
      const data = await api('/api/trophies', {
        method: 'POST',
        body: JSON.stringify({
          name: values.get('name'),
          secondaryName: values.get('secondaryName') || null,
          category: values.get('category'),
          code: values.get('code') || null,
        }),
      });
      document.querySelector('#new-trophy-dialog').close();
      form.reset();
      await loadCatalogue();
      await openTrophy(data.trophy.id);
      showToast('Trophy created. Add photographs from several angles next.');
    } catch (exception) {
      error.textContent = exception.message;
      error.hidden = false;
    } finally {
      submit.disabled = false;
    }
  }

  function installMemberDirectory() {
    const actions = catalogueActions();
    if (!actions || document.querySelector('#member-directory-control')) return;
    const control = document.createElement('div');
    control.id = 'member-directory-control';
    control.className = 'member-directory-control';
    control.innerHTML = `
      <label class="member-upload-button" title="Dates of birth are reduced to birth year and the original file is not retained.">
        <span class="member-upload-icon" aria-hidden="true">CSV</span>
        <span><strong id="member-upload-title">Upload member information</strong><small id="member-directory-summary">CSV or Excel member export</small></span>
        <input id="member-file-input" type="file" accept=".csv,.tsv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" hidden>
      </label>
      <button id="clear-member-directory" type="button" title="Remove imported member information" aria-label="Remove imported member information" hidden>×</button>`;
    const trophyButton = actions.querySelector('#new-trophy-button');
    if (trophyButton) trophyButton.before(control);
    else actions.append(control);
    control.querySelector('#member-file-input').addEventListener('change', importMembers);
    control.querySelector('#clear-member-directory').addEventListener('click', clearMembers);
  }

  async function refreshCapabilities() {
    try {
      const auth = await api('/api/auth/status');
      commercial.illustrationConfigured = Boolean(auth.illustrationConfigured);
      updateBalanceIndicator(auth.balance);
      if (auth.authenticated) await refreshMemberSummary();
      updateIllustrationControl();
    } catch { }
  }

  async function refreshMemberSummary() {
    try {
      const data = await api('/api/members');
      commercial.memberDirectory = data.directory;
      const summary = document.querySelector('#member-directory-summary');
      const title = document.querySelector('#member-upload-title');
      const control = document.querySelector('#member-directory-control');
      const clear = document.querySelector('#clear-member-directory');
      if (!summary || !title || !control || !clear) return;
      if (data.directory.memberCount) {
        title.textContent = 'Update member information';
        summary.textContent = data.directory.memberCount + ' members loaded' + (data.directory.sourceName ? ' · ' + data.directory.sourceName : '');
        control.classList.add('has-data');
        clear.hidden = false;
      } else {
        title.textContent = 'Upload member information';
        summary.textContent = 'CSV or Excel member export';
        control.classList.remove('has-data');
        clear.hidden = true;
      }
    } catch { }
  }

  async function importMembers(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file, file.name);
    setBusy(true, 'Importing member directory…', 'Normalising names and reducing dates of birth to birth year.');
    try {
      const data = await api('/api/members/import', { method: 'POST', body: form });
      commercial.memberDirectory = data.directory;
      await refreshMemberSummary();
      if (state.current) await refreshCurrent();
      showToast(`${data.result.importedCount} members imported and compared with the winners archive.`);
    } catch (exception) {
      showToast(exception.message, true, 6500);
    } finally {
      event.target.value = '';
      setBusy(false);
    }
  }

  async function clearMembers() {
    if (!confirm('Remove the imported member directory and all suggested matches? Trophy winners will not be changed.')) return;
    try {
      await api('/api/members', { method: 'DELETE', body: '{}' });
      await refreshMemberSummary();
      if (state.current) await refreshCurrent();
      showToast('Member directory removed.');
    } catch (exception) {
      showToast(exception.message, true);
    }
  }

  function installTrophyPhotoManager() {
    const heading = document.querySelector('.detail-heading');
    if (!heading || document.querySelector('#trophy-photo-card')) return;
    const card = document.createElement('section');
    card.id = 'trophy-photo-card';
    card.className = 'trophy-photo-card';
    card.innerHTML = `
      <div class="trophy-photo-copy">
        <span class="trophy-photo-icon" aria-hidden="true">▣</span>
        <span><strong>Trophy reference photos</strong><small>Whole-trophy angles used only for the catalogue illustration. The engraving reader never sees these images.</small></span>
      </div>
      <div class="trophy-photo-actions">
        <span id="trophy-photo-count">0 reference photos</span>
        <label>Add reference photos<input id="trophy-photo-input" type="file" accept="image/jpeg,image/png,image/webp" multiple hidden></label>
      </div>
      <div id="trophy-photo-strip" class="trophy-photo-strip" aria-label="Trophy reference photographs"></div>`;
    heading.after(card);
    card.querySelector('#trophy-photo-input').addEventListener('change', uploadTrophyPhotos);
    card.querySelector('#trophy-photo-strip').addEventListener('click', event => {
      const remove = event.target.closest('[data-trophy-photo-id]');
      if (remove) deleteTrophyPhoto(remove.dataset.trophyPhotoId);
    });
    const observer = new MutationObserver(renderTrophyPhotos);
    observer.observe(document.querySelector('#detail-title'), { childList: true, subtree: true });
    renderTrophyPhotos();
  }

  function renderTrophyPhotos() {
    const strip = document.querySelector('#trophy-photo-strip');
    const count = document.querySelector('#trophy-photo-count');
    if (!strip || !count) return;
    const photos = state.current?.trophyPhotos || [];
    count.textContent = plural(photos.length, 'reference photo');
    strip.innerHTML = photos.length
      ? photos.map((photo, index) => `
          <span class="trophy-reference-photo">
            <img src="${escapeHtml(photo.url)}" alt="Trophy reference angle ${index + 1}">
            <button type="button" data-trophy-photo-id="${escapeHtml(photo.id)}" aria-label="Remove trophy reference angle ${index + 1}">×</button>
            <small>${index === 0 ? 'Main view' : `Angle ${index + 1}`}</small>
          </span>`).join('')
      : '<span class="trophy-photo-empty">No reference photos yet. Engraving evidence remains separate below.</span>';
    strip.querySelectorAll('img').forEach(addImageFallback);
    updateIllustrationControl();
  }

  async function uploadTrophyPhotos(event) {
    const files = [...(event.target.files || [])];
    event.target.value = '';
    if (!files.length || !state.current) return;
    const id = state.current.id;
    setBusy(true, `Preparing ${plural(files.length, 'reference photo')}…`, 'These images will be stored separately and used only for the trophy illustration.');
    try {
      const prepared = [];
      for (let index = 0; index < files.length; index += 1) {
        setBusy(true, `Preparing reference photo ${index + 1} of ${files.length}…`, 'Keeping the full trophy clear while reducing the upload size.');
        prepared.push(await optimiseImage(files[index]));
      }
      const form = new FormData();
      prepared.forEach(file => form.append('files', file, file.name));
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/trophy-photos`, { method: 'POST', body: form });
      if (state.current?.id !== id) return;
      state.current = data.trophy;
      renderTrophyPhotos();
      showToast(`${plural(prepared.length, 'reference photo')} saved separately from engraving evidence.`);
    } catch (exception) {
      showToast(exception.message, true, 6500);
    } finally {
      setBusy(false);
    }
  }

  async function deleteTrophyPhoto(photoId) {
    if (!state.current || !confirm('Remove this trophy reference photo? Engraving evidence will not be affected.')) return;
    const id = state.current.id;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/trophy-photos/${encodeURIComponent(photoId)}`, { method: 'DELETE', body: '{}' });
      if (state.current?.id !== id) return;
      state.current = data.trophy;
      renderTrophyPhotos();
      showToast('Trophy reference photo removed.');
    } catch (exception) {
      showToast(exception.message, true);
    }
  }

  function installIllustrationControl() {
    const heading = document.querySelector('.detail-heading');
    if (!heading || document.querySelector('#generate-illustration-button')) return;
    const button = document.createElement('button');
    button.id = 'generate-illustration-button';
    button.className = 'generate-illustration-button';
    button.type = 'button';
    button.innerHTML = '<span aria-hidden="true">✦</span><span><strong>Create illustration</strong><small>Use reference photos only</small></span>';
    heading.append(button);
    button.addEventListener('click', generateIllustration);

    const observer = new MutationObserver(updateIllustrationControl);
    observer.observe(document.querySelector('#detail-title'), { childList: true, subtree: true });
    updateIllustrationControl();
  }

  function updateIllustrationControl() {
    const button = document.querySelector('#generate-illustration-button');
    if (!button) return;
    const trophy = state.current;
    const photoCount = trophy?.trophyPhotos?.length ?? 0;
    button.disabled = !trophy || photoCount === 0 || !commercial.illustrationConfigured || trophy?.illustrationState === 'processing';
    const title = button.querySelector('strong');
    const copy = button.querySelector('small');
    if (trophy?.illustrationState === 'processing') {
      title.textContent = 'Illustration generating';
      copy.textContent = 'You can keep working';
    } else if (!commercial.illustrationConfigured) {
      title.textContent = 'Illustration unavailable';
      copy.textContent = 'Connect the image model';
    } else if (photoCount === 0) {
      title.textContent = 'Create illustration';
      copy.textContent = 'Add reference photos first';
    } else if (trophy.illustrationGenerationCount > 0) {
      title.textContent = 'Regenerate illustration';
      copy.textContent = `Use ${plural(Math.min(photoCount, 4), 'reference angle')}`;
    } else {
      title.textContent = 'Create illustration';
      copy.textContent = `Use ${plural(Math.min(photoCount, 4), 'reference angle')}`;
    }
  }

  async function generateIllustration() {
    if (!state.current) return;
    const id = state.current.id;
    const button = document.querySelector('#generate-illustration-button');
    button.disabled = true;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/illustration/background`, { method: 'POST', body: '{}' });
      if (state.current?.id !== id) return;
      state.current = data.trophy;
      renderDetail();
      renderTrophyPhotos();
      showToast('Illustration queued. You can keep working while it is generated.');
      watchIllustration(id);
    } catch (exception) {
      showToast(exception.message, true, 7000);
      updateIllustrationControl();
    }
  }

  async function watchIllustration(id) {
    for (let attempt = 0; attempt < 100; attempt += 1) {
      await new Promise(resolve => window.setTimeout(resolve, 3000));
      try {
        const data = await api(`/api/trophies/${encodeURIComponent(id)}/illustration/status`);
        if (state.current?.id === id) {
          state.current = data.trophy;
          renderDetail();
          renderTrophyPhotos();
        }
        if (data.trophy.illustrationState === 'complete') {
          await loadCatalogue();
          showToast('The catalogue illustration is ready.');
          return;
        }
        if (data.trophy.illustrationState === 'failed') {
          showToast(data.trophy.illustrationMessage || 'The illustration could not be completed. Your reference photos are safe.', true, 7000);
          return;
        }
      } catch {
        return;
      }
    }
  }
  function installMatchEnhancer() {
    const list = document.querySelector('#winner-list');
    if (!list) return;
    const observer = new MutationObserver(enhanceMatches);
    observer.observe(list, { childList: true, subtree: true });
    list.addEventListener('click', event => {
      const remove = event.target.closest('[data-remove-member-match]');
      if (remove) removeMemberMatch(remove.dataset.removeMemberMatch, remove);
    });
    enhanceMatches();
  }

  function enhanceMatches() {
    const winners = state.current?.winners || [];
    for (const winner of winners) {
      if (!winner.memberMatch) continue;
      const row = document.querySelector(`#winner-list [data-winner-id="${cssEscape(winner.id)}"]`);
      const nameLabel = row?.querySelector('.winner-name');
      if (!nameLabel || nameLabel.querySelector('.member-match')) continue;
      const match = winner.memberMatch;
      const age = match.birthYear ? winner.year - match.birthYear : null;
      const memberNumber = match.membershipNumber ? `Membership number ${match.membershipNumber}. ` : '';
      const badge = document.createElement('span');
      badge.className = `member-match is-${match.state}`;
      badge.title = `${memberNumber}${match.explanation}`.trim();
      badge.innerHTML = `<b>${match.state === 'strong' ? 'Likely member' : 'Possible member'}</b><span>${escapeHtml(match.memberName)}${match.birthYear ? ` · born ${match.birthYear}` : ''}${age !== null ? ` · age ${age} in ${winner.year}` : ''}</span><em>${Math.round(match.confidence * 100)}%</em>`;
      nameLabel.append(badge);

      const actions = row.querySelector('.winner-actions');
      if (actions && !actions.querySelector('[data-remove-member-match]')) {
        const remove = document.createElement('button');
        remove.className = 'remove-member-match';
        remove.type = 'button';
        remove.dataset.removeMemberMatch = winner.id;
        remove.textContent = 'Remove match';
        remove.title = match.membershipNumber
          ? `Remove membership match ${match.memberName} (${match.membershipNumber})`
          : `Remove membership match ${match.memberName}`;
        actions.prepend(remove);
      }
    }
  }

  async function removeMemberMatch(winnerId, button) {
    if (!state.current) return;
    const id = state.current.id;
    button.disabled = true;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/winners/${encodeURIComponent(winnerId)}/member-match`, { method: 'DELETE', body: '{}' });
      if (state.current?.id !== id) return;
      state.current = data.trophy;
      state.missingYears = data.missingYears || [];
      renderDetail();
      showToast('Member match removed. That member will not be suggested again for this winner.');
    } catch (exception) {
      button.disabled = false;
      showToast(exception.message, true);
    }
  }
  function cssEscape(value) {
    return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');
  }
})();
