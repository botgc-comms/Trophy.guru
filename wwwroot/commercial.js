(() => {
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = '/wizard.css';
  document.head.append(stylesheet);

  const core = document.createElement('script');
  core.src = '/commercial-core.js';
  core.onload = installPhotoFirstWizard;
  document.head.append(core);

  function installPhotoFirstWizard() {
    const oldButton = document.querySelector('#new-trophy-button');
    const oldDialog = document.querySelector('#new-trophy-dialog');
    if (!oldButton || !oldDialog) return;
    oldDialog.remove();

    const button = oldButton.cloneNode(false);
    button.innerHTML = '<span aria-hidden="true">+</span><span><strong>Add trophy</strong><small>Photos become its illustration</small></span>';
    oldButton.replaceWith(button);

    const dialog = document.createElement('dialog');
    dialog.id = 'new-trophy-dialog';
    dialog.className = 'commercial-dialog trophy-wizard-dialog';
    dialog.innerHTML = `
      <form id="new-trophy-form">
        <button class="commercial-dialog-close" type="button" aria-label="Close">×</button>
        <div class="wizard-step"><span>1</span><i></i><span>2</span><i></i><span>3</span></div>
        <p class="step-label">New trophy · details, photographs, illustration</p>
        <h2>Add a trophy</h2>
        <p>Give it a name, then take or choose one or more photographs. These reference photographs are stored separately from engraving evidence and are used only to create the catalogue illustration.</p>
        <label><span>Trophy name</span><input name="name" maxlength="160" required placeholder="e.g. Ladies Challenge Cup"></label>
        <div class="commercial-form-grid">
          <label><span>Category</span><input name="category" maxlength="80" required placeholder="e.g. Golf, Rugby, Cricket"></label>
          <label><span>Reference code <em>optional</em></span><input name="code" maxlength="24" placeholder="Auto-generated"></label>
        </div>
        <label><span>Alternative name <em>optional</em></span><input name="secondaryName" maxlength="160" placeholder="Name engraved on the base"></label>
        <fieldset class="wizard-photos">
          <legend>Trophy reference photographs <b>required</b></legend>
          <p>Use a clear full-trophy view first. Extra angles help reproduce handles, lids, bases and fine details.</p>
          <div class="wizard-photo-actions">
            <label class="wizard-camera"><span>Take a photo</span><input id="wizard-camera-input" type="file" accept="image/jpeg,image/png,image/webp" capture="environment" hidden></label>
            <label class="wizard-library"><span>Choose photos</span><input id="wizard-library-input" type="file" accept="image/jpeg,image/png,image/webp" multiple hidden></label>
          </div>
          <div id="wizard-photo-list" class="wizard-photo-list"><span class="wizard-photo-empty">No photographs added yet</span></div>
        </fieldset>
        <div class="wizard-outcome"><span>✦</span><p><strong>What happens next</strong><small>We create the trophy, save these reference angles separately and generate its transparent catalogue illustration. Add close-up engraving evidence afterwards.</small></p></div>
        <button class="commercial-submit" type="submit" disabled>Create trophy</button>
        <p class="commercial-form-error" role="alert" hidden></p>
      </form>`;
    document.body.append(dialog);

    let photographs = [];
    const form = dialog.querySelector('form');
    const camera = dialog.querySelector('#wizard-camera-input');
    const library = dialog.querySelector('#wizard-library-input');
    const list = dialog.querySelector('#wizard-photo-list');
    const submit = form.querySelector('[type="submit"]');

    button.addEventListener('click', () => dialog.showModal());
    dialog.querySelector('.commercial-dialog-close').addEventListener('click', closeWizard);
    dialog.addEventListener('click', event => { if (event.target === dialog) closeWizard(); });
    camera.addEventListener('change', event => addPhotographs([...event.target.files]));
    library.addEventListener('change', event => addPhotographs([...event.target.files]));
    list.addEventListener('click', event => {
      const remove = event.target.closest('[data-photo-index]');
      if (!remove) return;
      photographs.splice(Number(remove.dataset.photoIndex), 1);
      renderPhotographs();
    });
    form.addEventListener('input', updateSubmit);
    form.addEventListener('submit', createTrophyFromPhotos);

    function addPhotographs(files) {
      const accepted = files.filter(file => ['image/jpeg', 'image/png', 'image/webp'].includes(file.type));
      photographs = [...photographs, ...accepted].slice(0, 12);
      camera.value = '';
      library.value = '';
      renderPhotographs();
    }

    function renderPhotographs() {
      if (!photographs.length) {
        list.innerHTML = '<span class="wizard-photo-empty">No photographs added yet</span>';
        updateSubmit();
        return;
      }
      list.innerHTML = photographs.map((file, index) => {
        const objectUrl = URL.createObjectURL(file);
        return `<span class="wizard-photo"><img src="${objectUrl}" alt="Trophy photograph ${index + 1}"><button type="button" data-photo-index="${index}" aria-label="Remove photograph ${index + 1}">×</button><small>${index === 0 ? 'Main view' : `Angle ${index + 1}`}</small></span>`;
      }).join('');
      list.querySelectorAll('img').forEach(image => image.addEventListener('load', () => URL.revokeObjectURL(image.src), { once: true }));
      updateSubmit();
    }

    function updateSubmit() {
      submit.disabled = photographs.length === 0 || !form.querySelector('[name="name"]').value.trim() || !form.querySelector('[name="category"]').value.trim();
    }

    function closeWizard() {
      dialog.close();
      form.reset();
      photographs = [];
      renderPhotographs();
      form.querySelector('.commercial-form-error').hidden = true;
    }

    async function createTrophyFromPhotos(event) {
      event.preventDefault();
      const error = form.querySelector('.commercial-form-error');
      const values = new FormData(form);
      const sourcePhotos = [...photographs];
      let createdId = null;
      error.hidden = true;
      submit.disabled = true;
      try {
        setBusy(true, 'Preparing the trophy photographs…', `Optimising ${plural(sourcePhotos.length, 'angle')} for a reliable mobile upload.`);
        const prepared = [];
        for (let index = 0; index < sourcePhotos.length; index += 1) {
          setBusy(true, `Preparing photograph ${index + 1} of ${sourcePhotos.length}…`, 'Keeping enough detail to reproduce the trophy faithfully.');
          prepared.push(await optimiseImage(sourcePhotos[index]));
        }

        const created = await api('/api/trophies', {
          method: 'POST',
          body: JSON.stringify({
            name: values.get('name'),
            secondaryName: values.get('secondaryName') || null,
            category: values.get('category'),
            code: values.get('code') || null,
          }),
        });
        createdId = created.trophy.id;

        setBusy(true, `Uploading ${plural(prepared.length, 'photograph')}…`, 'Saving every angle to the club archive.');
        const upload = new FormData();
        prepared.forEach(file => upload.append('files', file, file.name));
       await api(`/api/trophies/${encodeURIComponent(createdId)}/trophy-photos`, { method: 'POST', body: upload });

        const auth = state.auth || await api('/api/auth/status');
        let illustrationQueued = false;
        if (auth.illustrationConfigured) {
          await api(`/api/trophies/${encodeURIComponent(createdId)}/illustration/background`, { method: 'POST', body: '{}' });
          illustrationQueued = true;
        }

        closeWizard();
        setBusy(false);
        await loadCatalogue();
        await openTrophy(createdId);
        showToast(illustrationQueued
          ? 'Trophy saved. Its illustration is generating in the background.'
          : 'Trophy and reference photographs saved. Connect the image model to create its illustration.',
          false,
          6000);
        if (illustrationQueued) watchIllustration(createdId);
      } catch (exception) {
        if (createdId) {
          closeWizard();
          await loadCatalogue();
          await openTrophy(createdId);
          showToast(`The trophy was saved, but the next step needs attention: ${exception.message}`, true, 7500);
        } else {
          error.textContent = exception.message;
          error.hidden = false;
        }
      } finally {
        setBusy(false);
        updateSubmit();
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
          }
          if (data.trophy.illustrationState === 'complete') {
            await loadCatalogue();
            showToast('The catalogue illustration is ready.');
            return;
          }
          if (data.trophy.illustrationState === 'failed') {
            showToast(data.trophy.illustrationMessage || 'The illustration could not be completed. Your photographs are safe.', true, 7000);
            return;
          }
        } catch {
          return;
        }
      }
    }
  }
})();
