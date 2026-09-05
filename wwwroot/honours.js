(() => {
  'use strict';

  const elements = {
    browser: document.querySelector('#browser-view'),
    browserEyebrow: document.querySelector('#browser-eyebrow'),
    browserTitle: document.querySelector('#browser-title'),
    clubContext: document.querySelector('#club-context'),
    clubLogo: document.querySelector('#club-logo'),
    clubMonogram: document.querySelector('#club-monogram'),
    detail: document.querySelector('#detail-view'),
    detailBack: document.querySelector('#detail-back'),
    detailContent: document.querySelector('#detail-content'),
    divisions: document.querySelector('#division-filters'),
    empty: document.querySelector('#honours-empty'),
    error: document.querySelector('#honours-error'),
    introduction: document.querySelector('#honours-introduction'),
    navigation: document.querySelector('#view-navigation'),
    results: document.querySelector('#honours-results'),
    search: document.querySelector('#honours-search'),
    share: document.querySelector('#share-board'),
    shareStatus: document.querySelector('#share-status'),
    summaryHonours: document.querySelector('#summary-winners'),
    summaryTrophies: document.querySelector('#summary-trophies'),
    summaryYears: document.querySelector('#summary-years'),
    title: document.querySelector('#honours-title'),
    years: document.querySelector('#year-navigation'),
  };

  const state = {
    clubId: readClubId(),
    data: null,
    division: 'all',
    search: '',
    view: 'year',
    year: null,
  };

  initialise();

  async function initialise() {
    bindEvents();
    if (!state.clubId) return showError();

    try {
      const response = await fetch(`/api/public/clubs/${encodeURIComponent(state.clubId)}/honours`, {
        credentials: 'omit',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error('Honours board unavailable');
      state.data = await response.json();
      state.year = state.data.summary.latestYear;
      applyClubIdentity();

      if (!state.data.trophies.length) {
        elements.browser.hidden = true;
        elements.navigation.hidden = true;
        elements.empty.hidden = false;
        return;
      }

      renderFromLocation();
    } catch {
      showError();
    }
  }

  function bindEvents() {
    elements.navigation.addEventListener('click', event => {
      const button = event.target.closest('[data-view]');
      if (!button) return;
      const view = button.dataset.view;
      location.hash = view === 'year' ? `year/${state.year || ''}` : view;
    });

    elements.divisions.addEventListener('click', event => {
      const button = event.target.closest('[data-division]');
      if (!button) return;
      state.division = button.dataset.division;
      elements.divisions.querySelectorAll('[data-division]').forEach(item => {
        item.setAttribute('aria-pressed', String(item === button));
      });
      renderBrowser();
    });

    elements.years.addEventListener('click', event => {
      const button = event.target.closest('[data-year]');
      if (!button) return;
      state.year = Number(button.dataset.year);
      location.hash = `year/${state.year}`;
    });

    elements.search.addEventListener('input', () => {
      state.search = normalise(elements.search.value);
      renderBrowser();
    });

    elements.detailBack.addEventListener('click', () => {
      location.hash = state.view === 'year' ? `year/${state.year}` : state.view;
    });

    elements.share.addEventListener('click', shareBoard);
    window.addEventListener('hashchange', renderFromLocation);
  }

  function readClubId() {
    const parts = location.pathname.split('/').filter(Boolean);
    return parts.length === 2 && parts[0].toLowerCase() === 'honours' ? decodeURIComponent(parts[1]) : '';
  }

  function applyClubIdentity() {
    const { club, summary } = state.data;
    const yearRange = summary.firstYear && summary.latestYear
      ? summary.firstYear === summary.latestYear ? `${summary.firstYear}` : `${summary.firstYear}–${summary.latestYear}`
      : 'Confirmed records';
    elements.clubContext.textContent = `${club.sport} · ${yearRange}`;
    elements.title.textContent = `${club.name} honours`;
    elements.introduction.textContent = `Explore ${club.name}'s confirmed trophy winners by year, trophy and person.`;
    elements.clubMonogram.textContent = club.name.trim().charAt(0).toUpperCase() || 'C';
    elements.clubLogo.src = club.logoUrl;
    elements.clubLogo.alt = `${club.name} logo`;
    elements.clubLogo.hidden = false;
    elements.clubLogo.addEventListener('error', () => {
      elements.clubLogo.hidden = true;
      elements.clubMonogram.hidden = false;
    }, { once: true });
    elements.clubMonogram.hidden = true;
    elements.summaryTrophies.textContent = formatNumber(summary.trophies);
    elements.summaryHonours.textContent = formatNumber(summary.honours);
    elements.summaryYears.textContent = formatNumber(summary.years);
    document.title = `${club.name} honours board`;
  }

  function renderFromLocation() {
    if (!state.data?.trophies.length) return;
    const [route = 'year', value] = location.hash.replace(/^#/, '').split('/');
    const view = ['year', 'trophy', 'person'].includes(route) ? route : 'year';
    state.view = view;

    if (view === 'year' && value) {
      const requestedYear = Number(value);
      if (allYears().includes(requestedYear)) state.year = requestedYear;
    }

    if ((view === 'trophy' || view === 'person') && value) {
      renderDetail(view, decodeURIComponent(value));
      return;
    }

    elements.browser.hidden = false;
    elements.detail.hidden = true;
    elements.navigation.querySelectorAll('[data-view]').forEach(button => {
      button.setAttribute('aria-pressed', String(button.dataset.view === view));
    });
    renderBrowser();
  }

  function renderBrowser() {
    if (state.view === 'year') renderByYear();
    if (state.view === 'trophy') renderByTrophy();
    if (state.view === 'person') renderByPerson();
    attachImageFallbacks(elements.results);
  }

  function renderByYear() {
    const years = allYears();
    if (!years.includes(state.year)) state.year = years[0];
    elements.browserEyebrow.textContent = 'Season archive';
    elements.browserTitle.textContent = `Winners in ${state.year}`;
    elements.search.placeholder = 'Search names or trophies';
    elements.years.hidden = false;
    elements.years.innerHTML = years.map(year => `
      <button type="button" data-year="${year}" aria-pressed="${year === state.year}">${year}</button>`).join('');

    const trophies = filteredTrophies().map(trophy => ({
      ...trophy,
      winners: trophy.winners.filter(winner => winner.year === state.year),
    })).filter(trophy => trophy.winners.length && matchesSearch(trophy));

    elements.results.className = 'honours-results year-results';
    elements.results.innerHTML = trophies.length
      ? trophies.map(trophy => `
        <article class="year-honour-card">
          ${trophyVisual(trophy)}
          <div class="year-honour-copy">
            <p>${escapeHtml(divisionLabel(trophy.division))}</p>
            <h3><a href="#trophy/${encodeURIComponent(trophy.id)}">${escapeHtml(trophy.name)}</a></h3>
            ${trophy.secondaryName ? `<small>${escapeHtml(trophy.secondaryName)}</small>` : ''}
            <ul>${trophy.winners.map(winner => `<li><a href="#person/${winner.personId}">${escapeHtml(winner.name)}</a></li>`).join('')}</ul>
            <a class="history-link" href="#trophy/${encodeURIComponent(trophy.id)}">View trophy history <span>→</span></a>
          </div>
        </article>`).join('')
      : noResults('No matching honours in this year');
  }

  function renderByTrophy() {
    elements.browserEyebrow.textContent = 'Trophy cabinet';
    elements.browserTitle.textContent = 'Winners by trophy';
    elements.search.placeholder = 'Search trophies or winners';
    elements.years.hidden = true;
    const trophies = filteredTrophies().filter(matchesSearch);
    elements.results.className = 'honours-results trophy-results';
    elements.results.innerHTML = trophies.length
      ? trophies.map(trophy => {
          const years = trophy.winners.map(winner => winner.year);
          const first = Math.min(...years);
          const latest = Math.max(...years);
          return `
            <article class="trophy-card">
              <a class="trophy-card-visual" href="#trophy/${encodeURIComponent(trophy.id)}">${trophyVisual(trophy)}</a>
              <div>
                <p>${escapeHtml(divisionLabel(trophy.division))}</p>
                <h3><a href="#trophy/${encodeURIComponent(trophy.id)}">${escapeHtml(trophy.name)}</a></h3>
                ${trophy.secondaryName ? `<small>${escapeHtml(trophy.secondaryName)}</small>` : ''}
                <dl><div><dt>${formatNumber(trophy.winners.length)}</dt><dd>Honours</dd></div><div><dt>${first === latest ? first : `${first}–${latest}`}</dt><dd>Recorded years</dd></div></dl>
              </div>
            </article>`;
        }).join('')
      : noResults('No trophies match that search');
  }

  function renderByPerson() {
    elements.browserEyebrow.textContent = 'Club roll of honour';
    elements.browserTitle.textContent = 'Trophies by person';
    elements.search.placeholder = 'Search winner names';
    elements.years.hidden = true;
    const people = buildPeople().filter(person => !state.search || normalise(person.name).includes(state.search));
    elements.results.className = 'honours-results person-results';
    elements.results.innerHTML = people.length
      ? people.map((person, index) => {
          const uniqueTrophies = new Set(person.honours.map(honour => honour.trophy.id)).size;
          return `
            <article class="person-card">
              <a href="#person/${person.id}" aria-label="View ${escapeAttribute(person.name)}'s honours">
                <span class="person-number">${String(index + 1).padStart(2, '0')}</span>
                <span class="person-name"><strong>${escapeHtml(person.name)}</strong><small>${formatNumber(person.honours.length)} honour${person.honours.length === 1 ? '' : 's'} · ${formatNumber(uniqueTrophies)} ${uniqueTrophies === 1 ? 'trophy' : 'trophies'}</small></span>
                <span class="person-years">${Math.min(...person.honours.map(item => item.year))}—${Math.max(...person.honours.map(item => item.year))}</span>
                <span class="person-arrow" aria-hidden="true">→</span>
              </a>
            </article>`;
        }).join('')
      : noResults('No winners match that search');
  }

  function renderDetail(type, id) {
    elements.browser.hidden = true;
    elements.detail.hidden = false;
    elements.detailBack.textContent = type === 'trophy' ? 'Back to trophies' : 'Back to winners';

    if (type === 'trophy') {
      const trophy = state.data.trophies.find(item => item.id === id);
      if (!trophy) return renderMissingDetail();
      const groups = Object.entries(groupBy(trophy.winners, winner => winner.year)).sort((a, b) => Number(b[0]) - Number(a[0]));
      elements.detailContent.innerHTML = `
        <article class="trophy-detail">
          <div class="detail-portrait">${trophyVisual(trophy)}</div>
          <div class="detail-heading">
            <p class="eyebrow">${escapeHtml(divisionLabel(trophy.division))} trophy</p>
            <h1>${escapeHtml(trophy.name)}</h1>
            ${trophy.secondaryName ? `<p>${escapeHtml(trophy.secondaryName)}</p>` : ''}
            <dl><div><dt>${formatNumber(trophy.winners.length)}</dt><dd>Confirmed honours</dd></div><div><dt>${groups.length}</dt><dd>Recorded years</dd></div></dl>
          </div>
          <div class="winner-timeline">
            <h2>Roll of honour</h2>
            <ol>${groups.map(([year, winners]) => `
              <li><time>${year}</time><div>${winners.map(winner => `<a href="#person/${winner.personId}">${escapeHtml(winner.name)}</a>`).join('')}</div></li>`).join('')}
            </ol>
          </div>
        </article>`;
      document.title = `${trophy.name} · ${state.data.club.name}`;
    } else {
      const person = buildPeople('all').find(item => item.id === id);
      if (!person) return renderMissingDetail();
      const honours = person.honours.sort((a, b) => b.year - a.year || a.trophy.name.localeCompare(b.trophy.name));
      elements.detailContent.innerHTML = `
        <article class="person-detail">
          <div class="person-detail-heading">
            <p class="eyebrow">Club roll of honour</p>
            <h1>${escapeHtml(person.name)}</h1>
            <p>${formatNumber(honours.length)} confirmed honour${honours.length === 1 ? '' : 's'} across ${formatNumber(new Set(honours.map(item => item.trophy.id)).size)} ${new Set(honours.map(item => item.trophy.id)).size === 1 ? 'trophy' : 'trophies'}.</p>
          </div>
          <ol class="person-honours-list">${honours.map(honour => `
            <li>
              <time>${honour.year}</time>
              <div>${trophyVisual(honour.trophy)}<span><small>${escapeHtml(divisionLabel(honour.trophy.division))}</small><strong><a href="#trophy/${encodeURIComponent(honour.trophy.id)}">${escapeHtml(honour.trophy.name)}</a></strong>${honour.trophy.secondaryName ? `<em>${escapeHtml(honour.trophy.secondaryName)}</em>` : ''}</span></div>
            </li>`).join('')}</ol>
        </article>`;
      document.title = `${person.name} · ${state.data.club.name}`;
    }
    attachImageFallbacks(elements.detailContent);
  }

  function renderMissingDetail() {
    elements.detailContent.innerHTML = noResults('That honour could not be found');
  }

  function filteredTrophies() {
    return state.data.trophies.filter(trophy => state.division === 'all' || trophy.division === state.division);
  }

  function matchesSearch(trophy) {
    if (!state.search) return true;
    return normalise([trophy.name, trophy.secondaryName, trophy.category, ...trophy.winners.map(winner => winner.name)].filter(Boolean).join(' ')).includes(state.search);
  }

  function buildPeople(division = state.division) {
    const people = new Map();
    state.data.trophies
      .filter(trophy => division === 'all' || trophy.division === division)
      .forEach(trophy => trophy.winners.forEach(winner => {
        const person = people.get(winner.personId) || { id: winner.personId, name: winner.name, honours: [] };
        person.honours.push({ year: winner.year, trophy });
        people.set(winner.personId, person);
      }));
    return [...people.values()].sort((a, b) => a.name.localeCompare(b.name, 'en-GB', { sensitivity: 'base' }));
  }

  function allYears() {
    return [...new Set(state.data.trophies.flatMap(trophy => trophy.winners.map(winner => winner.year)))].sort((a, b) => b - a);
  }

  function trophyVisual(trophy) {
    const illustrationClass = /\/illustration(?:[?#]|$)/i.test(trophy.imageUrl || '') ? ' is-illustration' : '';
    const fallback = `<span class="trophy-placeholder" aria-hidden="true"><b>✦</b><small>${escapeHtml(trophy.name.charAt(0))}</small></span>`;
    return trophy.imageUrl
      ? `<span class="trophy-visual${illustrationClass}"><img src="${escapeAttribute(trophy.imageUrl)}" alt="${escapeAttribute(trophy.name)}" loading="lazy">${fallback}</span>`
      : `<span class="trophy-visual is-placeholder">${fallback}</span>`;
  }

  function attachImageFallbacks(container) {
    container.querySelectorAll('.trophy-visual img').forEach(image => image.addEventListener('error', () => {
      image.parentElement.classList.add('is-placeholder');
      image.remove();
    }, { once: true }));
  }

  function groupBy(items, key) {
    return items.reduce((groups, item) => {
      const value = key(item);
      (groups[value] ||= []).push(item);
      return groups;
    }, {});
  }

  async function shareBoard() {
    const url = `${location.origin}${location.pathname}`;
    const share = { title: `${state.data?.club.name || 'Club'} honours board`, text: 'Explore our club trophy winners.', url };
    try {
      if (navigator.share) await navigator.share(share);
      else {
        await navigator.clipboard.writeText(url);
        showShareStatus('Link copied');
      }
    } catch (error) {
      if (error.name !== 'AbortError') showShareStatus('Copy the address from your browser to share it');
    }
  }

  function showShareStatus(message) {
    elements.shareStatus.textContent = message;
    elements.shareStatus.classList.add('is-visible');
    window.setTimeout(() => elements.shareStatus.classList.remove('is-visible'), 2800);
  }

  function showError() {
    elements.browser.hidden = true;
    elements.navigation.hidden = true;
    elements.empty.hidden = true;
    elements.error.hidden = false;
    elements.share.hidden = true;
  }

  function noResults(message) {
    return `<div class="no-results"><span aria-hidden="true">✦</span><strong>${escapeHtml(message)}</strong><small>Try another search, year or trophy type.</small></div>`;
  }

  function divisionLabel(division) {
    return ({ gents: 'Gents', ladies: 'Ladies', junior: 'Junior', mixed: 'Mixed & open' })[division] || 'Mixed & open';
  }

  function normalise(value) {
    return String(value || '').toLocaleLowerCase('en-GB').replace(/\s+/g, ' ').trim();
  }

  function formatNumber(value) {
    return new Intl.NumberFormat('en-GB').format(value || 0);
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, character => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;',
    })[character]);
  }

  function escapeAttribute(value) {
    return escapeHtml(value).replace(/`/g, '&#096;');
  }
})();
