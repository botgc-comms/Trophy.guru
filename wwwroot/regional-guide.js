(() => {
  'use strict';
  const paths = { uk: '/uk/how-to-catalogue-trophy-winners/', us: '/us/how-to-catalog-trophy-winners/' };
  // Respect an explicitly opened guide, including shared and bookmarked links.
  const explicit = location.pathname.startsWith('/us/') ? 'us' : location.pathname.startsWith('/uk/') ? 'uk' : null;
  function suggestedRegion() {
    let zone = '';
    try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch { /* Use language preference. */ }
    if (['Europe/London', 'Europe/Belfast', 'Europe/Guernsey', 'Europe/Jersey', 'Europe/Isle_of_Man'].includes(zone)) return 'uk';
    const languages = navigator.languages?.length ? navigator.languages : [navigator.language || ''];
    for (const language of languages) {
      const tag = language.toLowerCase();
      if (/-(gb|uk)(-|$)/.test(tag)) return 'uk';
      if (/-us(-|$)/.test(tag)) return 'us';
    }
    if (/^America\/(New_York|Chicago|Denver|Los_Angeles|Phoenix|Detroit|Anchorage|Adak|Boise|Juneau|Menominee|Metlakatla|Nome|Sitka|Yakutat)$/.test(zone) || /^America\/(Indiana|Kentucky|North_Dakota)\//.test(zone) || zone === 'Pacific/Honolulu') return 'us';
    return 'uk';
  }
  function regionForCountry(country) {
    const name = (country || '').toLowerCase().replace(/[^a-z]/g, '');
    if (['us', 'usa', 'unitedstates', 'unitedstatesofamerica'].includes(name)) return 'us';
    if (['uk', 'gb', 'gbr', 'unitedkingdom', 'greatbritain', 'england', 'scotland', 'wales', 'northernireland'].includes(name)) return 'uk';
    return null;
  }
  function update(country) {
    const region = explicit || regionForCountry(country) || suggestedRegion();
    document.querySelectorAll('[data-regional-guide]').forEach(link => {
      link.href = paths[region];
      link.textContent = 'Trophy guide';
      link.dataset.guideRegion = region;
    });
  }
  window.TrophyRegionalGuide = Object.freeze({ update });
  update();
})();
