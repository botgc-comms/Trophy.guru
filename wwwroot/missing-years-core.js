(() => {
  if (!document.querySelector('link[href="/commercial.css"]')) {
    const stylesheet = document.createElement('link');
    stylesheet.rel = 'stylesheet';
    stylesheet.href = '/commercial.css';
    document.head.append(stylesheet);
  }
  if (!document.querySelector('script[src="/commercial.js"]')) {
    const commercialScript = document.createElement('script');
    commercialScript.src = '/commercial.js';
    document.head.append(commercialScript);
  }

  const box = document.querySelector('#missing-years');
  if (!box) return;

  if (!document.querySelector('link[href="/missing-years.css"]')) {
    const stylesheet = document.createElement('link');
    stylesheet.rel = 'stylesheet';
    stylesheet.href = '/missing-years.css';
    document.head.append(stylesheet);
  }

  function addManualControl() {
    const years = Array.isArray(state.missingYears) ? state.missingYears : [];
    if (box.hidden || !years.length) return;
    const signature = years.join(',');
    const existing = box.querySelector('.missing-year-actions');
    if (existing?.dataset.signature === signature) return;
    existing?.remove();

    const actions = document.createElement('div');
    actions.className = 'missing-year-actions';
    actions.dataset.signature = signature;
    actions.innerHTML = `
      <label>
        <span>Add a winner for</span>
        <select aria-label="Choose a missing year">
          ${years.map(year => `<option value="${year}">${year}</option>`).join('')}
        </select>
      </label>
      <button type="button">Add year</button>`;
    box.append(actions);
  }

  box.addEventListener('click', event => {
    const button = event.target.closest('.missing-year-actions button');
    if (!button) return;
    const year = box.querySelector('.missing-year-actions select')?.value;
    if (!year) return;
    renderWinners(true);
    const row = document.querySelector('#winner-list [data-winner-id="new"]');
    const yearInput = row?.querySelector('input[name="year"]');
    const nameInput = row?.querySelector('input[name="name"]');
    if (!yearInput || !nameInput) return;
    yearInput.value = year;
    nameInput.focus();
    row.scrollIntoView({ behavior: 'smooth', block: 'center' });
  });

  const observer = new MutationObserver(addManualControl);
  observer.observe(box, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden'] });
  queueMicrotask(addManualControl);
})();
