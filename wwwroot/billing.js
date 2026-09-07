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
    const logo = node('img', undefined, 'integration-brand-logo'); logo.src = '/images/partners/intelligentgolf.png'; logo.alt = 'intelligentgolf';
    copy.append(logo);
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
      const basis = state.upgradeBasis;
      const pending = state.purchases.find(p => p.state === 'pending' && p.packCode === pack.code && p.upgradeFrom?.startsWith('balance:'));
      const quoteFor = count => {
        const full = pack.code === 'complete' ? count * (pack.amountPence / pack.credits) : pack.amountPence;
        const upgrade = basis && !basis.pending && basis.credits > 0 && basis.credits < count;
        return { amount: upgrade ? (count - basis.credits) * (pack.amountPence / pack.credits) : full, credits: upgrade ? count - basis.credits : count, from: upgrade ? basis.upgradeFrom : null };
      };
      let count = pack.credits;
      const total = node('strong');
      const explanation = node('p');
      const buy = button('Buy credits', () => {
        if (pending) { redirect('/api/billing/checkout', {packCode: pending.packCode, requestId: pending.requestId, upgradeFrom: pending.upgradeFrom}); return; }
        const quote = quoteFor(count);
        checkout(pack.code, quote.from, pack.code === 'complete' ? count : undefined);
      }, enabled);
      const update = () => {
        if (pending) { total.textContent = money(pending.amountPence); buy.textContent = 'Continue upgrade'; buy.disabled = !enabled; explanation.textContent = 'Complete your pending purchase of ' + pending.credits + ' additional credits.'; return; }
        const valid = Number.isInteger(count) && count >= pack.credits && count <= 2147483647;
        buy.disabled = !enabled || !valid;
        if (!valid) { total.textContent = 'Enter 150 or more'; explanation.textContent = ''; return; }
        const quote = quoteFor(count);
        total.textContent = money(quote.amount);
        buy.textContent = quote.from ? 'Upgrade to ' + count : 'Buy credits';
        explanation.textContent = quote.from ? 'Add ' + quote.credits + ' credits at ' + money(pack.amountPence / pack.credits) + ' each. You have already purchased ' + basis.credits + ' credits.' : 'VAT included. One-off purchase. Credits do not expire.';
      };
      card.append(node('h3', pack.code === 'complete' ? '150 or more trophies' : pack.credits + ' trophy ' + (pack.credits === 1 ? 'credit' : 'credits')));
      if (pack.code === 'complete') {
        const quantity = node('input'); quantity.type = 'number'; quantity.min = '150'; quantity.step = '1'; quantity.value = '150'; quantity.id = 'volume-trophy-quantity'; quantity.max = '2147483647'; quantity.required = true; quantity.disabled = Boolean(pending);
        const label = node('label', 'Total trophies'); label.htmlFor = quantity.id;
        quantity.addEventListener('input', () => { count = Number(quantity.value); update(); });
        total.id = 'volume-trophy-total'; card.append(label, quantity);
      }
      const rate = money(pack.amountPence / pack.credits);
      const saving = Math.round((1 - pack.amountPence / pack.credits / 750) * 100);
      card.append(total, node('p', rate + ' per trophy' + (saving ? ' · Save ' + saving + '%' : '')), explanation, buy);
      update(); packs.append(card);
    }
    mount.append(packs);
    if (state.upgradeBasis?.credits) mount.append(node('p', 'Upgrade prices charge only for the additional credits, at the selected pack’s per-credit rate. Used credits stay used; your free first trophy is separate. All prices include VAT.'));
    renderIntegration(mount);
    mount.append(node('p', 'One credit is permanently linked to one trophy. All future edits, photo readings and trophy illustrations are included. Retrying an interrupted request never uses another credit.', 'billing-explanation'));
    mount.append(node('p', 'Archive storage is limited to keep the service reliable. The standard allowance is 256 MiB for a free archive and 2 GiB for a paid archive; contact support for larger collections. Existing records remain available when an allowance is reached.'));
    if (state.purchases.length) {
      mount.append(node('h3', 'Recent purchases'));
      const history = node('ul', undefined, 'billing-history');
      for (const purchase of state.purchases.slice(0, 10)) history.append(node('li', `${purchase.credits} credits · ${money(purchase.amountPence)} · ${purchase.state}`));
      mount.append(history);
    }
    if (state.portalAvailable) mount.append(button('Manage payments and subscriptions', () => redirect('/api/billing/portal', {}), state.owner && state.emailVerified));

  }
  async function refresh() {
    try {
      state = await api('/api/billing');
      for (const purchase of state.purchases) {
        if (purchase.state === 'pending') continue;
        for (let index = sessionStorage.length - 1; index >= 0; index--) {
          const key = sessionStorage.key(index);
          if (key?.startsWith('trophy-checkout:') && sessionStorage.getItem(key) === purchase.requestId) sessionStorage.removeItem(key);
        }
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
  async function canAddTrophy() {
    const current = await refresh();
    if (!current) {
      await open();
      return false;
    }
    if (current.balance.unlimited || current.balance.available > 0) return true;
    await open();
    const explanation = node('p', 'You have no unused trophy credits. Buy more credits to add another trophy. Future edits to your existing credited trophies are already included.', 'billing-message');
    explanation.setAttribute('role', 'status');
    document.querySelector('#billing-panel')?.prepend(explanation);
    return false;
  }
  window.TrophyBilling = { open, refresh, canAddTrophy };
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  document.addEventListener('DOMContentLoaded', async () => {
    const params = new URLSearchParams(location.search);
    if (params.get('billing') !== 'success') return;
    const purchaseId = params.get('purchase');
    const notice = node('p', 'Confirming your payment and updating your credits…', 'security-verification-banner');
    notice.id = 'payment-return-notice'; notice.setAttribute('role', 'status');
    document.querySelector('.catalogue-heading')?.after(notice);
    document.querySelector('#plan-dialog')?.close();
    history.replaceState(null, '', location.pathname + location.search + '#catalogue');
    for (let attempt = 0; attempt < 30; attempt++) {
      const current = await refresh();
      const confirmed = current && (purchaseId ? current.purchases.some(p => p.id === purchaseId && p.state === 'paid') : current.purchases.some(p => p.state === 'paid') && !current.purchases.some(p => p.state === 'pending'));
      if (confirmed) {
        notice.textContent = 'Payment confirmed. You now have ' + current.balance.available + ' trophy ' + (current.balance.available === 1 ? 'credit' : 'credits') + ' available.';
        history.replaceState(null, '', location.pathname + '#catalogue');
        return;
      }
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
    notice.textContent = 'Your payment is still awaiting confirmation. Your credits will appear once Stripe confirms it; please do not pay again.';
  });
})();
