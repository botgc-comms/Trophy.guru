(() => {
  'use strict';
  let state;
  let integrationIntentHandled = false;
  const money = amount => new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(amount / 100);
  const node = (tag, text, className) => { const element = document.createElement(tag); if (text !== undefined) element.textContent = text; if (className) element.className = className; return element; };
  async function api(path, body) {
    const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...(body !== undefined ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}) });
    const result = await response.json();
    if (!response.ok) throw new Error(result.message || 'Billing is temporarily unavailable. Please try again.');
    return result;
  }
  function message(text) { const target = document.querySelector('#billing-message'); if (target) target.textContent = text; }
  async function checkout(packCode, upgradeFrom, credits) {
    const key = `trophy-checkout:volume-20260906:${state.clubId}:${packCode}:${credits || 'default'}:${upgradeFrom || 'new'}`;
    let requestId = sessionStorage.getItem(key); if (!requestId) { requestId = crypto.randomUUID(); sessionStorage.setItem(key, requestId); }
    await redirect('/api/billing/checkout', { packCode, requestId, upgradeFrom: upgradeFrom || null, credits: credits ?? null });
  }
  async function redirect(path, body) {
    document.querySelectorAll('#billing-panel button').forEach(button => { button.disabled = true; });
    message('Opening secure billing…');
    try { const result = await api(path, body); location.assign(result.url); }
    catch (error) { render(); message(error.message); }
  }
  function button(label, action, enabled = true) { const item = node('button', label); item.type = 'button'; item.disabled = !enabled; item.addEventListener('click', action); return item; }
  function integrationCheckout() {
    const key = 'trophy-integration-checkout:' + state.clubId;
    let id = sessionStorage.getItem(key);
    if (!id) { id = crypto.randomUUID(); sessionStorage.setItem(key, id); }
    return redirect('/api/billing/integration-checkout', { requestId: id });
  }
  function renderIntegration(mount) {
    const offer = state.integrationOffer;
    const ready = offer?.available === true && offer.amountPence === 29900 && offer.currency === 'gbp' && offer.billingInterval === 'year';
    const subscribed = state.integrationSubscription?.current === true;
    const card = node('article', undefined, 'integration-offer'); card.id = 'billing-intelligent-golf';
    card.setAttribute('aria-labelledby', 'billing-integration-title');
    const copy = node('div', undefined, 'integration-offer-copy');
    const title = node('h3', 'Intelligent Golf integration'); title.id = 'billing-integration-title';
    copy.append(node('p', 'Optional annual extra', 'integration-kicker'), title,
      node('p', 'Bring your honours board into your club’s member area, with a personal view of each member’s trophies.'),
      node('span', subscribed ? state.integrationSubscription.status === 'active' ? 'Subscription active' : 'Manage existing subscription' : ready ? 'Annual subscription' : 'In development', 'integration-status'));
    const features = node('div', undefined, 'integration-offer-features');
    const list = node('ul');
    for (const text of ['Member-area honours board', '“My trophies” for signed-in members', 'Search approved winners and trophy histories', 'Managed page setup and integration updates']) list.append(node('li', text));
    features.append(node('p', ready || subscribed ? 'Member experience' : 'Planned member experience', 'integration-feature-label'), list);
    const price = node('div', undefined, 'integration-offer-price');
    const amount = node('p'); amount.append(node('strong', '£299'), node('span', 'per club / year'));
    const details = node('a', 'See what the integration includes →', 'integration-detail-link'); details.href = '/integrations/intelligent-golf/';
    price.append(amount, details);
    if (subscribed) price.append(button('Manage annual subscription', () => redirect('/api/billing/portal', {}), state.portalAvailable && state.owner && state.emailVerified));
    else price.append(button(ready ? 'Choose annual integration' : 'Coming soon', integrationCheckout, ready && state.owner && state.emailVerified && !state.balance.onHold));
    price.querySelector('button')?.classList.add('integration-subscribe');
    let availability = 'The member integration is being prepared. No payment is taken yet.';
    if (subscribed) availability = 'Your subscription and club-page installation are managed separately. Contact support for your installation status.';
    else if (ready && !state.owner) availability = 'Your club owner can add this annual subscription.';
    else if (ready && !state.emailVerified) availability = 'Verify your email address before subscribing.';
    else if (ready && state.balance.onHold) availability = 'Contact support to resolve the billing issue before subscribing.';
    else if (ready) availability = '£299 billed annually. Separate from your trophy credit balance.';
    else if (offer?.status && offer.status !== 'coming_soon') availability = 'Annual checkout is temporarily unavailable. No payment can be taken.';
    price.append(node('small', availability));
    card.append(copy, features, price, node('p', 'Public sharing, winner search and member matching are already included in the core archive. This extra adds the managed Intelligent Golf installation and member features. No per-member fee.', 'integration-offer-footnote'));
    mount.append(card);
  }
  function render() {
    const mount = document.querySelector('#billing-panel'); if (!mount || !state) return;
    mount.replaceChildren();
    const { balance } = state;
    const summary = node('div', undefined, 'billing-summary');
    summary.append(node('strong', balance.unlimited ? 'Unlimited trophy credits' : `${balance.available} trophy ${balance.available === 1 ? 'credit' : 'credits'} available`));
    summary.append(node('p', `${balance.used} trophies processed · ${balance.reserved} currently reserved`));
    mount.append(summary);
    const note = node('p', '', 'billing-message'); note.id = 'billing-message'; note.setAttribute('role', 'status'); mount.append(note);
    if (balance.onHold) message('New AI work is paused while a billing issue is reviewed. Your records remain available. Contact support.');
    else if (balance.unlimited) message('Your existing archive has unlimited trophy credits. The optional website integration is priced separately.');
    else if (!state.owner) message('Your club owner manages credit purchases and subscriptions.');
    else if (!state.emailVerified) message('Verify your email address before buying credits. You can still review your archive.');
    else if (!state.paymentsEnabled) message('Payments are not enabled yet. Prices below are for reference; no payment can be taken.');
    else if (state.mode === 'test') message('Payment testing is enabled. Checkout accepts Stripe test cards only; no real payment is taken.');
    const enabled = state.paymentsEnabled && state.owner && state.emailVerified && !balance.unlimited && !balance.onHold;
    const packs = node('div', undefined, 'billing-packs');
    for (const pack of state.packs) {
      const card = node('article', undefined, 'billing-pack');
      if (pack.code === 'complete') {
        const quantity = node('input'); quantity.type = 'number'; quantity.min = '250'; quantity.step = '1'; quantity.value = '250'; quantity.id = 'volume-trophy-quantity';
        const label = node('label', 'Number of trophies'); label.htmlFor = quantity.id;
        const total = node('strong', money(pack.amountPence)); total.id = 'volume-trophy-total';
        const buy = button('Buy credits', () => { if (quantity.reportValidity()) checkout(pack.code, null, Number(quantity.value)); }, enabled);
        quantity.addEventListener('input', () => {
          const count = Number(quantity.value); const valid = Number.isInteger(count) && count >= 250 && count <= 2147483647;
          total.textContent = valid ? money(count * pack.amountPence / pack.credits) : 'Enter 250 or more'; buy.disabled = !enabled || !valid;
        });
        quantity.required = true; quantity.max = '2147483647';
        card.append(node('h3', '250 or more trophies'), node('p', '£2.50 per trophy'), label, quantity, total, node('p', 'One-off purchase. Credits do not expire.'), buy);
        packs.append(card); continue;
      }
      card.append(node('h3', `${pack.credits} trophy ${pack.credits === 1 ? 'credit' : 'credits'}`), node('strong', money(pack.amountPence)), node('p', 'One-off purchase. Credits do not expire.'), button('Buy credits', () => checkout(pack.code), enabled));
      packs.append(card);
    }
    mount.append(packs);
    renderIntegration(mount);
    if (state.upgrades.length && !balance.unlimited) {
      mount.append(node('h3', 'Upgrade a previous pack'));
      const upgrades = node('div', undefined, 'billing-upgrades');
      for (const quote of state.upgrades) {
        const pack = state.packs.find(item => item.code === quote.packCode);
        upgrades.append(button(`Upgrade to ${pack.credits}: add ${quote.credits} credits for ${money(quote.amountPence)}`, () => checkout(quote.packCode, quote.upgradeFrom), enabled));
      }
      mount.append(upgrades, node('p', 'Upgrades add only the extra credits. Credits you have already used stay used; your free first trophy is separate.'));
    }
    mount.append(node('p', 'One credit covers a trophy’s first successful AI job. Work reserves the credit while processing. Additional attempts are included within the published allowance; manual review and access to your saved records do not spend credits.', 'billing-explanation'));
    mount.append(node('p', 'First trophy: up to 12 saved photos, 3 readings and 2 illustrations. Paid clubs: up to 40 photos, 12 readings and 3 illustrations per trophy. Contact support for larger jobs.'));
    mount.append(node('p', 'Archive storage is limited to keep the service reliable. The standard allowance is 256 MiB for a free archive and 2 GiB for a paid archive; contact support for larger collections. Existing records remain available when an allowance is reached.'));
    if (state.purchases.length) {
      mount.append(node('h3', 'Recent purchases'));
      const history = node('ul', undefined, 'billing-history');
      for (const purchase of state.purchases.slice(0, 10)) history.append(node('li', `${purchase.credits} credits · ${money(purchase.amountPence)} · ${purchase.state}`));
      mount.append(history);
    }
    if (state.reviewJobs?.length) {
      mount.append(node('h3', 'Interrupted AI requests need your review'));
      for (const job of state.reviewJobs) {
        const card = node('article', undefined, 'billing-pack');
        card.append(node('strong', `${job.kind === 'analysis' ? 'Winner reading' : 'Illustration'} for trophy ${job.trophyId}`), node('p', 'Check the trophy for a saved result first. The provider may have completed this request. Acknowledging this will allow a new request within your remaining allowance; it does not automatically retry.'));
        const label = node('label'); const check = node('input'); check.type = 'checkbox';
        label.append(check, document.createTextNode(' I checked the trophy and understand this attempt still counts.'));
        const acknowledge = button('Acknowledge interrupted request', async () => {
          acknowledge.disabled = true;
          try { await api(`/api/billing/jobs/${encodeURIComponent(job.id)}/acknowledge`, { understandAttemptStillCounts: check.checked }); await refresh(); }
          catch (error) { message(error.message); acknowledge.disabled = !check.checked; }
        }, false);
        check.addEventListener('change', () => { acknowledge.disabled = !check.checked || !state.owner || !state.emailVerified; });
        card.append(label, acknowledge); mount.append(card);
      }
    }
    if (state.portalAvailable) mount.append(button('Manage payments and subscriptions', () => redirect('/api/billing/portal', {}), state.owner && state.emailVerified));

  }
  async function refresh() {
    try {
      state = await api('/api/billing');
      for (const purchase of state.purchases) {
        if (purchase.state === 'pending') continue;
        const key = `trophy-checkout:${state.clubId}:${purchase.packCode}:${purchase.upgradeFrom || 'new'}`;
        if (sessionStorage.getItem(key) === purchase.requestId) sessionStorage.removeItem(key);
      }
      const name = document.querySelector('#header-plan-name'); const balance = document.querySelector('#header-plan-balance');
      if (name) name.textContent = state.balance.unlimited ? 'Unlimited' : `${state.balance.available} ${state.balance.available === 1 ? 'credit' : 'credits'}`;
      if (balance) balance.textContent = state.balance.unlimited ? 'No limit' : `${state.balance.available} left`;
      render();
      if (!integrationIntentHandled && new URLSearchParams(location.search).get('addon') === 'intelligent-golf') {
        integrationIntentHandled = true;
        const dialog = document.querySelector('#plan-dialog');
        if (dialog && !dialog.open) dialog.showModal();
        document.querySelector('#billing-intelligent-golf')?.scrollIntoView({ block: 'start' });
      }
      return state;
    } catch (error) { message(error.message); return null; }
  }
  async function open() {
    const dialog = document.querySelector('#plan-dialog'); if (dialog && !dialog.open) dialog.showModal();
    const mount = document.querySelector('#billing-panel'); if (mount) mount.replaceChildren(node('p', 'Loading your club balance…'));
    await refresh();
  }
  window.TrophyBilling = { open, refresh };
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  document.addEventListener('DOMContentLoaded', () => {
    const returned = new URLSearchParams(location.search).get('billing');
    if (returned) {
      open().then(() => { if (returned === 'success') message('Checkout has returned. Your balance updates when Stripe confirms payment. Refresh if it is still pending.'); });
    }
  });
})();
