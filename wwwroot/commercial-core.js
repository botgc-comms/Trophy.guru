(() => {
  const commercial = {
    illustrationConfigured: false,
    memberDirectory: null,
    activeWinnerId: null,
  };

  installBalanceIndicator();
  installNewTrophyFlow();
  installMemberDirectory();
  installTrophyPhotoManager();
  installTrophyDivision();
  installMatchSelector();
  refreshCapabilities();

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
    const settingsValue = document.querySelector('#settings-credit-balance');
    const link = document.querySelector('#credit-balance');
    const label = balance?.unlimited
      ? 'Unlimited'
      : `${Number(balance?.trophyCredits || 0)} ${Number(balance?.trophyCredits || 0) === 1 ? 'credit' : 'credits'}`;
    if (value) value.textContent = label;
    if (settingsValue) settingsValue.textContent = label;
    if (link) link.setAttribute('aria-label', `Trophy credit balance: ${label}. View plans.`);
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
        <p>Create the trophy record, then add whole-trophy photographs for its catalogue illustration.</p>
        <label><span>Trophy name</span><input name="name" maxlength="160" required placeholder="e.g. Ladies Challenge Cup"></label>
        <div class="commercial-form-grid">
          <label><span>Category</span><input name="category" maxlength="80" required placeholder="e.g. Golf, Rugby, Cricket"></label>
          <label><span>Trophy type <em>optional</em></span><select name="division"><option value="mixed">Mixed or open</option><option value="gents">Gents</option><option value="ladies">Ladies</option><option value="junior">Junior</option></select></label>
        </div>
        <label><span>Alternative name <em>optional</em></span><input name="secondaryName" maxlength="160" placeholder="Name engraved on the base"></label>
        <label><span>Reference code <em>optional</em></span><input name="code" maxlength="24" placeholder="Auto-generated"></label>
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
          division: values.get('division') || 'mixed',
        }),
      });
      document.querySelector('#new-trophy-dialog').close();
      form.reset();
      await loadCatalogue();
      await openTrophy(data.trophy.id);
      showToast('Trophy created. Add its reference photographs next.');
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
      <label class="member-upload-button" title="Birth and joining dates are reduced to year only, and the original file is not retained.">
        <span class="member-upload-icon" aria-hidden="true">CSV</span>
        <span><strong id="member-upload-title">Upload member information</strong><small id="member-directory-summary">CSV, XML or Excel · include birth and joining dates</small></span>
        <input id="member-file-input" type="file" accept=".csv,.tsv,.xml,.xlsx,text/csv,text/xml,application/xml,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" hidden>
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
      renderTrophyPhotos();
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
        const joinedCopy = data.directory.withJoinYearCount ? ` · ${data.directory.withJoinYearCount} with joining year` : '';
        const genderCopy = data.directory.withGenderCount ? ` · ${data.directory.withGenderCount} with gender` : '';
        summary.textContent = `${data.directory.memberCount} members loaded${joinedCopy}${genderCopy}`;
        control.classList.add('has-data');
        clear.hidden = false;
      } else {
        title.textContent = 'Upload member information';
        summary.textContent = 'CSV, XML or Excel · include birth and joining dates';
        control.classList.remove('has-data');
        clear.hidden = true;
      }
      enhanceMatches();
    } catch { }
  }

  async function importMembers(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file, file.name);
    setBusy(true, 'Importing member directory…', 'Normalising names, gender, birth dates and joining dates for trophy matching.');
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
    const dialog = document.querySelector('#trophy-photos-dialog');
    const button = document.querySelector('#trophy-photo-button');
    if (!dialog || !button) return;
    button.addEventListener('click', () => {
      renderTrophyPhotos();
      dialog.showModal();
    });
    dialog.querySelector('.commercial-dialog-close').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
    dialog.querySelector('#reference-camera-input').addEventListener('change', uploadTrophyPhotos);
    dialog.querySelector('#trophy-photo-input').addEventListener('change', uploadTrophyPhotos);
    dialog.querySelector('#trophy-photo-strip').addEventListener('click', event => {
      const remove = event.target.closest('[data-trophy-photo-id]');
      if (remove) deleteTrophyPhoto(remove.dataset.trophyPhotoId);
    });
    const observer = new MutationObserver(renderTrophyPhotos);
    observer.observe(document.querySelector('#detail-title'), { childList: true, subtree: true });
  }

  function renderTrophyPhotos() {
    const strip = document.querySelector('#trophy-photo-strip');
    const status = document.querySelector('#reference-illustration-status');
    if (!strip || !status) return;
    const trophy = state.current;
    const photos = trophy?.trophyPhotos || [];
    strip.innerHTML = photos.length
      ? photos.map((photo, index) => `
          <span class="trophy-reference-photo">
            <img src="${escapeHtml(photo.url)}" alt="Trophy reference angle ${index + 1}">
            <button type="button" data-trophy-photo-id="${escapeHtml(photo.id)}" aria-label="Remove trophy reference angle ${index + 1}">×</button>
            <small>${index === 0 ? 'Main view' : `Angle ${index + 1}`}</small>
          </span>`).join('')
      : '<span class="trophy-photo-empty">No reference photos yet. Add whole-trophy views here; winner-record evidence remains separate.</span>';
    strip.querySelectorAll('img').forEach(addImageFallback);
    if (!trophy) status.textContent = '';
    else if (trophy.illustrationState === 'processing') status.textContent = '✦ The illustration is generating in the background. You can close this window and keep working.';
    else if (trophy.illustrationState === 'failed') status.textContent = trophy.illustrationMessage || 'The illustration needs another set of reference photos.';
    else if (!commercial.illustrationConfigured) status.textContent = 'Reference photos will be saved. Illustration generation is not configured on this service.';
    else if (photos.length) status.textContent = 'Uploading another reference photo will automatically refresh the illustration.';
    else status.textContent = 'The illustration will generate automatically after you add a reference photo.';
  }

  async function uploadTrophyPhotos(event) {
    const files = [...(event.target.files || [])];
    event.target.value = '';
    if (!files.length || !state.current) return;
    const id = state.current.id;
    setBusy(true, `Preparing ${plural(files.length, 'reference photo')}…`, 'These images stay separate from winner-record evidence.');
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
      renderDetail();
      renderTrophyPhotos();
      if (commercial.illustrationConfigured) {
        const queued = await api(`/api/trophies/${encodeURIComponent(id)}/illustration/background`, { method: 'POST', body: '{}' });
        if (state.current?.id === id) state.current = queued.trophy;
        renderDetail();
        renderTrophyPhotos();
        watchIllustration(id);
        showToast('Reference photos saved. The illustration is refreshing in the background.');
      } else {
        showToast('Reference photos saved separately from winner-record evidence.');
      }
    } catch (exception) {
      showToast(exception.message, true, 6500);
    } finally {
      setBusy(false);
    }
  }

  async function deleteTrophyPhoto(photoId) {
    if (!state.current || !confirm('Remove this trophy reference photo? Winner-record evidence will not be affected.')) return;
    const id = state.current.id;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/trophy-photos/${encodeURIComponent(photoId)}`, { method: 'DELETE', body: '{}' });
      if (state.current?.id !== id) return;
      state.current = data.trophy;
      renderDetail();
      renderTrophyPhotos();
      showToast('Trophy reference photo removed.');
    } catch (exception) {
      showToast(exception.message, true);
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
      } catch { return; }
    }
  }

  function installTrophyDivision() {
    document.querySelector('#detail-division')?.addEventListener('change', async event => {
      if (!state.current) return;
      const id = state.current.id;
      event.target.disabled = true;
      try {
        const data = await api(`/api/trophies/${encodeURIComponent(id)}/division`, {
          method: 'PUT',
          body: JSON.stringify({ division: event.target.value }),
        });
        if (state.current?.id === id) {
          state.current = data.trophy;
          state.missingYears = data.missingYears || [];
          renderDetail();
        }
        showToast('Trophy type saved and member matches recalculated.');
      } catch (exception) {
        showToast(exception.message, true);
      } finally {
        event.target.disabled = false;
      }
    });
  }

  function installMatchSelector() {
    const list = document.querySelector('#winner-list');
    const dialog = document.querySelector('#member-match-dialog');
    if (!list || !dialog) return;
    const observer = new MutationObserver(enhanceMatches);
    observer.observe(list, { childList: true, subtree: true });
    list.addEventListener('click', event => {
      const match = event.target.closest('[data-open-member-match]');
      if (!match) return;
      event.preventDefault();
      event.stopPropagation();
      openMemberCandidates(match.dataset.openMemberMatch);
    });
    dialog.querySelector('.commercial-dialog-close').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
    dialog.querySelector('#member-candidate-list').addEventListener('click', event => {
      const candidate = event.target.closest('[data-member-id]');
      if (candidate) selectMemberCandidate(candidate.dataset.memberId, candidate);
    });
    dialog.querySelector('#show-manual-member-form').addEventListener('click', showManualMemberForm);
    dialog.querySelector('#cancel-manual-member').addEventListener('click', hideManualMemberForm);
    dialog.querySelector('#manual-member-form').addEventListener('submit', addManualMember);
    dialog.querySelector('#remove-current-member-match').addEventListener('click', removeCurrentMemberMatch);
    enhanceMatches();
  }

  function enhanceMatches() {
    const winners = state.current?.winners || [];
    for (const winner of winners) {
      const row = document.querySelector(`#winner-list [data-winner-id="${cssEscape(winner.id)}"]`);
      const nameLabel = row?.querySelector('.winner-name');
      if (!nameLabel || nameLabel.querySelector('.member-match')) continue;
      const match = winner.memberMatch;
      const badge = document.createElement('button');
      badge.type = 'button';
      badge.dataset.openMemberMatch = winner.id;

      if (!match) {
        if (winner.keepMemberUnmatched) {
          badge.className = 'member-match is-unmatched is-deliberately-unmatched';
          badge.title = 'This winner has deliberately been left unlinked, for example because they were not a club member. Click to attach a member if needed.';
          badge.innerHTML = '<b>Not linked to a member</b><span>Kept unmatched by your choice</span><em>Change ›</em>';
        } else {
          badge.className = 'member-match is-unmatched';
          badge.title = 'No member is currently attached. Click to choose an existing member or add one manually.';
          badge.innerHTML = '<b>No member attached</b><span>Choose an existing member or add one manually</span><em>Choose ›</em>';
        }
        nameLabel.append(badge);
        continue;
      }

      const age = match.birthYear ? winner.year - match.birthYear : null;
      const memberNumber = match.membershipNumber ? `Membership number ${match.membershipNumber}. ` : '';
      badge.className = `member-match is-${match.state}`;
      badge.title = `${memberNumber}${match.explanation} Click to see every possible match.`.trim();
      const label = match.manuallySelected ? 'Selected member' : match.state === 'strong' ? 'Likely member' : 'Possible member';
      badge.innerHTML = `<b>${label}</b><span>${escapeHtml(match.memberName)}${match.birthYear ? ` · born ${match.birthYear}` : ''}${age !== null ? ` · age ${age} in ${winner.year}` : ''}${match.joinYear ? ` · joined ${match.joinYear}` : ''}</span><em>${Math.round(match.confidence * 100)}% ›</em>`;
      nameLabel.append(badge);
    }
  }
  async function openMemberCandidates(winnerId) {
    if (!state.current) return;
    commercial.activeWinnerId = winnerId;
    const winner = state.current.winners.find(item => item.id === winnerId);
    const dialog = document.querySelector('#member-match-dialog');
    const list = dialog.querySelector('#member-candidate-list');
    document.querySelector('#member-match-title').textContent = `Match ${winner?.name || 'winner'}`;
    document.querySelector('#member-match-copy').textContent = winner?.keepMemberUnmatched
      ? `This ${winner.year} winner is deliberately not linked to a member. Choose a record below only if you want to change that.`
      : winner
      ? `Choose the membership record for the ${winner.year} winner. Age is shown at the time of the award.`
      : 'Choose the correct membership record.';
    document.querySelector('#remove-current-member-match').hidden = !winner?.memberMatch;
    prepareManualMemberForm(winner);
    list.innerHTML = '<p class="candidate-loading">Loading possible matches…</p>';
    dialog.showModal();
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(state.current.id)}/winners/${encodeURIComponent(winnerId)}/member-candidates`);
      if (commercial.activeWinnerId !== winnerId) return;
      renderMemberCandidates(data.candidates || [], winner);
    } catch (exception) {
      list.innerHTML = `<p class="candidate-empty">${escapeHtml(exception.message)}</p>`;
    }
  }

  function renderMemberCandidates(candidates, winner) {
    const list = document.querySelector('#member-candidate-list');
    const currentId = winner?.memberMatch?.memberId;
    list.innerHTML = candidates.length ? candidates.map(candidate => {
      const age = candidate.birthYear ? winner.year - candidate.birthYear : null;
      const gender = candidate.gender && candidate.gender !== 'unknown' ? capitalize(candidate.gender) : 'Gender not supplied';
      const details = [candidate.membershipNumber ? `Member ${candidate.membershipNumber}` : 'No member number', candidate.birthYear ? `Born ${candidate.birthYear}` : 'Birth year not supplied', age !== null ? `Age ${age} in ${winner.year}` : null, candidate.joinYear ? `Joined ${candidate.joinYear}` : 'Joining year not supplied', gender].filter(Boolean).join(' · ');
      return `<button class="member-candidate ${candidate.memberId === currentId ? 'is-current' : ''}" type="button" data-member-id="${escapeHtml(candidate.memberId)}"><span><strong>${escapeHtml(candidate.memberName)}</strong><small>${escapeHtml(details)}</small><em>${escapeHtml(candidate.explanation)}</em></span><b>${candidate.memberId === currentId ? 'Current' : `${Math.round(candidate.confidence * 100)}%`}</b></button>`;
    }).join('') : '<p class="candidate-empty">No plausible records were found. Add this member manually below, or leave the winner unmatched.</p>';
  }

  function prepareManualMemberForm(winner) {
    const form = document.querySelector('#manual-member-form');
    const toggle = document.querySelector('#show-manual-member-form');
    form.reset();
    form.hidden = true;
    toggle.hidden = false;
    form.elements.fullName.value = winner?.name || '';
    form.elements.gender.value = state.current?.division === 'ladies' ? 'female' : state.current?.division === 'gents' ? 'male' : 'unknown';
    document.querySelector('#manual-member-error').hidden = true;
  }

  function showManualMemberForm() {
    const form = document.querySelector('#manual-member-form');
    document.querySelector('#show-manual-member-form').hidden = true;
    form.hidden = false;
    form.elements.fullName.focus();
    form.elements.fullName.select();
  }

  function hideManualMemberForm() {
    const winner = state.current?.winners.find(item => item.id === commercial.activeWinnerId);
    prepareManualMemberForm(winner);
  }

  async function addManualMember(event) {
    event.preventDefault();
    if (!state.current || !commercial.activeWinnerId) return;
    const form = event.currentTarget;
    const submit = form.querySelector('[type="submit"]');
    const error = document.querySelector('#manual-member-error');
    const values = new FormData(form);
    const id = state.current.id;
    submit.disabled = true;
    error.hidden = true;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/winners/${encodeURIComponent(commercial.activeWinnerId)}/member-match/manual`, {
        method: 'POST',
        body: JSON.stringify({
          fullName: values.get('fullName'),
          dateOfBirth: values.get('dateOfBirth') || null,
          dateJoined: values.get('dateJoined') || null,
          membershipNumber: values.get('membershipNumber') || null,
          gender: values.get('gender') || 'unknown',
        }),
      });
      if (state.current?.id === id) {
        state.current = data.trophy;
        state.missingYears = data.missingYears || [];
        commercial.memberDirectory = data.directory;
        renderDetail();
      }
      await refreshMemberSummary();
      document.querySelector('#member-match-dialog').close();
      showToast('Member saved to the club directory and attached to this winner.');
    } catch (exception) {
      error.textContent = exception.message;
      error.hidden = false;
    } finally {
      submit.disabled = false;
    }
  }

  async function selectMemberCandidate(memberId, button) {
    if (!state.current || !commercial.activeWinnerId) return;
    const id = state.current.id;
    button.disabled = true;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/winners/${encodeURIComponent(commercial.activeWinnerId)}/member-match`, {
        method: 'PUT',
        body: JSON.stringify({ memberId }),
      });
      if (state.current?.id === id) {
        state.current = data.trophy;
        state.missingYears = data.missingYears || [];
        renderDetail();
      }
      document.querySelector('#member-match-dialog').close();
      showToast('Member match updated. Your selection will be kept during later rematches.');
    } catch (exception) {
      button.disabled = false;
      showToast(exception.message, true);
    }
  }

  async function removeCurrentMemberMatch() {
    if (!state.current || !commercial.activeWinnerId) return;
    const id = state.current.id;
    try {
      const data = await api(`/api/trophies/${encodeURIComponent(id)}/winners/${encodeURIComponent(commercial.activeWinnerId)}/member-match`, { method: 'DELETE', body: '{}' });
      if (state.current?.id === id) {
        state.current = data.trophy;
        state.missingYears = data.missingYears || [];
        renderDetail();
      }
      document.querySelector('#member-match-dialog').close();
      showToast('Winner left unmatched. Automatic matching will not add the member back.');
    } catch (exception) {
      showToast(exception.message, true);
    }
  }

  function cssEscape(value) {
    return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');
  }
})();
