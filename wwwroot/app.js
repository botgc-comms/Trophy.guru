(() => {
  const loginForm = document.querySelector('#login-form');
  const signupForm = document.querySelector('#signup-form');
  const clubForm = document.querySelector('#club-setup-form');
  const loginPassword = document.querySelector('#login-password');
  const passwordToggle = document.querySelector('#toggle-login-password');

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
  coreScript.src = '/app-core.js?v=20260901-login-2';
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
    applyAccountIdentity(auth.user);
    applyClubBranding(auth.club);
    document.querySelector('#login-screen').hidden = true;
    document.querySelector('#club-setup-screen').hidden = true;
    if (['#signup', '#login', ''].includes(location.hash)) history.replaceState({}, '', '#catalogue');
    await loadCatalogue();
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

  function applyAccountIdentity(user) {
    if (!user) return;
    const displayName = user.displayName || 'Account';
    document.querySelector('#archive-account-initial').textContent = displayName.trim().charAt(0).toUpperCase() || 'A';
    document.querySelector('#archive-account-name').textContent = displayName;
    document.querySelector('#archive-menu-name').textContent = displayName;
    document.querySelector('#archive-menu-email').textContent = user.email || '';
  }

  function applyClubBranding(club) {
    if (!club) return;
    document.querySelector('#club-name').textContent = club.name;
    document.querySelector('#club-subtitle').textContent = `${club.sport} · Trophy Archive`;
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
