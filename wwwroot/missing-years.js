(() => {
  window.addEventListener('trophy-app-ready', () => {
    installEmptyCatalogueState();
    const script = document.createElement('script');
    script.src = '/missing-years-core.js';
    document.head.append(script);
  }, { once: true });

  function installEmptyCatalogueState() {
    const stylesheet = document.createElement('link');
    stylesheet.rel = 'stylesheet';
    stylesheet.href = '/empty-state.css';
    document.head.append(stylesheet);

    const originalRenderTrophies = renderTrophies;
    renderTrophies = function renderTrophiesWithFirstRun() {
      if (state.trophies.length > 0) {
        originalRenderTrophies();
        return;
      }

      elements.grid.innerHTML = `
        <section class="first-trophy-empty">
          <div class="empty-trophy-mark" aria-hidden="true">
            <svg viewBox="0 0 64 64"><path d="M22 12h20l-3 22c-.8 6-3.6 10-7 13-3.4-3-6.2-7-7-13l-3-22Z"/><path d="M21 17h-9c1 11 5 17 14 20M43 17h9c-1 11-5 17-14 20M32 47v7M23 55h18"/></svg>
          </div>
          <p class="eyebrow">Your collection is empty</p>
          <h2>Add your first trophy</h2>
          <p class="empty-intro">Enter the trophy details and add one or more whole-trophy reference photographs. Engraving close-ups are added separately afterwards.</p>
          <button id="empty-add-trophy-button" type="button"><span aria-hidden="true">+</span>Add your first trophy</button>
          <ol aria-label="What happens when you add a trophy">
            <li><span>1</span><p><strong>Name the trophy</strong><small>Add its category and reference code</small></p></li>
            <li><span>2</span><p><strong>Take reference photos</strong><small>Whole-trophy angles for the illustration</small></p></li>
            <li><span>3</span><p><strong>Add engraving evidence</strong><small>Upload close-ups separately for the reader</small></p></li>
          </ol>
        </section>`;

      document.querySelector('#empty-add-trophy-button')?.addEventListener('click', openAddTrophyWizard);
    };

    if (state.trophies.length === 0) renderTrophies();
  }

  function openAddTrophyWizard() {
    const addButton = document.querySelector('#new-trophy-button');
    if (addButton) {
      addButton.click();
      return;
    }

    showToast('Opening the trophy form…');
    window.setTimeout(() => {
      const loadedButton = document.querySelector('#new-trophy-button');
      if (loadedButton) loadedButton.click();
      else showToast('The trophy form could not be opened. Refresh the page and try again.', true);
    }, 500);
  }
})();
