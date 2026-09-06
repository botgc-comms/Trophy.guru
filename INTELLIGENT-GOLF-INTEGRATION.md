# Intelligent Golf integration: delivery design and existing reference

Reviewed 6 September 2026. This is an implementation note, not a claim that the managed integration has been installed or activated for customers. No live club pages, archive records or original illustrations were changed during this review.

## What already exists

The existing Burton-on-Trent Golf Club implementation is a substantial reference, not just a mock-up:

| Reference | Existing behaviour |
| --- | --- |
| `../Services/BOTGC.SiteContent/trophy-winners.md:2` | Targets CMS page 503; front matter sets `isPublic: false` and `isRestricted: true`. |
| `../Services/BOTGC.SiteContent/trophy-winners.md:23` and `:507` | Loads versioned CSS and JavaScript from Azure Front Door. |
| `../Services/BOTGC.SiteContent/assets/botgc-co-uk/scripts/trophy-winners.js:54` | Reads `window.userID` and `window.properties`, then resolves a positive player ID from the supported property names or the trailing numeric part of the user ID. |
| `../Services/BOTGC.SiteContent/assets/botgc-co-uk/scripts/trophy-winners.js:217` | Calls the BOTGC API for trophy catalogue, year winners, trophy history, member wins, member lookup and competition suggestions. |
| `../Services/BOTGC.SiteContent/assets/botgc-co-uk/scripts/trophy-winners.js:1697` and `:2017` | Renders a member's honours and the signed-in user's “My trophies” view. |
| `../Services/BOTGC.SiteContent/assets/botgc-co-uk/scripts/trophy-winners.js:2394` | Provides member lookup in the administrator's winner editor, allowing linked members or manually entered names. |
| `../Services/BOTGC.SiteContent/assets/botgc-co-uk/styles/trophy-winners.css:2122` | Includes responsive layouts; later rules provide reduced-motion and print treatment. |

The current Trophy Archive service also has a generic loader at `wwwroot/embed/v1.js`. It creates the service's honours-board iframe, includes a direct-link fallback and checks both message source and origin before changing the frame height. `data-service-origin` lets a separately hosted copy of the loader address the service. This is a **publicly published honours board** embed; putting it on a restricted CMS page does not make its source data private.

## What can be reused from the existing publisher

`../Services/BOTGC.API/Services/QueryHandlers/UpdateCmsPageHandler.cs:44` fingerprints rendered HTML and skips an unchanged page. At line 74 it targets the existing Intelligent Golf CKEditor save mechanism using the configured server-side data provider. `UpdateCmsPageSettingsHandler.cs:36` updates the page settings, including restricted/public visibility.

`../Services/.github/workflows/reusable-deploy-site-content.yml` uploads changed assets, rewrites supported asset references and submits processed Markdown to the CMS publisher. The publishing request uses a server-held API key. This is a useful deployment reference, but it currently targets the BOTGC service/configuration; it is not a multi-club provisioning service.

Reuse the small installed page, centrally hosted assets, content fingerprinting, year/trophy/person views, responsive treatment and preservation of the club's existing navigation. Refactor the reference's global JavaScript into an isolated module, remove club-specific defaults and scope all CSS below the widget root. The existing stylesheet has rules affecting `.inner-full`, `.menu-section` and `body`; these should not be copied wholesale into customer sites.

## Two practical delivery modes

### 1. Members-only CMS snapshot

This is the simplest candidate for a managed Intelligent Golf pilot if the customer permits protected page HTML and a hosted script:

1. The club owner approves a read-only honours snapshot in Trophy Archive.
2. The managed publisher places the approved snapshot in the body of a restricted Intelligent Golf CMS page, plus one hosted script reference.
3. Intelligent Golf controls access to that page. The script renders only the data that the protected page has already delivered.
4. The script may use the page's player-ID hint to preselect “My trophies” within that same authorised snapshot. This hint does not grant access to additional information or editing.
5. Publishing a new approved snapshot updates the CMS page. Changes to the renderer are deployed centrally without rewriting every customer's page.

The page should contain a widget mount, a safely JSON-encoded snapshot and the script reference. Inline JSON must escape `<` so a winner name cannot terminate the script-data element. It should contain no credentials, dates of birth, membership numbers, full member directory, source photographs or unreviewed matches. Illustrations may be served publicly only when their content is suitable for public access; private evidence must stay behind authentication.

This is not a live private API. It trades immediate data updates for straightforward member access using the club's existing login. The publisher must verify that a logged-out request cannot retrieve the snapshot, fail closed if page restrictions cannot be confirmed and retain the previous good page for rollback. Withdrawal must update/remove the CMS snapshot as well as the service publication; a successful service withdrawal alone cannot revoke a copy already installed in the club CMS.

