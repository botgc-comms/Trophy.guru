(() => {
  'use strict';

  // The embedded fictional board is part of its parent's page, which owns consent.
  if (window.parent !== window && location.pathname.toLowerCase() === '/honours.html' &&
      new URLSearchParams(location.search).get('demo') === '1') return;

  const measurementId = 'G-8GMHWE0WLH';
  const consentKey = 'trophyGuru.analyticsConsent.v1';
  const productionHost = location.protocol === 'https:' &&
    (location.hostname === 'trophy.guru' || location.hostname.endsWith('.trophy.guru'));
  const eventSchemas = {
    login: { method: ['password'] },
    sign_up: { method: ['password'] },
    trophy_created: {},
    evidence_uploaded: { item_count: 'count' },
    winner_confirmed: { entry_type: ['new', 'existing'] },
    honours_published: {},
  };

  let analyticsEnabled = false;
  let tagInitialised = false;
  let sessionConsent = null;
  let banner;
  let settingsButton;
  let lastPagePath = '';
  let focusBeforeDialog = null;

  window.trophyAnalytics = Object.freeze({
    track,
    pageView: sendPageView,
    openSettings,
    consent: () => readConsent(),
  });

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialise, { once: true });
  } else {
    initialise();
  }

  function initialise() {
    buildConsentControls();
    const consent = readConsent();
    if (consent === 'granted') {
      enableAnalytics();
    } else if (consent !== 'denied') {
      showBanner();
    }
    window.addEventListener('hashchange', sendPageView);
    window.addEventListener('popstate', sendPageView);
  }

  function buildConsentControls() {
    if (document.querySelector('#analytics-consent')) return;

    settingsButton = document.createElement('button');
    settingsButton.type = 'button';
    settingsButton.className = 'analytics-settings-button';
    settingsButton.textContent = 'Privacy & cookie settings';
    settingsButton.addEventListener('click', openSettings);

    banner = document.createElement('section');
    banner.id = 'analytics-consent';
    banner.className = 'analytics-consent';
    banner.setAttribute('role', 'dialog');
    banner.setAttribute('aria-labelledby', 'analytics-consent-title');
    banner.hidden = true;
    banner.innerHTML = `
      <div class="analytics-consent__copy">
        <p class="analytics-consent__eyebrow">Your privacy choice</p>
        <h2 id="analytics-consent-title">Help us improve Trophy.guru?</h2>
        <p>With your permission, Google Analytics tells us which pages and features are useful. We do not send member names, club records, uploaded images, email addresses or account details.</p>
        <a href="/privacy.html">Read how analytics and cookies are used</a>
      </div>
      <div class="analytics-consent__actions">
        <button type="button" data-consent="denied">Reject analytics</button>
        <button type="button" data-consent="granted">Accept analytics</button>
      </div>`;
    banner.addEventListener('click', event => {
      const choice = event.target.closest('[data-consent]')?.dataset.consent;
      if (choice) setConsent(choice);
    });
    banner.addEventListener('keydown', event => {
      if (event.key !== 'Escape' || readConsent() === null) return;
      hideBanner();
    });

    document.body.append(settingsButton, banner);
  }

  function openSettings() {
    focusBeforeDialog = document.activeElement;
    showBanner();
  }

  function showBanner() {
    if (!banner) return;
    banner.hidden = false;
    settingsButton.hidden = true;
    window.requestAnimationFrame(() => banner.querySelector('button')?.focus());
  }

  function hideBanner() {
    if (!banner) return;
    banner.hidden = true;
    settingsButton.hidden = false;
    if (focusBeforeDialog instanceof HTMLElement) focusBeforeDialog.focus();
    focusBeforeDialog = null;
  }

  function setConsent(choice) {
    if (!['granted', 'denied'].includes(choice)) return;
    sessionConsent = choice;
    try {
      localStorage.setItem(consentKey, choice);
    } catch { }

    if (choice === 'granted') {
      enableAnalytics();
    } else {
      disableAnalytics();
    }
    hideBanner();
  }

  function readConsent() {
    if (sessionConsent) return sessionConsent;
    try {
      const choice = localStorage.getItem(consentKey);
      return ['granted', 'denied'].includes(choice) ? choice : null;
    } catch {
      return null;
    }
  }

  function enableAnalytics() {
    if (!productionHost || analyticsEnabled || readConsent() !== 'granted') return;
    analyticsEnabled = true;
    window.dataLayer = window.dataLayer || [];
    window.gtag = window.gtag || function gtag() { window.dataLayer.push(arguments); };
    const safePage = pageDetails(location.pathname, location.hash);
    if (!tagInitialised) {
      tagInitialised = true;
      window.gtag('consent', 'default', consentState('denied'));
      window.gtag('consent', 'update', consentState('granted'));
      window.gtag('set', {
        page_location: `${location.origin}${safePage.path}`,
        page_referrer: safeReferrer(),
        page_title: safePage.title,
      });
      window.gtag('js', new Date());
      window.gtag('config', measurementId, {
        allow_google_signals: false,
        allow_ad_personalization_signals: false,
        send_page_view: false,
      });

      const script = document.createElement('script');
      script.async = true;
      script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
      document.head.append(script);
    } else {
      window.gtag('consent', 'update', consentState('granted'));
    }
    sendPageView();
  }

  function disableAnalytics() {
    if (typeof window.gtag === 'function') {
      window.gtag('consent', 'update', consentState('denied'));
    }
    analyticsEnabled = false;
    lastPagePath = '';
    removeAnalyticsCookies();
  }

  function consentState(analyticsStorage) {
    return {
      analytics_storage: analyticsStorage,
      ad_storage: 'denied',
      ad_user_data: 'denied',
      ad_personalization: 'denied',
      functionality_storage: 'denied',
      personalization_storage: 'denied',
      security_storage: 'granted',
    };
  }

  function sendPageView() {
    if (!analyticsEnabled || readConsent() !== 'granted' || typeof window.gtag !== 'function') return;
    const page = pageDetails(location.pathname, location.hash);
    if (page.path === lastPagePath) return;
    lastPagePath = page.path;
    const pageLocation = `${location.origin}${page.path}`;
    window.gtag('set', { page_location: pageLocation, page_title: page.title });
    window.gtag('event', 'page_view', {
      page_location: pageLocation,
      page_path: page.path,
      page_referrer: safeReferrer(),
      page_title: page.title,
    });
  }

  function track(eventName, parameters = {}) {
    const schema = eventSchemas[eventName];
    if (!schema || readConsent() !== 'granted') return;
    if (!analyticsEnabled) enableAnalytics();
    if (!analyticsEnabled || typeof window.gtag !== 'function') return;
    window.gtag('event', eventName, sanitiseParameters(schema, parameters));
  }

  function sanitiseParameters(schema, values) {
    return Object.fromEntries(Object.entries(schema).flatMap(([name, rule]) => {
      const value = values[name];
      if (rule === 'count') {
        const count = Number(value);
        return Number.isInteger(count) && count > 0 && count <= 30 ? [[name, count]] : [];
      }
      return Array.isArray(rule) && rule.includes(value) ? [[name, value]] : [];
    }));
  }

  function pageDetails(pathname, hash) {
    const path = pathname.replace(/\/+$/, '') || '/';
    const route = String(hash || '').replace(/^#/, '').split('/')[0].toLowerCase();

    if (path === '/archive.html' || path === '/archive') {
      const safeRoute = ['signup', 'login', 'catalogue', 'trophy'].includes(route) ? route : 'catalogue';
      return { path: `/archive/${safeRoute}`, title: archiveTitle(safeRoute) };
    }
    if (path === '/honours' || path.startsWith('/honours/')) {
      const safeRoute = ['year', 'trophy', 'person'].includes(route) ? route : 'overview';
      return { path: `/honours/${safeRoute}`, title: honoursTitle(safeRoute) };
    }
    if (path === '/uk/how-to-catalogue-trophy-winners') {
      return { path: '/uk/how-to-catalogue-trophy-winners/', title: 'UK trophy cataloguing guide' };
    }
    if (path === '/us/how-to-catalog-trophy-winners') {
      return { path: '/us/how-to-catalog-trophy-winners/', title: 'US trophy cataloging guide' };
    }
    if (path === '/privacy.html' || path === '/privacy') {
      return { path: '/privacy', title: 'Privacy and cookies' };
    }
    if (path === '/' || path === '/index.html') {
      return { path: '/', title: 'Trophy Archive AI' };
    }
    return { path: '/other', title: 'Trophy Archive AI' };
  }

  function archiveTitle(route) {
    return ({
      signup: 'Create a Trophy Archive account',
      login: 'Sign in to Trophy Archive',
      catalogue: 'Trophy catalogue',
      trophy: 'Review a trophy',
    })[route];
  }

  function honoursTitle(route) {
    return ({
      overview: 'Published honours board',
      year: 'Honours by year',
      trophy: 'Honours by trophy',
      person: 'Honours by person',
    })[route];
  }

  function safeReferrer() {
    if (!document.referrer) return '';
    try {
      const referrer = new URL(document.referrer);
      if (referrer.origin !== location.origin) return `${referrer.origin}/`;
      const page = pageDetails(referrer.pathname, referrer.hash);
      return `${referrer.origin}${page.path}`;
    } catch {
      return '';
    }
  }

  function removeAnalyticsCookies() {
    const cookieNames = document.cookie.split(';')
      .map(cookie => cookie.split('=')[0].trim())
      .filter(name => name === '_ga' || name.startsWith('_ga_'));
    const hostname = location.hostname;
    const parentDomain = hostname.endsWith('.trophy.guru') || hostname === 'trophy.guru' ? '.trophy.guru' : null;
    for (const name of cookieNames) {
      document.cookie = `${name}=; Max-Age=0; path=/; SameSite=Lax`;
      document.cookie = `${name}=; Max-Age=0; path=/; domain=${hostname}; SameSite=Lax`;
      if (parentDomain) document.cookie = `${name}=; Max-Age=0; path=/; domain=${parentDomain}; SameSite=Lax`;
    }
  }
})();
