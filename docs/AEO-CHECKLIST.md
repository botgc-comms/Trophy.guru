# AEO implementation checklist

## Implemented automatically

- [x] Inspect architecture, route families, rendering, metadata, private APIs, analytics and deployment; record baseline in AEO-SEO-AUDIT.md.
- [x] Clear homepage category H1 and factual introductory copy; retain existing design, real pricing and workflows.
- [x] Seven server-rendered guides: electronic honours boards, golf clubs, trophy digitisation, workflow, FAQ, About, privacy/security. Shared content supplies public agent answers.
- [x] Unique titles, descriptions, canonical and social metadata; improve existing privacy/blog metadata. Add descriptive links and breadcrumbs.
- [x] Stable Organization, WebSite, WebApplication and Service graph; visible FAQ-backed FAQPage and BreadcrumbList. Keep real homepage pricing Offers. No invented reviews, awards, profiles or prices.
- [x] Explicit OpenAI/Google/Bing crawler rules, shared private exclusions, broader private response noindex headers. Preserve authentication and public-board publication controls.
- [x] Generated sitemap includes 13 public pages plus actual published blog articles; only reliable blog update dates become lastmod. No private or account pages included.
- [x] Generated `/llms.txt` links the authoritative public pages. No redundant full-text export.
- [x] IndexNow environment key, dynamically served ownership proof, opt-in deployment/CMS change monitor with persistent content digest, and updated manual submit script.
- [x] Four safe WebMCP tools and official-SDK remote MCP endpoint. No private data, uploads, purchases, messages or publication actions.
- [x] Consent-gated GA4 ChatGPT campaign attribution, public landing-page paths and signup-intent event; mocked-network analytics tests.
- [x] Intrinsic dimensions added to homepage images missing them; new pages reuse existing brand assets and require no client-side rendering framework.
- [x] Tests cover server HTML, canonical metadata, JSON-LD parsing and FAQ agreement, XML sitemap, robots, llms, private exclusions, invalid URLs, public links, mobile overflow, WebMCP calls and remote MCP transport/security.

## Verification results

Final checks on 10 September 2026:

- Release build/publish succeeded; `outputs/aeo-release` excludes QA output and private working directories.
- **242/242 .NET tests passed**, including the existing account, publication, billing, blog and security regressions.
- **25 JavaScript files passed syntax checks**; `git diff --check` passed. There is no separate configured lint suite.
- Existing search-discovery smoke: **18 checks passed**, covering GET/HEAD, real pricing/schema agreement, hreflang, crawler access, canonical aliases, private exclusions and missing pages.
- New AEO smoke passed against the **actual published build**: all 13 public content routes, XML sitemap, robots, llms, canonical/social metadata, parsed JSON-LD, private noindex, invalid blog pagination, key verification, internal links, 390px/1440px layouts, four WebMCP executions and remote MCP handshake/list/calls/errors/origin/body limits.
- Mocked-network GA4 tests passed: no events before consent, ChatGPT campaign preservation, dropped sensitive/unrecognized query fields, public signup-click intent and no archive measurement.
- Existing functional, security, integration and publication browser suites passed. These cover signup/verification, private API isolation, cross-origin rejection, oversized requests, draft limits, export safety, publication/preview/withdrawal, checkout gating, password reset and desktop/mobile UI behaviour.
- Existing publication browser assertions were updated for the already-implemented uppercase winner-name formatter and the intentionally changed homepage copy. No customer behaviour was changed to satisfy a stale assertion.

Screenshots and extracted metadata are in `outputs/aeo/`; priority values are committed as AEO-PAGE-METADATA.md. The mobile homepage and new guide were visually inspected. All data used for functional QA was created in isolated temporary directories; external AI, billing, SMTP and IndexNow were disabled. QA processes were stopped after testing. These are local QA results, not production measurements. Core Web Vitals, actual search indexing and provider-side plugin acceptance require production evidence.

## Requires manual/external action

- [ ] Review and deploy the changes through the existing Render deployment process.
- [ ] Verify production HTTPS, headers, all canonical routes, static resources and hosting firewall access.
- [ ] Verify Google Search Console ownership and submit the sitemap.
- [ ] Verify Bing Webmaster Tools ownership and submit the sitemap; monitor AI Performance where available.
- [ ] Generate/store `INDEXNOW_KEY` and enable `INDEXNOW_ENABLED` after deployment; check receipt logs.
- [ ] Set up GA4 AI-referral exploration and optionally mark `signup_click` as a key event. It is intent, not completed registration/demo conversion.
- [ ] Test the public MCP endpoint from the owner's ChatGPT/Codex account; complete the plugin identity, legal/support material, review and publication steps in OPENAI-PLUGIN.md.
- [ ] Approve and publish complete service privacy/terms/support documentation; repository drafts are not asserted to be final policies.
- [ ] Obtain genuine customer case studies, independent golf-industry mentions and company/social profiles if desired. None were invented.
- [ ] Monitor real user performance, crawl logs, indexed pages, referral quality and citations after launch. No ranking or citation outcome is guaranteed.
