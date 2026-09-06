(() => {
  'use strict';
  const content = document.querySelector('#security-content');
  if (!content) {
    const login = document.querySelector('#login-form');
    if (login && !login.querySelector('[data-security-link]')) {
      const forgot = document.createElement('a');
      forgot.href = '/account-security.html#forgot'; forgot.textContent = 'Forgot your password?';
      forgot.className = 'security-inline-link'; forgot.dataset.securityLink = 'forgot'; login.append(forgot);
    }
    const logout = document.querySelector('#logout-button');
    if (logout && !document.querySelector('[data-security-link="settings"]')) {
      const settings = document.createElement('a');
      settings.href = '/account-security.html#settings'; settings.textContent = 'Security & editor access';
      settings.className = 'security-inline-link'; settings.dataset.securityLink = 'settings'; logout.before(settings);
    }
    let refreshing = false;
    const refreshBanner = async () => {
      if (refreshing) return;
      refreshing = true;
      try {
        const account = await request('/api/auth/security');
        let banner = document.querySelector('#security-verification-banner');
        if (account.emailVerified) { banner?.remove(); return; }
        if (!banner) {
          banner = document.createElement('aside'); banner.id = 'security-verification-banner'; banner.className = 'security-verification-banner';
          banner.append(document.createTextNode('Verify your email to publish your honours board, buy credits and invite editors. '));
          const link = document.createElement('a'); link.href = '/account-security.html#settings'; link.textContent = 'Manage email verification'; banner.append(link);
          document.querySelector('.catalogue-heading')?.after(banner);
        }
      } catch { document.querySelector('#security-verification-banner')?.remove(); }
      finally { refreshing = false; }
    };
    window.addEventListener('trophy-account-changed', refreshBanner);
    window.addEventListener('focus', refreshBanner);
    refreshBanner();
    return;
  }

  const title = document.querySelector('#security-title');
  const notice = document.querySelector('#security-notice');
  const params = new URLSearchParams(location.hash.slice(1));
  const action = ['reset', 'verify', 'invite'].find(name => params.has(name));
  let actionToken = action ? params.get(action) : null;
  // Keep action tokens out of HTTP requests, history, referrers and analytics.
  if (action) history.replaceState(null, '', `${location.pathname}#${action}`);
  initialise().catch(error => { content.replaceChildren(); showNotice(error.message, true); }).finally(() => content.setAttribute('aria-busy', 'false'));

  async function request(url, data, method = data === undefined ? 'GET' : 'POST') {
    const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) {
      const error = new Error(result.message || (response.status === 401 ? 'Sign in to manage your account.' : response.status === 429 ? 'Please wait a minute before trying again.' : 'This request could not be completed. Please try again.'));
      error.status = response.status; throw error;
    }
    return result;
  }

  function showNotice(message, error = false) {
    notice.textContent = message; notice.hidden = false; notice.classList.toggle('is-error', error);
    notice.scrollIntoView({ block:'nearest', behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }

  function card(heading, copy) {
    const section = document.createElement('section'); section.className = 'security-card';
    if (heading) { const h = document.createElement('h2'); h.textContent = heading; section.append(h); }
    if (copy) { const p = document.createElement('p'); p.textContent = copy; section.append(p); }
    content.append(section); return section;
  }

  function link(container, text, href) { const anchor = document.createElement('a'); anchor.textContent = text; anchor.href = href; container.append(anchor); return anchor; }
  function button(container, text, action, className = '') {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = text; element.className = className; container.append(element);
    element.addEventListener('click', () => perform(element, action)); return element;
  }
  function field(form, name, label, type = 'text', autocomplete = '') {
    const wrapper = document.createElement('label'); wrapper.textContent = label;
    const input = document.createElement('input'); input.name = name; input.type = type; input.required = true; input.autocomplete = autocomplete;
    if (type === 'password') input.maxLength = 128; else if (type === 'email') input.maxLength = 254; else input.maxLength = 100;
    wrapper.append(input); form.append(wrapper); return input;
  }
  function passwordFields(form) {
    const password = field(form, 'password', 'New password', 'password', 'new-password'); password.minLength = 10;
    const note = document.createElement('small'); note.textContent = '10–128 characters, including at least one letter and one number.'; note.id = 'security-password-help'; password.setAttribute('aria-describedby', note.id); password.parentElement.after(note);
    const confirm = field(form, 'confirm', 'Confirm new password', 'password', 'new-password');
    confirm.addEventListener('input', () => confirm.setCustomValidity(''));
    password.addEventListener('input', () => confirm.setCustomValidity(''));
    return () => {
      if (password.value !== confirm.value) { confirm.setCustomValidity('The passwords do not match.'); confirm.reportValidity(); return null; }
      return password.value;
    };
  }
  function submit(form, label, action) {
    const control = document.createElement('button'); control.type = 'submit'; control.textContent = label; form.append(control);
    form.addEventListener('submit', event => { event.preventDefault(); perform(control, action); }); return control;
  }
  async function perform(control, action) {
    control.disabled = true; notice.hidden = true;
    try { await action(); } catch (error) { showNotice(error.message, true); } finally { control.disabled = false; }
  }

  async function initialise() {
    content.replaceChildren();
    if (action && !/^[A-Za-z0-9_-]{43}$/.test(actionToken || '')) throw new Error('This link is incomplete. Open the complete link in your email.');
    if (action === 'reset') return renderReset();
    if (action === 'verify') return renderVerify();
    if (action === 'invite') return renderInvitation();
    if (location.hash === '#forgot') return renderForgot();
    if (['#reset', '#verify', '#invite'].includes(location.hash)) throw new Error('Reopen the link in your email to continue. Links expire and can only be used once.');
    return renderSettings();
  }

  function renderForgot() {
    title.textContent = 'Reset your password';
    const section = card('', 'Enter the email address you use for your archive.');
    const form = document.createElement('form'); section.append(form);
    const email = field(form, 'email', 'Email address', 'email', 'username');
    submit(form, 'Request reset link', async () => { const result = await request('/api/auth/forgot-password', { email: email.value.trim() }); showNotice(result.message); });
    const help = document.createElement('p'); help.className = 'security-help'; help.textContent = 'Original archive accounts using a .test address can still use their existing email/password or original archive sign-in.'; section.append(help);
  }

  function renderReset() {
    title.textContent = 'Choose a new password';
    const section = card('', 'Changing your password signs out your account on every device.');
    const form = document.createElement('form'); section.append(form); const getPassword = passwordFields(form);
    submit(form, 'Save new password', async () => {
      const password = getPassword(); if (!password) return;
      const result = await request('/api/auth/reset-password', { token: actionToken, password }); actionToken = null; content.replaceChildren(); showNotice(result.message);
      link(content, 'Sign in with your new password', '/archive.html#login');
    });
  }

  function renderVerify() {
    title.textContent = 'Verify your email';
    const section = card('', 'Confirm this email address for your Trophy Archive account.');
    button(section, 'Verify email address', async () => {
      const result = await request('/api/auth/verify-email', { token: actionToken }); actionToken = null; content.replaceChildren(); showNotice(result.message); link(content, 'Return to your archive', '/archive.html');
    });
  }

  async function renderInvitation() {
    title.textContent = 'Join your club archive'; content.replaceChildren();
    let account = null;
    try { account = await request('/api/auth/security'); } catch (error) { if (error.status !== 401) throw error; }
    if (account) {
      const section = card('', `You are signed in as ${account.email}. Accept using the address that received this invitation.`);
      if (account.clubId) {
        const message = document.createElement('p'); message.textContent = 'This account already belongs to a club. An invitation cannot move or replace its archive. Sign in with an account that does not already belong to a club.'; section.append(message);
      } else {
        button(section, 'Accept editor invitation', async () => {
          const result = await request('/api/auth/accept-invitation', { token: actionToken }); actionToken = null; content.replaceChildren(); showNotice(result.message); link(content, 'Open the club archive', '/archive.html');
        });
      }
      const actions = document.createElement('div'); actions.className = 'security-actions'; section.append(actions);
      button(actions, 'Use another account', async () => { await request('/api/auth/logout', {}); await renderInvitation(); }, 'secondary'); return;
    }
    const section = card('', 'Sign in using the email address that received this invitation. New editors can create their account here.');
    let create = false;
    const form = document.createElement('form'); section.append(form);
    const name = field(form, 'displayName', 'Your name', 'text', 'name'); name.minLength = 2; name.parentElement.hidden = true; name.required = false;
    const email = field(form, 'email', 'Email address', 'email', 'username');
    const password = field(form, 'password', 'Password', 'password', 'current-password');
    const send = submit(form, 'Sign in', async () => {
      await request(create ? '/api/auth/signup' : '/api/auth/login', { displayName: name.value.trim(), email: email.value.trim(), password: password.value });
      await renderInvitation();
    });
    const actions = document.createElement('div'); actions.className = 'security-actions'; section.append(actions);
    const toggle = button(actions, 'Create an account', async () => {
      create = !create; name.parentElement.hidden = !create; name.required = create; password.autocomplete = create ? 'new-password' : 'current-password'; password.minLength = create ? 10 : 1;
      send.textContent = create ? 'Create account' : 'Sign in'; toggle.textContent = create ? 'I already have an account' : 'Create an account';
    }, 'secondary');
    const recovery = link(actions, 'Forgot your password?', '/account-security.html#forgot'); recovery.target = '_blank'; recovery.rel = 'noopener';
  }

  async function renderSettings() {
    title.textContent = 'Security & editor access';
    let account;
    try { account = await request('/api/auth/security'); }
    catch (error) {
      if (error.status !== 401) throw error;
      card('', 'Sign in to manage your password, email verification and club editors.'); link(content, 'Sign in to your archive', '/archive.html#login'); return;
    }
    const identity = card('Your email', account.email);
    const status = document.createElement('p'); status.className = 'security-help';
    status.textContent = account.trustedLegacy ? 'Original archive access is preserved. Email verification is not required for this account.' : account.emailVerified ? 'Your email address is verified.' : 'Verify your email before publishing your honours board, buying credits or inviting editors.';
    identity.append(status);
    if (!account.emailVerified) button(identity, 'Send verification email', async () => { const result = await request('/api/auth/resend-verification', {}); showNotice(result.message); });
    if (!account.emailDeliveryAvailable && !account.trustedLegacy) {
      const mailNote = document.createElement('p'); mailNote.className = 'security-help'; mailNote.textContent = 'Account email is currently unavailable. Your existing sign-in continues to work.'; identity.append(mailNote);
    }
    const password = card('Change password', 'All existing sessions will be signed out when your password changes.');
    const form = document.createElement('form'); password.append(form); const current = field(form, 'currentPassword', 'Current password', 'password', 'current-password'); const getPassword = passwordFields(form);
    submit(form, 'Change password', async () => {
      const newPassword = getPassword(); if (!newPassword) return;
      const result = await request('/api/auth/change-password', { currentPassword: current.value, newPassword }); content.replaceChildren(); showNotice(result.message); link(content, 'Sign in again', '/archive.html#login');
    });
    const sessions = card('Signed-in devices', 'Sign out all devices, including this one. This also cancels outstanding verification and password reset links.');
    button(sessions, 'Sign out all devices', async () => { const result = await request('/api/auth/logout-all', {}); content.replaceChildren(); showNotice(result.message); link(content, 'Sign in again', '/archive.html#login'); }, 'secondary');
    if (account.clubId && account.role === 'owner') await renderTeam(account);
    else if (account.clubId) card('Your club access', 'You are an editor. You can manage trophies, evidence and winner records. The club owner manages publication, billing and editor access.');
  }

  async function renderTeam(account) {
    const section = card('Club editors', 'Invite people to help catalogue and check winners. Editors cannot publish the honours board, make purchases, change club settings or invite others. Each account belongs to one club.');
    const team = await request('/api/auth/team');
    for (const member of team.members) {
      const row = document.createElement('div'); row.className = 'security-member';
      const label = document.createElement('span'); const name = document.createElement('strong'); name.textContent = member.displayName; const detail = document.createElement('small'); detail.textContent = `${member.email} · ${member.role}`;
      label.append(name, detail); row.append(label); section.append(row);
      if (member.role === 'editor') button(row, 'Remove access', async () => {
        await request(`/api/auth/team/${encodeURIComponent(member.id)}`, undefined, 'DELETE'); row.remove(); showNotice('Editor access removed and their sessions signed out.');
      }, 'danger');
    }
    for (const invitation of team.invitations) {
      const row = document.createElement('div'); row.className = 'security-member';
      const label = document.createElement('span'); const name = document.createElement('strong'); name.textContent = invitation.email; const detail = document.createElement('small'); detail.textContent = `Invitation expires ${new Date(invitation.expiresAt).toLocaleDateString('en-GB')}`;
      label.append(name, detail); row.append(label); section.append(row);
      button(row, 'Revoke', async () => { await request(`/api/auth/invitations/${encodeURIComponent(invitation.id)}`, undefined, 'DELETE'); row.remove(); showNotice('Invitation revoked.'); }, 'secondary');
    }
    const form = document.createElement('form'); section.append(form); const email = field(form, 'email', 'Editor email address', 'email', 'off');
    const invite = submit(form, 'Invite editor', async () => { const result = await request('/api/auth/invitations', { email: email.value.trim() }); content.replaceChildren(); await renderSettings(); showNotice(result.message); });
    invite.disabled = !account.emailVerified || !account.emailDeliveryAvailable;
    if (invite.disabled) { const help = document.createElement('small'); help.textContent = !account.emailVerified ? 'Verify your email to send invitations.' : 'Invitations become available when account email is connected.'; form.append(help); }
  }
})();