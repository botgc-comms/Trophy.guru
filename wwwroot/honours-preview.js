(() => {
  'use strict';

  const root = document.querySelector('[data-honours-preview]');
  if (!root) return;
  const frame = root.querySelector('iframe');
  const stage = root.querySelector('.honours-preview-stage');
  const buttons = [...root.querySelectorAll('[data-preview-view]')];
  const pauseButton = root.querySelector('[data-preview-pause]');
  const error = root.querySelector('.honours-preview-error');
  const loading = root.querySelector('.honours-preview-loading');
  const motionPreference = matchMedia('(prefers-reduced-motion: reduce)');
  const views = ['year', 'trophy', 'person'];
  let selected = 'year';
  let ready = false;
  let visible = false;
  let hovered = false;
  let focused = false;
  let userPaused = false;
  let timer;

  function fitBoard() {
    const css = getComputedStyle(stage);
    const width = Number(css.getPropertyValue('--honours-demo-width')) || 1280;
    const height = Number(css.getPropertyValue('--honours-demo-height')) || 1140;
    frame.style.width = `${width}px`;
    frame.style.height = `${height}px`;
    frame.style.transform = `scale(${stage.clientWidth / width})`;
  }

  function showView(view) {
    selected = view;
    buttons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.previewView === view)));
    frame.contentWindow?.postMessage({ type: 'trophy-archive:preview-view', view }, location.origin);
    schedule();
  }

  function schedule() {
    clearTimeout(timer);
    if (!ready || !visible || hovered || focused || userPaused || document.hidden || motionPreference.matches) return;
    timer = setTimeout(() => showView(views[(views.indexOf(selected) + 1) % views.length]), 7000);
  }

  function updatePauseButton() {
    pauseButton.hidden = motionPreference.matches;
    pauseButton.textContent = userPaused ? 'Play tour' : 'Pause tour';
    pauseButton.setAttribute('aria-label', userPaused ? 'Play the honours board preview' : 'Pause the honours board preview');
  }

  buttons.forEach(button => button.addEventListener('click', () => showView(button.dataset.previewView)));
  pauseButton.addEventListener('click', () => {
    userPaused = !userPaused;
    updatePauseButton();
    schedule();
  });
  root.addEventListener('pointerenter', event => {
    if (event.pointerType !== 'mouse') return;
    hovered = true;
    schedule();
  });
  root.addEventListener('pointerleave', () => { hovered = false; schedule(); });
  root.addEventListener('focusin', () => { focused = true; schedule(); });
  root.addEventListener('focusout', event => {
    focused = root.contains(event.relatedTarget);
    schedule();
  });
  document.addEventListener('visibilitychange', schedule);
  motionPreference.addEventListener('change', () => { updatePauseButton(); schedule(); });
  window.addEventListener('message', event => {
    if (event.origin !== location.origin || event.source !== frame.contentWindow) return;
    if (['trophy-archive:preview-ready', 'trophy-archive:preview-view'].includes(event.data?.type)) {
      ready = true;
      root.dataset.ready = 'true';
      error.hidden = true;
      fitBoard();
      schedule();
    } else if (event.data?.type === 'trophy-archive:preview-error') {
      ready = false;
      root.dataset.ready = 'false';
      error.hidden = false;
      loading.hidden = true;
      schedule();
    }
  });
  frame.addEventListener('load', () => showView(selected));

  if ('IntersectionObserver' in window) {
    new IntersectionObserver(entries => {
      visible = entries[0].isIntersecting;
      schedule();
    }, { threshold: 0.15 }).observe(root);
  } else {
    visible = true;
  }
  if ('ResizeObserver' in window) new ResizeObserver(fitBoard).observe(stage);
  else window.addEventListener('resize', fitBoard);
  updatePauseButton();
  fitBoard();
  // Also covers a cached iframe whose load event preceded this deferred script.
  showView(selected);
})();