### 2. Live board with trusted member access

For updates immediately served from Trophy Archive, a server must establish that the person is signed in to the relevant club. Use an approved provider mechanism or a server-controlled bridge that can validate the club session; then issue a short-lived, audience-bound assertion scoped to that club and member. Validate signature, issuer, audience, expiry, replay controls and subscription entitlement server-side. Do not place reusable secrets or tokens in page markup or query strings.

There is no verified Intelligent Golf browser-session assertion in the inspected trophy page. A supported provider interface needs to be confirmed for each integration method; do not invent an SSO endpoint. Cross-site cookie restrictions also mean an iframe cannot simply rely on the administrator's existing Trophy Archive cookie.

The BOTGC API has a separate JWT-backed `app/web-sso` endpoint in `Controllers/AuthController.cs:51`. It exchanges an already authenticated mobile-app identity for a short-lived web code. That is not evidence of an Intelligent Golf page authenticating its browser member to Trophy Archive.

## Why the existing browser key is not member authentication

The reference renderer sends a shared `X-BOTGC-Website-Key` injected into downloadable JavaScript and uses `credentials: "omit"`. `ClubWebsiteMiddleware.cs:59` checks the request origin and shared key. It does not verify the current Intelligent Golf session or bind a supplied member ID to an authenticated member.

Those checks cannot protect a commercial members-only API: downloaded JavaScript can be inspected, browser variables can be changed, and a non-browser client can supply an Origin header. The current reference also obtains administrator permissions using a client-supplied member ID. Do not carry that trust model or the browser-driven mutation routes into the commercial connector.

The supplied files are UX/functionality references only. Their BOTGC-specific administration API is not a dependency of the new plugin. Implement and verify a dedicated server-side installation/publishing connection only where needed; no publisher or archive-write credential belongs in the member page or CDN script. The separate historical service observations are outside the current scope.

## Product scope and boundaries

User clarification: the plugin is a read-only member experience. Do not reproduce trophy creation, editing, deletion or administration from the example page. Those actions, including member matching, stay in the authenticated Trophy Archive app. Read-only permissions must be enforced on the server, not merely by hiding buttons.

The proposed £299-per-club annual managed option can cover installation on an agreed Intelligent Golf page, club branding, ongoing compatibility maintenance, access to the selected delivery mode and support. Its member-facing features should be described precisely:

- Browse approved honours by year, trophy and person, including trophy illustrations and history.
- Show “My trophies” when there is a reliable link between the page's current player identifier and approved archive winner identities; otherwise leave ordinary browsing available without guessing by surname.
- Search published winner names and their honours. This is not an unrestricted club member directory.
- Allow authorised archive editors to review suggestions and link inscriptions to past or present member records. The public/member board remains read-only.

Current/past member import or synchronisation is a separate data connection: the existing trophy page consumes BOTGC's member-search endpoint, not a portable Intelligent Golf directory API. Define the permitted fields, source, refresh process, deletion behaviour and club authorisation before promising automatic synchronisation. A same-surname match must never become an asserted identity without review.

Purchasing the annual option should create an installation entitlement, not automatically publish private records. Show provisioning state separately from payment state: requested, awaiting club access, configured, checked, active and suspended. Cancellation should stop renewal; access continues until the agreed paid-through date unless a documented security intervention is necessary. Failure or expiration must not remove the club's core archive, trophy credits or original files.

## Remaining work before a managed live installation is claimed complete

1. Select and implement the pilot delivery mode with the club; record the desired audience and approved data scope.
2. Build per-club connector configuration, safely held publisher credentials, page/domain bindings, installation status and rollback.
3. Build the shared read-only renderer around the Trophy Archive board and approved snapshot contract, using the supplied page only as a UX reference. Do not bring its administration controls or BOTGC API dependencies into the plugin.
4. Add approved archive-person to provider-player mappings. Preserve the original inscription and unmatched historical names.
5. For CMS snapshots, implement reviewed publication, refresh and verified withdrawal into the protected page. For a live API, implement verified identity and entitlement checks before enabling private data routes.
6. Connect annual subscription events to the connector entitlement and installation workflow; verify the configured Stripe price is annual, in GBP and agrees with the amount shown to the buyer.
7. Test logged-out denial, a signed-in member, a member without a matching winner, a wrong-club user, revoked access, cancellation, mobile layout, content escaping and accidental CMS-publication changes.
8. Only then activate a live customer installation. Keep `archive@botgc.test`, its credentials, club association, balances and actual trophy data unchanged throughout migration/testing.
