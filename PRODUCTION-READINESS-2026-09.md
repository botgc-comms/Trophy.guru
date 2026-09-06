# Trophy.guru: UK launch readiness and integration plan

Assessment date: 6 September 2026. Initial audience: UK clubs and organisations, confirmed by the owner. This is an implementation proposal and source-code audit, not confirmation that the service is production ready. No payment, publication, access-control or customer-data behaviour was changed during this assessment. The previous homepage work is separate.

## Implementation update

The subsequent authorised implementation added private-by-default frozen publication, generic link/iframe/script sharing, owner/editor access, verified-email recovery and invitations, session revocation, an additive SQLite credit/payment/job ledger, Stripe test/live configuration gates, durable queues and offline backup/restore tools. Existing archive JSON and identity paths were preserved; no real payment, email or production deployment was performed. The local original archive was hash-verified and backed up separately. See [LAUNCH-RUNBOOK.md](LAUNCH-RUNBOOK.md) for implemented behaviour, test commands and remaining launch gates. The findings below describe the code before these changes and are retained as assessment context.

Managed Postgres/object storage remains a future scaling option. The implemented pilot keeps archive JSON and adds a transactional SQLite operational database on the existing persistent disk, with one-writer enforcement. This avoids a destructive customer-data migration while providing atomic billing and persistent jobs. It requires production backup and capacity evidence; it is not a multi-instance deployment design.

## Recommended product shape

Sell non-expiring trophy credits for the core archive. Offer optional, recurring website-integration services, with installation charged separately when manual CMS work is needed. Keep the hosted honours board and ordinary sharing included in the core offer already advertised. An integration subscription funds compatibility, synchronisation and support. Its cancellation must not erase purchased archive records or consume remaining credits.

Use one honours-board product with a shared renderer, a small CDN loader, and thin platform adapters. Per-club branding, domains and entitlements belong in configuration. Do not build and maintain a separate application for every club or CMS.

## Pre-implementation findings (historical audit)

| Area | Current implementation | Required before launch |
| --- | --- | --- |
| Accounts | Individual password-hashed accounts and club-scoped stores exist | Verified email, recovery, owner/editor roles, invitations, session revocation, abuse controls |
| Public board | Every complete club can expose confirmed winner records via the public API | Explicit publication, private default for new clubs, approved public names, withdrawal |
| Identity | Public names/person grouping may use automatic member-match suggestions without checking approval | Separate transcription confirmation, approved member identity, and publication decisions |
| Embedding | Production boards deny framing; the homepage demo is a separate same-origin exception | Customer embed route, per-club allowed domains, shared access policy |
| Billing | Prices, balance and upgrade calculations are UI prototypes; checkout is disabled | Actual payment processing, club-owned credit ledger, server enforcement |
| AI jobs | Background queues and some startup recovery exist | Durable job IDs, reservation linkage, duplicate protection and cost limits |
| Persistence | JSON files, process-local locks and one persistent disk | Transactional billing storage, recoverable jobs and tested backup/restore; managed database/object storage are the recommended foundation |
| Privacy | Current page explains Google Analytics and cookies | Service notice, club DPA, publication notice, retention/deletion, terms |

The older COMMERCIAL-LAUNCH.md is stale where it calls the service a single-club/shared-password prototype. Its missing payment, recovery and compliance gates remain relevant.

Evidence: EntryPoint.cs public routes around lines 327–416 and public-name helpers around 1097; Domain/Models.cs account balance fields; wwwroot/app.js around 408–436; wwwroot/privacy.html; Services/AccountStore.cs; Services/BackgroundIllustrationQueue.cs. These are local source findings, not a penetration test or verification of deployed configuration.

### First privacy/accuracy fix

A confirmed inscription can currently be associated automatically with a merely possible member match. PublicWinnerName then prefers that match's full member name, and PublicPersonId groups records using the suggested identity. Thus approving an engraving can result in an unapproved identity becoming public.

