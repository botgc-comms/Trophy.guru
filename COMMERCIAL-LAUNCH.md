# Trophy Archive AI — commercial launch plan

This document separates the product capabilities now present in this repository from the controls still required before charging public customers.

## Product and pricing

Use non-expiring trophy credits for the core digitisation workflow. A trophy cabinet is normally a finite project, so a forced monthly subscription adds friction without improving the customer's outcome.

| Offer | Customer price | Effective price | Intended use |
| --- | ---: | ---: | --- |
| First trophy | £0 | £0 | One account, two evidence images, one illustration, review and export |
| Single credit | £7.50 | £7.50 | A club testing a difficult or important trophy |
| Club pack | £60 | £6.00 | Ten trophies |
| Heritage pack | £225 | £4.50 | Fifty trophies |
| Cabinet pack | £875 | £3.50 | Two hundred and fifty trophies; sized for a realistic full-club collection |

Prices should be presented excluding VAT where applicable until the final tax treatment is confirmed. One credit should cover one trophy record, background inscription reading from its evidence set, one generated catalogue illustration, member matching and CSV export. Regeneration and unusually high evidence volume should have a documented fair-use limit so a faulty workflow cannot create unbounded AI spend.

The current AI and payment costs leave substantial room for storage, support and acquisition. Maintain a live cost ledger using actual OpenAI usage rather than relying on launch estimates. Give every account a hard daily spend cap, and alert before the OpenAI project budget is reached.

A recurring subscription is better reserved for a later Archive Care module: hosted public trophy pages, ongoing curation, additional administrators, API access, backups and publishing integrations. Intelligent Golf remains explicitly out of scope for this release.

## Entitlements

All limits must be enforced by the server, never only by the browser.

- One free entitlement per verified organisation/account, with two evidence images and one illustration generation.
- A paid credit is reserved atomically when a new trophy is created and consumed when the first billable AI job starts.
- Failed provider requests release the reservation; completed AI work consumes it.
- Idempotency keys prevent a retry or webhook replay from consuming a second credit.
- Account owners can see purchased, reserved and consumed credits in an immutable ledger.
- Support adjustments create ledger entries rather than silently changing a balance.

## Required production architecture

The present application is a successful single-club prototype: one JSON catalogue, one member directory, local disk files and one shared password. It must not be treated as a multi-customer payment service. Before accepting money, migrate to:

1. Render Postgres (or an equivalent managed relational database) with `organisation_id` on every trophy, winner, member, evidence, job and credit-ledger row.
2. Object storage with private objects, short-lived signed URLs, lifecycle deletion and an organisation prefix on every key. A single Render disk prevents safe horizontal scaling.
3. Individual accounts with verified email, secure password reset or managed sign-in, organisation invitations, owner/editor/viewer roles and session revocation.
4. Stripe Checkout for one-time credit packs, with the Stripe Customer ID attached to the organisation. Process `checkout.session.completed`, refunds and disputes through signature-verified, idempotent webhooks.
5. A durable background job queue. Engraving analysis and image generation must survive a web process restart, expose progress and never hold a request open for several minutes.
6. Per-organisation metering, throttling and an audit trail for data export, deletion, member imports, AI jobs and administrator actions.
7. Automated database backups, object-storage version/retention policy, restore drills, error monitoring and a documented incident process.

Every repository query and object lookup needs an automated cross-tenant isolation test. Never accept an organisation ID supplied by the client as authority; derive it from the authenticated membership.

## Data protection defaults

- Keep the current privacy-minimised import behaviour: derive birth year in memory and never persist the member spreadsheet or full date of birth.
- Let the organisation omit date of birth entirely. Matching still works with lower confidence and explains that limitation.
- Treat winner/member links as suggestions until a human confirms them; store the probability and rationale.
- Give owners configurable evidence-image retention, immediate member-directory removal, whole-account export and whole-account deletion.
- Define controller/processor roles, retention periods, subprocessors and international transfer terms in the privacy notice and data processing agreement.
- Do not use customer evidence, member data or winner records for advertising or model training.
- Run spreadsheet files through content validation and malware scanning before parsing them in the background.

Legal review, a privacy notice, terms of service, cookie position, VAT setup and appropriate company/contact details are launch gates, not placeholder copy.

## Website and acquisition

The home page should lead with the real problem and evidence-led workflow, as the new page does. After choosing the production domain and brand:

- add the canonical URL, absolute Open Graph URL, organisation details and a sitemap;
- connect Google Search Console and privacy-respecting analytics with conversion events for trial creation, first image, first confirmed winner and checkout;
- publish a Burton-on-Trent case study with real before/after evidence and the curator's review process;
- create useful, distinct pages for golf, rugby, cricket, tennis, bowls, schools and museums only when each has original examples and language;
- build an engraving photography guide covering glare, overlapping angles, macro focus, rubbings and data review;
- approach county sports associations, club-secretary networks, heritage groups, governing bodies and specialist website providers with live demonstrations;
- run small search campaigns around intent such as “digitise trophy inscriptions” and “trophy archive software”, then stop any channel whose paid conversion cost exceeds the contribution margin of the first pack.

Avoid thin location pages, invented testimonials and mass-generated SEO articles. The collection, evidence and verified results are the strongest marketing assets.

## Decisions and inputs still needed

- Final product name and domain.
- Legal business name, address, support email, VAT status and target launch countries.
- The exact illustration prompt/script; it can replace `TROPHY_ILLUSTRATION_PROMPT` without a code change.
- Whether illustrations may use up to four or more source angles, and how many regenerations a credit includes.
- Stripe account and product/price IDs.
- Authentication provider and transactional email provider.
- Default image, member-directory and inactive-account retention periods.
- Final century-pack support promise and refund policy.

## Release gates

- [x] Multi-photo background engraving analysis.
- [x] Manual winner/year correction and missing-year workflow.
- [x] New trophy records.
- [x] Multi-angle AI illustration endpoint and UI.
- [x] CSV/TSV/XLSX member import with birth-year minimisation.
- [x] Age-aware probabilistic matching and enriched CSV export.
- [x] Commercial landing-page and pricing prototype.
- [ ] Multi-tenant database and object storage.
- [ ] Individual accounts, organisations and roles.
- [ ] Stripe Checkout, credit ledger and webhooks.
- [ ] Enforced free/paid entitlements and provider spend caps.
- [ ] Legal pages, retention/deletion controls and production contact details.
- [ ] Cross-tenant, billing, recovery, abuse and mobile acceptance tests.

Do not remove the final six gates merely to meet a date. They are what turns the current club tool into a service customers can safely pay for.
