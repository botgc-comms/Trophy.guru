(() => {
  'use strict';
  if (window.top !== window || typeof document.modelContext?.registerTool !== 'function') return;
  const definitions = [
    ['get_trophy_guru_overview', 'Explain software for digitising engraved trophy records and historical winners into electronic honours boards for golf clubs and sporting organisations.', data => ({ description: data.overview, producer: data.company, url: 'https://trophy.guru/about' })],
    ['get_how_it_works', 'Explain the public workflow from trophy photographs and engraved plates to reviewed winner records and an online honours board. Does not upload or publish records.', data => ({ steps: data.workflow, url: 'https://trophy.guru/how-it-works' })],
    ['get_frequently_asked_questions', 'Read answers about historic trophy digitisation, human review, engraved plates and electronic honours boards. Public information only.', data => ({ questions: data.faqs, url: 'https://trophy.guru/faq' })],
    ['get_demo_links', 'Find the illustrative honours board and signup page. Returns links without submitting a demo request or creating an account.', data => ({ demoUrl: data.demoUrl, signupUrl: data.signupUrl })]
  ];
  for (const [name, description, select] of definitions) {
    Promise.resolve().then(() => document.modelContext.registerTool({
      name, description,
      inputSchema: { type: 'object', properties: {}, additionalProperties: false },
      annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
      execute: async () => {
        const response = await fetch('/api/public/product', { credentials: 'omit', signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error('Public product information is temporarily unavailable.');
        return select(await response.json());
      }
    })).catch(() => { /* Unsupported registration must not disrupt the normal site. */ });
  }
})();