Add separate fields/states for transcription review, identity approval and publication. Default the public name to the club-approved inscription/display name; never enrich it from an unapproved directory match. Review free-text descriptions as part of publication. Keep DOB, member identifiers, private evidence and internal matching data out of every public projection. Initials reduce disclosure but are still potentially identifying.

Provide new clubs with Private, Public and Members-only board modes. Members-only must not be selectable as a working feature until its server authentication is implemented. Migrate existing public boards deliberately with owner communication; do not silently reinterpret existing confirmation as publication consent. Enforce the same policy on HTML, JSON, images and any export route. Invalidate controlled caches when a club withdraws publication. Noindex and hard-to-guess links are not confidentiality controls.

## Sharing with minimal installation

| Method | Installed on the customer's site | Operation |
| --- | --- | --- |
| Link | One menu item or link | Opens the hosted board; follows that board's access policy |
| Plain iframe | One HTML element, if the CMS permits it | Central updates, no customer JavaScript required; less flexible sizing |
| CDN loader | A mount element plus one script reference | Creates/resizes the board, applies club branding and loads an optional adapter |
| Managed Intelligent Golf integration | The same small installation plus per-club setup | Compatibility, optional trusted member identity or protected data synchronisation |

Original proposal snippet (use the implemented loader snippet in LAUNCH-RUNBOOK.md):

```html
<div data-trophy-board="CLUB_ID"></div>
<script src="https://cdn.trophy.guru/embed/v1.js" defer></script>
```

Use an iframe for the normal live embed to isolate club CSS and minimise third-party script privileges. Keep the bootstrap small, use controlled release channels, staged rollouts and rollback. An approved-domain list can restrict framing, but does not authenticate viewers. CSP frame-ancestors controls permitted embedding parents. [MDN](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors)

### Intelligent Golf: the identity boundary

The existing SiteContent implementation already loads JavaScript/CSS through Azure Front Door. Its trophy page is configured as restricted. The publisher uses a server-held Intelligent Golf administrative session to update the CMS and skips unchanged content by hash. That is a useful installation mechanism, but the administrative session says nothing about the member viewing the resulting page.

Current browser code reads window.userID/window.properties. These can support convenience features within data a visitor already has permission to see. They cannot authorise a remote request, administrative action or disclosure of otherwise private records. Neither an Origin header, a public website key, nor a backend signing a browser-supplied member number fixes that problem.

The preferred live private flow is:

1. Intelligent Golf or a supported server integration verifies its member session.
2. It issues a short-lived assertion or authorisation code for the correct club and viewer.
3. Trophy.guru validates the trusted issuer, audience, expiry, club and replay protection.
4. Trophy.guru grants a narrowly scoped, read-only viewer session. Administrative accounts remain separate.
5. All protected API/media requests enforce this scope. Identity is used for 'My honours' only with a reliable, approved person mapping.

