(() => {
  const loginForm = document.querySelector('#login-form');
  const signupForm = document.querySelector('#signup-form');
  const clubForm = document.querySelector('#club-setup-form');
  const loginPassword = document.querySelector('#login-password');
  const passwordToggle = document.querySelector('#toggle-login-password');
  const trophyPlans = [
    { code: 'free', name: 'Free', allowance: 1, price: 0, description: 'Try the complete workflow with one trophy.' },
    { code: 'club-10', name: 'Club 10', allowance: 10, price: 60, description: '£6 per trophy · save 20%' },
    { code: 'heritage-50', name: 'Heritage 50', allowance: 50, price: 225, description: '£4.50 per trophy · save 40%' },
    { code: 'cabinet-250', name: 'Cabinet 250', allowance: 250, price: 875, description: '£3.50 per trophy · save 53%' },
  ];
  const singleTrophyPrice = 7.5;
  let preferredPlanCode = null;
  let selectedPurchase = null;

  loginForm?.addEventListener('submit', event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    accountSignIn(event.currentTarget);
  }, true);
  signupForm?.addEventListener('submit', event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    accountSignUp(event.currentTarget);
  }, true);
  clubForm?.addEventListener('submit', event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    saveClub(event.currentTarget);
  }, true);
  document.querySelector('#logout-button')?.addEventListener('click', event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    accountSignOut();
  }, true);
  document.querySelector('#setup-signout-button')?.addEventListener('click', accountSignOut);
  document.querySelector('#account-settings-button')?.addEventListener('click', openAccountSettings);
  document.querySelector('#header-plan-button')?.addEventListener('click', openPlanDialog);
  document.querySelector('#plan-dialog .commercial-dialog-close')?.addEventListener('click', () => document.querySelector('#plan-dialog').close());
  document.querySelector('#plan-dialog')?.addEventListener('click', event => { if (event.target === event.currentTarget) event.currentTarget.close(); });
  document.querySelector('#plan-quote-form')?.addEventListener('submit', event => event.preventDefault());
  document.querySelector('#additional-trophies')?.addEventListener('input', () => {
    preferredPlanCode = null;
    renderPlanDialog();
  });
  document.querySelector('#plan-tier-list')?.addEventListener('click', event => {
    const choice = event.target.closest('[data-plan-code]');
    if (!choice) return;
    const current = currentPlanContext();
    const plan = trophyPlans.find(item => item.code === choice.dataset.planCode);
    if (!plan || current.unlimited || plan.allowance <= current.allowance) return;
    preferredPlanCode = plan.code;
    selectedPurchase = { kind: 'upgrade', planCode: plan.code };
    document.querySelector('#additional-trophies').value = String(plan.allowance - current.allowance);
    renderPlanDialog();
    document.querySelector('#plan-quote-results').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  });
  document.querySelector('#plan-quote-results')?.addEventListener('click', event => {
    const choice = event.target.closest('[data-purchase-kind]');
    if (!choice) return;
    selectedPurchase = choice.dataset.purchaseKind === 'upgrade'
      ? { kind: 'upgrade', planCode: choice.dataset.planCode }
      : { kind: 'topup' };
    renderPlanDialog();
  });
  document.querySelector('#account-settings-dialog .commercial-dialog-close')?.addEventListener('click', () => document.querySelector('#account-settings-dialog').close());
  document.querySelector('#account-settings-dialog')?.addEventListener('click', event => { if (event.target === event.currentTarget) event.currentTarget.close(); });
  document.querySelector('#account-club-form')?.addEventListener('submit', saveAccountSettings);
  document.querySelector('#settings-logo-input')?.addEventListener('change', previewSettingsLogo);
  document.querySelector('#show-login-button')?.addEventListener('click', () => showAuthTab('login'));
  document.querySelector('#show-signup-button')?.addEventListener('click', () => showAuthTab('signup'));
  passwordToggle?.addEventListener('click', () => {
    const isVisible = loginPassword.type === 'text';
    loginPassword.type = isVisible ? 'password' : 'text';
    passwordToggle.textContent = isVisible ? 'Show' : 'Hide';
    passwordToggle.setAttribute('aria-pressed', String(!isVisible));
    loginPassword.focus();
  });
  document.querySelector('#club-logo-input')?.addEventListener('change', previewClubLogo);

  const coreScript = document.createElement('script');
  coreScript.src = '/app-core.js?v=20260905-co-winner-1';
  coreScript.onload = () => {
    installBatchUploadControl();
    accountInitialise();
    window.dispatchEvent(new CustomEvent('trophy-app-ready'));
  };
  document.head.append(coreScript);

  async function accountInitialise() {
    try {
      const auth = await api('/api/auth/status');
      state.auth = auth;
      state.aiConfigured = auth.aiConfigured;
      if (!auth.authenticated) {
        showAuthTab(location.hash === '#signup' ? 'signup' : 'login');
        document.querySelector('#login-screen').hidden = false;
        return;
      }
      applyAccountIdentity(auth.user);
      if (auth.onboardingRequired) {
        showClubSetup(auth);
        return;
      }
      await enterArchive(auth);
    } catch (exception) {
      showAccountError('#login-error', exception.message);
    }
  }

  async function accountSignIn(form) {
    const submit = form.querySelector('[type="submit"]');
    const error = document.querySelector('#login-error');
    submit.disabled = true;
    error.hidden = true;
    try {
      const auth = await api('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({
          email: document.querySelector('#login-email').value.trim(),
          password: loginPassword.value.trim(),
        }),
      });
      state.auth = auth;
      state.aiConfigured = auth.aiConfigured;
      window.trophyAnalytics?.track('login', { method: 'password' });
      loginPassword.value = '';
      if (auth.onboardingRequired) {
        showClubSetup(auth);
        return;
      }
      await enterArchive(auth);
    } catch (exception) {
      showAccountError('#login-error', exception.message);
    } finally {
      submit.disabled = false;
    }
  }

  async function accountSignUp(form) {
    const submit = form.querySelector('[type="submit"]');
    const error = document.querySelector('#signup-error');
    submit.disabled = true;
    error.hidden = true;
    try {
      const auth = await api('/api/auth/signup', {
        method: 'POST',
        body: JSON.stringify({
          displayName: document.querySelector('#signup-name').value.trim(),
          email: document.querySelector('#signup-email').value.trim(),
          password: document.querySelector('#signup-password').value,
        }),
      });
      state.auth = auth;
      state.aiConfigured = auth.aiConfigured;
      window.trophyAnalytics?.track('sign_up', { method: 'password' });
      form.reset();
      showClubSetup(auth);
    } catch (exception) {
      showAccountError('#signup-error', exception.message);
    } finally {
      submit.disabled = false;
    }
  }

  async function saveClub(form) {
    const submit = form.querySelector('.setup-submit');
    const error = document.querySelector('#club-setup-error');
    const logo = document.querySelector('#club-logo-input').files?.[0];
    if (!logo) {
      showAccountError('#club-setup-error', 'Add the club logo before creating the archive.');
      return;
    }
    submit.disabled = true;
    error.hidden = true;
    try {
      setBusy(true, 'Creating your club archive…', 'Saving the club identity and preparing a separate trophy collection.');
      await api('/api/club', {
        method: 'PUT',
        body: JSON.stringify({
          name: document.querySelector('#club-name-input').value.trim(),
          sport: document.querySelector('#club-sport-input').value.trim(),
          country: document.querySelector('#club-country-input').value.trim(),
          website: document.querySelector('#club-website-input').value.trim() || null,
        }),
      });
      const logoForm = new FormData();
      logoForm.append('logo', logo, logo.name);
      await api('/api/club/logo', { method: 'POST', body: logoForm });
      const auth = await api('/api/auth/status');
      if (auth.onboardingRequired) throw new Error('The club setup is incomplete. Check the details and logo, then try again.');
      state.auth = auth;
      state.aiConfigured = auth.aiConfigured;
      await enterArchive(auth);
      showToast(`${auth.club.name} is ready. Add the first trophy whenever you’re ready.`);
    } catch (exception) {
      showAccountError('#club-setup-error', exception.message);
    } finally {
      setBusy(false);
      submit.disabled = false;
    }
  }

  async function enterArchive(auth) {
    state.auth = auth;
    applyAccountIdentity(auth.user);
    applyClubBranding(auth.club);
    document.querySelector('#login-screen').hidden = true;
    document.querySelector('#club-setup-screen').hidden = true;
    if (['#signup', '#login', ''].includes(location.hash)) history.replaceState({}, '', '#catalogue');
    await loadCatalogue();
    renderPlanSummary(auth.balance);
    const trophyId = trophyIdFromHash();
    if (trophyId) await openTrophy(trophyId, false);
    else closeTrophy(false);
  }

  function showClubSetup(auth) {
    document.querySelector('#login-screen').hidden = true;
    document.querySelector('#club-setup-screen').hidden = false;
    const club = auth.club;
    if (club) {
      document.querySelector('#club-name-input').value = club.name || '';
      document.querySelector('#club-sport-input').value = club.sport || '';
      document.querySelector('#club-country-input').value = club.country || '';
      document.querySelector('#club-website-input').value = club.website || '';
    }
    setTimeout(() => document.querySelector('#club-name-input').focus(), 50);
  }

  function showAuthTab(tab) {
    const signup = tab === 'signup';
    document.querySelector('#login-form').hidden = signup;
    document.querySelector('#signup-form').hidden = !signup;
    const loginButton = document.querySelector('#show-login-button');
    const signupButton = document.querySelector('#show-signup-button');
    loginButton.classList.toggle('is-active', !signup);
    signupButton.classList.toggle('is-active', signup);
    loginButton.setAttribute('aria-selected', String(!signup));
    signupButton.setAttribute('aria-selected', String(signup));
    document.querySelector('#login-error').hidden = true;
    document.querySelector('#signup-error').hidden = true;
    setTimeout(() => document.querySelector(signup ? '#signup-name' : '#login-email').focus(), 30);
  }

  function previewClubLogo(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const preview = document.querySelector('#club-logo-preview');
    const objectUrl = URL.createObjectURL(file);
    preview.innerHTML = `<img src="${objectUrl}" alt="Selected club logo"><small>Tap to choose a different logo</small>`;
    preview.querySelector('img').addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true });
  }

  function openAccountSettings() {
    const auth = state.auth;
    if (!auth?.club) return;
    document.querySelector('.archive-account-menu')?.removeAttribute('open');
    document.querySelector('#settings-club-name').value = auth.club.name || '';
    document.querySelector('#settings-club-sport').value = auth.club.sport || '';
    document.querySelector('#settings-club-country').value = auth.club.country || '';
    document.querySelector('#settings-club-website').value = auth.club.website || '';
    renderSettingsLogo(auth.club.logoUrl, auth.club.name);
    document.querySelector('#account-settings-error').hidden = true;
    document.querySelector('#account-settings-dialog').showModal();
  }

  function previewSettingsLogo(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const objectUrl = URL.createObjectURL(file);
    renderSettingsLogo(objectUrl, 'Selected club');
    document.querySelector('#settings-logo-preview img')?.addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true });
  }

  function renderSettingsLogo(url, name) {
    const preview = document.querySelector('#settings-logo-preview');
    preview.innerHTML = url
      ? `<img src="${url}" alt="${name || 'Club'} logo"><small>Choose a different logo</small>`
      : '<b>+</b><small>Choose a club logo</small>';
  }

  async function saveAccountSettings(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const submit = form.querySelector('[type="submit"]');
    const error = document.querySelector('#account-settings-error');
    const logo = document.querySelector('#settings-logo-input').files?.[0];
    submit.disabled = true;
    error.hidden = true;
    try {
      await api('/api/club', {
        method: 'PUT',
        body: JSON.stringify({
          name: document.querySelector('#settings-club-name').value.trim(),
          sport: document.querySelector('#settings-club-sport').value.trim(),
          country: document.querySelector('#settings-club-country').value.trim(),
          website: document.querySelector('#settings-club-website').value.trim() || null,
        }),
      });
      if (logo) {
        const logoForm = new FormData();
        logoForm.append('logo', logo, logo.name);
        await api('/api/club/logo', { method: 'POST', body: logoForm });
      }
      const auth = await api('/api/auth/status');
      state.auth = auth;
      state.aiConfigured = auth.aiConfigured;
      applyClubBranding(auth.club);
      document.querySelector('#settings-logo-input').value = '';
      document.querySelector('#account-settings-dialog').close();
      showToast('Club details saved.');
    } catch (exception) {
      error.textContent = exception.message;
      error.hidden = false;
    } finally {
      submit.disabled = false;
    }
  }

  function applyAccountIdentity(user) {
    if (!user) return;
    const displayName = user.displayName || 'Account';
    document.querySelector('#archive-account-initial').textContent = displayName.trim().charAt(0).toUpperCase() || 'A';
    document.querySelector('#archive-account-name').textContent = displayName;
    document.querySelector('#archive-menu-name').textContent = displayName;
    document.querySelector('#archive-menu-email').textContent = user.email || '';
    renderPlanSummary(state.auth?.balance);
  }

  function currentPlanContext() {
    const balance = state.auth?.balance || {};
    const unlimited = Boolean(balance.unlimited) || balance.planCode === 'unlimited';
    const plan = trophyPlans.find(item => item.code === balance.planCode) || trophyPlans[0];
    const used = Number(state.totals?.all ?? state.trophies?.length ?? 0);
    const credits = unlimited ? null : Math.max(0, Number(balance.trophyCredits || 0));
    return {
      ...plan,
      name: unlimited ? 'Unlimited' : plan.name,
      allowance: unlimited ? Number.POSITIVE_INFINITY : plan.allowance,
      price: unlimited ? 0 : plan.price,
      unlimited,
      used,
      credits,
    };
  }

  function renderPlanSummary(balance = state.auth?.balance) {
    if (balance && state.auth) state.auth.balance = balance;
    const current = currentPlanContext();
    const name = document.querySelector('#header-plan-name');
    const remaining = document.querySelector('#header-plan-balance');
    if (name) name.textContent = current.name;
    if (remaining) remaining.textContent = current.unlimited ? 'No limit' : `${current.credits} left`;
    document.querySelector('#header-plan-button')?.setAttribute('aria-label', current.unlimited
      ? 'Current plan: Unlimited. View plans and trophy capacity.'
      : `Current plan: ${current.name}. ${current.credits} trophy ${current.credits === 1 ? 'credit' : 'credits'} available. View plans.`);
  }

  function openPlanDialog() {
    document.querySelector('.archive-account-menu')?.removeAttribute('open');
    preferredPlanCode = null;
    selectedPurchase = null;
    document.querySelector('#additional-trophies').value = '1';
    renderPlanDialog();
    document.querySelector('#plan-dialog').showModal();
  }

  function renderPlanDialog() {
    const current = currentPlanContext();
    const currentName = document.querySelector('#plan-current-name');
    const currentUsage = document.querySelector('#plan-current-usage');
    const input = document.querySelector('#additional-trophies');
    currentName.textContent = current.name;
    currentUsage.textContent = current.unlimited
      ? `${current.used} catalogued · no trophy limit`
      : `${current.used} catalogued · ${current.credits} ${current.credits === 1 ? 'credit' : 'credits'} available`;
    input.disabled = current.unlimited;
    renderPlanTiers(current);
    renderPlanQuote(current);
  }

  function renderPlanTiers(current) {
    const list = document.querySelector('#plan-tier-list');
    list.innerHTML = trophyPlans.map(plan => {
      const isCurrent = !current.unlimited && plan.code === current.code;
      const unavailable = current.unlimited || plan.allowance <= current.allowance;
      const upgradeCost = Math.max(0, plan.price - current.price);
      const action = current.unlimited ? 'Already covered' : isCurrent ? 'Current plan' : unavailable ? 'Included' : `Compare ${formatMoney(upgradeCost)} upgrade`;
      return `<article class="plan-tier ${isCurrent ? 'is-current' : ''}">
        <span><small>${plan.allowance === 1 ? 'First trophy' : `${plan.allowance} trophies`}</small><strong>${plan.name}</strong></span>
        <b>${formatMoney(plan.price)}</b>
        <p>${plan.description}</p>
        <button type="button" data-plan-code="${plan.code}" ${unavailable ? 'disabled' : ''}>${action}</button>
      </article>`;
    }).join('');
  }

  function renderPlanQuote(current) {
    const results = document.querySelector('#plan-quote-results');
    const selection = document.querySelector('#plan-selection');
    if (current.unlimited) {
      results.innerHTML = '<div class="unlimited-plan-note"><strong>No additional capacity is needed</strong><span>This account can catalogue any number of trophies.</span></div>';
      selection.innerHTML = '';
      return;
    }

    const input = document.querySelector('#additional-trophies');
    const additional = Math.min(1000, Math.max(1, Math.round(Number(input.value) || 1)));
    if (String(additional) !== input.value) input.value = String(additional);
    const topupCost = additional * singleTrophyPrice;
    const requestedAllowance = current.allowance + additional;
    const preferred = trophyPlans.find(plan => plan.code === preferredPlanCode && plan.allowance > current.allowance);
    const upgrade = preferred || trophyPlans.find(plan => plan.allowance >= requestedAllowance && plan.allowance > current.allowance);
    const upgradeCost = upgrade ? Math.max(0, upgrade.price - current.price) : null;
    const recommendUpgrade = upgrade && upgradeCost < topupCost;

    if (!selectedPurchase || (selectedPurchase.kind === 'upgrade' && selectedPurchase.planCode !== upgrade?.code)) {
      selectedPurchase = recommendUpgrade
        ? { kind: 'upgrade', planCode: upgrade.code }
        : { kind: 'topup' };
    }

    const topupSelected = selectedPurchase.kind === 'topup';
    const upgradeSelected = selectedPurchase.kind === 'upgrade' && selectedPurchase.planCode === upgrade?.code;
    results.innerHTML = `<button class="plan-quote-option ${topupSelected ? 'is-selected' : ''}" type="button" data-purchase-kind="topup">
        <span><small>Pay as you go${!recommendUpgrade ? ' · best price' : ''}</small><strong>${additional} additional ${additional === 1 ? 'trophy' : 'trophies'}</strong><em>£7.50 per trophy</em></span><b>${formatMoney(topupCost)}</b>
      </button>${upgrade ? `<button class="plan-quote-option ${upgradeSelected ? 'is-selected' : ''}" type="button" data-purchase-kind="upgrade" data-plan-code="${upgrade.code}">
        <span><small>${recommendUpgrade ? 'Recommended · lower price' : 'Plan upgrade'}</small><strong>Upgrade to ${upgrade.name}</strong><em>${upgrade.allowance - current.allowance} more trophies in your plan</em></span><b>${formatMoney(upgradeCost)}</b>
      </button>` : '<div class="plan-quote-limit"><strong>More than 250 trophies?</strong><span>Use individual credits for the extra capacity while we prepare a larger collection plan.</span></div>'}`;

    const selectedPlan = upgradeSelected ? upgrade : null;
    const selectionName = selectedPlan ? `Upgrade to ${selectedPlan.name}` : `${additional} additional ${additional === 1 ? 'trophy' : 'trophies'}`;
    const selectionCost = selectedPlan ? upgradeCost : topupCost;
    selection.innerHTML = `<span><small>Selected option</small><strong>${selectionName} · ${formatMoney(selectionCost)}</strong></span><button type="button" disabled>Checkout not connected</button>`;
  }

  function formatMoney(value) {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency: 'GBP',
      minimumFractionDigits: Number.isInteger(value) ? 0 : 2,
      maximumFractionDigits: 2,
    }).format(value);
  }

  window.refreshPlanSummary = renderPlanSummary;

  function applyClubBranding(club) {
    if (!club) return;
    document.querySelector('#club-name').textContent = club.name;
    document.querySelector('#club-subtitle').textContent = `${club.sport} · Trophy Archive`;
    const honoursLink = document.querySelector('#honours-board-link');
    if (honoursLink && club.id && club.complete) {
      honoursLink.href = `/honours/${encodeURIComponent(club.id)}`;
      honoursLink.hidden = false;
    }
    const monogram = document.querySelector('#club-monogram');
    const logo = document.querySelector('#club-logo');
    monogram.textContent = club.name.trim().charAt(0).toUpperCase() || 'T';
    if (club.logoUrl) {
      logo.src = club.logoUrl;
      logo.alt = `${club.name} logo`;
      logo.hidden = false;
      monogram.hidden = true;
      logo.onerror = () => { logo.hidden = true; monogram.hidden = false; };
    } else {
      logo.hidden = true;
      monogram.hidden = false;
    }
    document.title = `Trophy Archive · ${club.name}`;
  }

  async function accountSignOut() {
    try { await api('/api/auth/logout', { method: 'POST', body: '{}' }); } catch { }
    location.href = '/';
  }

  function showAccountError(selector, message) {
    const element = document.querySelector(selector);
    element.textContent = message;
    element.hidden = false;
  }
})();
