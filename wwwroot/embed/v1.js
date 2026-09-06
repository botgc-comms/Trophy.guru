(() => {
  'use strict';
  const script = document.currentScript;
  if (!script || script.dataset.trophyLoaded === 'true') return;
  script.dataset.trophyLoaded = 'true';
  const club = script.dataset.club;
  if (!club || !/^[a-zA-Z0-9_-]{1,80}$/.test(club)) return;
  let serviceOrigin;
  try {
    // Set data-service-origin when this tiny loader is mirrored to a separate CDN hostname.
    const service = new URL(script.dataset.serviceOrigin || script.src, location.href);
    if (service.protocol !== 'https:' && !(service.protocol === 'http:' &&
        ['localhost', '127.0.0.1', '[::1]'].includes(service.hostname))) return;
    serviceOrigin = service.origin;
  } catch { return; }
  const boardUrl = new URL(`/embed/${encodeURIComponent(club)}`, serviceOrigin);
  boardUrl.searchParams.set('parentOrigin', location.origin);
  const frame = document.createElement('iframe');
  frame.src = boardUrl.href;
  frame.title = script.dataset.title || 'Club honours board';
  frame.loading = 'lazy';
  frame.referrerPolicy = 'no-referrer';
  frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox');
  frame.setAttribute('width', '100%');
  frame.setAttribute('height', '900');
  frame.style.border = '0';
  frame.style.display = 'block';
  const fallback = document.createElement('a');
  fallback.href = new URL(`/honours/${encodeURIComponent(club)}`, serviceOrigin).href;
  fallback.target = '_blank';
  fallback.rel = 'noopener';
  fallback.textContent = 'Open the honours board in a new window';
  script.before(frame, fallback);
  window.addEventListener('message', event => {
    if (event.source !== frame.contentWindow || event.origin !== serviceOrigin ||
        event.data?.type !== 'trophy-archive:embed-size' || event.data.clubId !== club ||
        typeof event.data.height !== 'number' || !Number.isFinite(event.data.height)) return;
    frame.height = String(Math.max(480, Math.min(16000, Math.ceil(event.data.height))));
  });
})();
