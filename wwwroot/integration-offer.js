(() => {
  'use strict';
  const offers = [...document.querySelectorAll('[data-ig-offer]')];
  if (!offers.length) return;
  fetch('/api/public/integrations/intelligent-golf', { credentials: 'omit', cache: 'no-store' })
    .then(response => response.ok ? response.json() : null)
    .then(offer => {
      if (!offer || typeof offer.available !== 'boolean' || offer.amountPence !== 29900 || offer.currency !== 'gbp' || offer.billingInterval !== 'year') return;
      for (const root of offers) {
        root.querySelector('[data-ig-status]').textContent = offer.available ? 'Annual subscription' : offer.status === 'coming_soon' ? 'In development' : 'Checkout unavailable';
        root.querySelector('[data-ig-unavailable]').hidden = Boolean(offer.available);
        root.querySelector('[data-ig-subscribe]').hidden = !offer.available;
        root.querySelector('[data-ig-availability]').textContent = offer.available
          ? '£299 billed annually. Installation is arranged after your club’s setup is confirmed.'
          : offer.status === 'coming_soon' ? 'The member integration is being prepared. No payment is taken yet.' : 'Annual checkout is temporarily unavailable. No payment can be taken.';
      }
    }).catch(() => { /* The stated price remains visible; checkout stays unavailable. */ });
})();
