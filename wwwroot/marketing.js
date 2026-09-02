(() => {
  if (location.hash.startsWith('#trophy/') || ['#catalogue', '#signup', '#login'].includes(location.hash)) {
    location.replace('/archive.html' + location.hash);
    return;
  }

  initialiseAccountHeader();

  async function initialiseAccountHeader() {
    try {
      const response = await fetch('/api/auth/status', { credentials: 'same-origin' });
      if (!response.ok) return;
      const auth = await response.json();
      if (!auth.authenticated) return;

      document.querySelector('#signed-out-actions').hidden = true;
      document.querySelector('#signed-in-actions').hidden = false;
      const displayName = auth.user?.displayName || 'My account';
      document.querySelector('#public-account-initial').textContent = displayName.trim().charAt(0).toUpperCase() || 'A';
      document.querySelector('#public-account-name').textContent = displayName;
      document.querySelector('#public-menu-name').textContent = displayName;
      document.querySelector('#public-menu-email').textContent = auth.user?.email || '';
      document.querySelector('#public-logout-button').addEventListener('click', signOut);
    } catch {
      // The public information page remains fully usable if account status is unavailable.
    }
  }

  async function signOut() {
    const button = document.querySelector('#public-logout-button');
    button.disabled = true;
    try {
      await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
      });
    } finally {
      location.reload();
    }
  }
})();