No supported Intelligent Golf SSO/signed-identity facility was established in this investigation. Confirm vendor capabilities and terms through a pilot; do not sell seamless authenticated integration before proving it. Avoid transferring customer member passwords or browser cookies to Trophy.guru. Cross-site cookie restrictions also make the current SameSite=Strict admin cookie unsuitable for embedded viewers. [MDN](https://developer.mozilla.org/en-US/docs/Web/Privacy/Guides/Third-party_cookies)

If that trust bridge is unavailable, there is a practical alternative: synchronise the approved honours dataset into the actual Intelligent Golf members-only page, and load only generic rendering code from the CDN. The CMS then protects the data itself. Update that data when the club publishes changes; application code remains centrally maintained. Verify that all dataset/media endpoints really require membership, that the CMS accepts the payload, and that withdrawal removes controlled copies. This option trades immediate live data for synchronisation and CMS-size constraints. Its subscription should clearly cover synchronisation/support; stopping it does not magically revoke copies already delivered.

If neither integration is available, offer a hosted members-only board using approved member invitations and email sign-in links, or a public board where the club has deliberately chosen publication. A shared board password can be an optional fallback but has weak individual revocation and creates the friction the owner wants to avoid.

### Existing BOTGC publisher: separate review needed

Source review found the older BOTGC.API website middleware accepts an allowed Origin and a website key shipped in client JavaScript, without verifying the member. Trophy write/delete actions appear to depend on that mechanism. The /api/cms/pages prefix is also exempted from API-key middleware while local publishing actions have no visible controller/action authorisation. These are serious local-code findings, distinct from Trophy.guru; production may have additional external controls that were not verified. Do not reuse the publisher for paying customers until the actual deployment is reviewed and server authorisation is established. No live exploit or publication was attempted.

Relevant files: Services/BOTGC.SiteContent/trophy-winners.md and assets/botgc-co-uk/scripts/trophy-winners.js; Services/BOTGC.API/Common/ClubWebsiteMiddleware.cs; Controllers/TrophiesController.cs; Controllers/CmsController.cs; Program.cs; Services/QueryHandlers/UpdateCmsPageHandler.cs.

## UK privacy and legal position

Treat living winners' names/initials combined with club, trophy and year as personal data. A common surname alone may be ambiguous, but context and linked history can identify someone. A hash or initials do not make the service anonymous. [ICO identifiability guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/personal-information-what-is-it/what-is-personal-data/what-are-identifiers-and-related-factors/)

Publication may be supportable under ordinary legitimate interests after the club assesses necessity, reasonable expectations and impacts. This is a proposed basis to assess, not automatic permission. An online searchable record is a wider disclosure than a trophy in a clubhouse. Make correction, objection and withdrawal practical; review juniors and unusual/sensitive descriptions separately. [ICO legitimate interests](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/lawful-basis/legitimate-interests/what-is-the-legitimate-interests-basis/)

A basic honours list does not automatically trigger a DPIA. The planned service includes AI and matching personal data from multiple sources, which the ICO specifically identifies in its DPIA criteria. Treat a DPIA for that combined workflow as a pre-launch requirement, with controller-specific assessment and UK privacy review. It is normally retained by the controller, not sent to the ICO for routine approval; unresolved high residual risk can require consultation. [ICO DPIA criteria](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/data-protection-impact-assessments-dpias/when-do-we-need-to-do-a-dpia/)

The likely role split is that clubs control the purposes/publication of their historical and membership records, while Trophy.guru processes them under club instructions. Trophy.guru separately controls its business account, security and billing administration. Confirm this allocation in practice; it is not determined solely by a contract label. [ICO roles](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/controllers-and-processors/controllers-and-processors/)

Prepare this service-specific document set:

- Public service privacy notice and accurate cookie notice.
- UK organisation terms: who contracts, fees/VAT, credits and regeneration limits, upgrade/refund rules, recurring renewals/cancellation, customer content rights, AI limitations, availability, termination/export/deletion and proportionate liability terms.
- Article 28 data processing agreement, including instructions, confidentiality, security, subprocessors, rights requests, incidents, assistance, deletion/return and audit information. [ICO processor contracts](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/contracts-and-liabilities-between-controllers-and-processors-multi/responsibilities-and-liabilities-for-controllers-using-a-processor/)
- Club-facing honours publication wording, LIA template and DPIA/data-flow pack.
- Retention schedule, subprocessor list, incident process and transfer assessment where applicable. Frankfurt hosting alone does not settle all downstream transfers. [ICO transfers](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/international-transfers/)

Include existing winner records sent with AI reading prompts, gender in optional member matching, and Google Fonts network requests in the data-flow inventory. Hosting fonts with the app would remove that separate third-party request.

Do not promise that a privacy notice alone establishes compliance. The publication controls, deletion/retention behaviour and supplier contracts must match it. Confirm legal operator/contact, VAT position, launch-country scope and any ICO fee obligations. The accompanying privacy notice is an unpublished draft with explicit outstanding fields.

## Payments, credits and subscriptions

Use Stripe-hosted Checkout for one-time purchases and initial subscriptions. Use Stripe Billing plus its customer portal for recurring integrations, payment details, invoices and cancellation. Trophy.guru remains responsible for its own tax/commercial decisions and entitlements. [Stripe customer portal](https://docs.stripe.com/customer-management)

Store a Stripe customer against the club, not whichever administrator first buys. Owners/billing managers can purchase; viewers cannot. Choose prices on the server from a maintained catalogue, validate club/order identity, currency and expected amounts, and never accept a browser-supplied price as authoritative.

Maintain an append-only club ledger of purchases, free grants, reservations, consumption, releases, refunds and support adjustments. Atomically reserve before billable work; settle consumption on the first successful billable result. If one AI component succeeds and another fails, keep that success linked to the same trophy/credit rather than releasing and charging inconsistently. Reconcile unknown provider outcomes before releasing reservations. Ordinary editing, viewing and exports should not charge again. Define included photo volume, analysis reruns and illustration regenerations before launch. Enforce the free proof once per verified organisation, and add spend caps and job deduplication.

Fulfil after verified server payment confirmation, with signed webhooks and unique purchase/event constraints. The return-to-site page is not proof of payment. Handle duplicate/out-of-order events, delayed payment success, reconciliation, refunds/disputes and simultaneous last-credit requests. [Stripe fulfilment](https://docs.stripe.com/checkout/fulfillment)

For current undiscounted, same-currency packs, proposed upgrade examples before VAT are:

| Action | Charge | Credit effect |
| --- | ---: | --- |
| Buy 10 credits | £60 | Add 10, separately from the free proof |
| Upgrade an eligible 10-credit pack to 50 | £165 | Add 40; existing consumption remains |
| Upgrade an eligible 50-credit pack to 250 | £650 | Add 200; existing consumption remains |
| Buy another single credit | £7.50 | Add 1 |

Example: a club has used 3 of its 10 credits. After the £165 upgrade, 47 remain, not 50. Record purchase lineage and upgrade eligibility so the same purchase cannot be upgraded twice. Define promotional discounts, refunds, historical price changes and eligibility periods explicitly. Separate top-ups do not silently become retrospective upgrade discounts.

Recurring integration upgrades are different: quote the time-based price adjustment and only enable a paid upgrade once payment succeeds. Stripe supports prorations and pending updates for this purpose. Schedule downgrades/cancellation at the stated period end; define failed-payment grace and recovery without destroying archives. [Stripe prorations](https://docs.stripe.com/billing/subscriptions/prorations), [pending updates](https://docs.stripe.com/billing/subscriptions/pending-updates)

The current homepage's unqualified 'No subscription' and 'All features included' claims must be scoped to the core archive before optional paid integrations are offered.

## Delivery order and acceptance gates

1. **Publication and identity controls.** Separate approval states; enforce private/public policies across resources; test withdrawal/cache invalidation, unapproved matching and cross-club access. Decide a reviewed migration for existing boards.
2. **Production account, data and job foundation.** Verified accounts/recovery/roles; transactional storage for club entitlements and jobs; backup restore drill; uploads/secret handling; application error monitoring and provider spend limits. A single web instance is acceptable initially if recovery is proven, but process-local counters are not a billing ledger.
3. **Core paid UK launch.** Signed Checkout fulfilment, ledger and quota enforcement; test failed/replayed payments, concurrent consumption, worker restart, refunds and pack upgrades. Complete matching service policies, retention controls and terms. Run a small invited-club pilot before opening paid signup broadly.
4. **Generic embedding.** Install link/iframe/loader, confirm desktop/mobile sizing, accessibility and approved-domain behaviour. Protect data independently of where it is displayed.
5. **Paid Intelligent Golf pilot.** Secure the publisher; prove either trusted member SSO or CMS-protected data synchronisation; test logged-out/member/former-member access, identity changes, CMS upgrades and subscription expiry. Package other platforms as adapters only after this path is reliable.

Completion is demonstrated by acceptance evidence, not by replacing disabled payment buttons with active ones. The legal notice must describe the shipped behaviour, and billing must remain unavailable until the required gates pass.
