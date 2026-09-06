# Trophy Archive — launch and preservation runbook

6 September 2026. The current local implementation passes 86 automated regression cases and the isolated browser smoke checks; this is not a record of a production deployment or real payment. Keep billing disabled until the external release gates below have evidence.

## Preserve the existing archive

Keep the existing Render service and `/var/data` disk. Do not create a replacement empty service, change `DATA_PATH`, seed the real archive or copy a QA database over it. Preserve `identity.json`, the entire key-ring, member-identity keys, all club/catalogue files, uploaded photographs and generated illustrations. The new `operations.sqlite` files and per-club publication folders belong to the same backup set.

The local original account `archive@botgc.test` was found in `C:/_src/BOTGC/Miscelaneous/Trophy.Catalogue/data-store`, with its existing account ID, `legacy` club and unlimited flag intact. A byte-verified preservation copy was made in the ignored `data-backups/legacy-preservation-20260906T115903Z` folder. It contains credentials and keys: keep it private and do not commit it. This is a local backup, not verification of the Render disk. The original files were not edited or used for integration tests.

Original owner email, password hash, account ID, club ID and unlimited trophy-credit allowance remain supported. `APP_PASSWORD` now only bootstraps an owner when the original catalogue has no owner account. The original-archive HTTP login verifies the current owner account password, including after a password change; it never accepts a separate old environment password or an empty Development password. Startup does not rewrite an existing owner account. The trusted original owner is exempt from the new email-verification gate, including its `.test` address. Opening the original archive must never reassign a different account. Keep the same data-protection application name and key-ring so existing compatible sessions survive.

## Offline backup and restore

Run one application writer per `DATA_PATH`. The app holds `.instance.lock` for its lifetime and refuses a second instance. Stop all application versions using the data directory before an offline backup, including older releases which did not have this lock. Pause incoming paid/AI work and handle queued jobs as part of the maintenance window.

After building the release, use absolute paths:

```text
dotnet Trophy.Catalogue.dll --backup-data /secure-backups/archive-YYYYMMDD --data-path /var/data
dotnet Trophy.Catalogue.dll --restore-data /secure-backups/archive-YYYYMMDD --destination-data /new-empty-restore-directory
```

The backup command copies files and writes a SHA-256 manifest. Restore verifies the manifest before copying and requires a completely new destination; it never overwrites a live archive. Keep the original directory and backup until the restored copy has been checked. Restore the identity/key-ring and operational ledger together. Check account login, club IDs, trophy/winner counts, illustrations, member identity keys and publication status. Do not replay a running AI call merely because the app restarted: interrupted jobs become `needs_review`.

Implement restricted, encrypted off-host backups and a documented restore drill before paid launch. Choose retention, frequency and recovery objectives with the business owner. A single persistent disk and a CSV export are not a complete backup plan. Reapply post-backup privacy withdrawals/deletions during a recovery before public access resumes. A rollback must also prevent old code from restoring automatic public exposure or bypassing paid-operation limits.

## Configuration and release gates

| Gate | Required evidence |
|---|---|
| Canonical HTTPS origin | `PUBLIC_SITE_URL` set to the actual service origin; cookies secure; origin checks tested through the real TLS proxy. Configure only trusted proxies if forwarding client IPs; do not trust arbitrary forwarded headers. Authentication limits currently partition by observed peer IP. |
| Email | `EMAIL_TRANSPORT=smtp`, `EMAIL_FROM`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`; TLS and inbox delivery tested, sender domain authenticated, reset and invitation links tested from email clients. Existing accounts remain usable when email is unconfigured; new signup fails closed. |
| Stripe testing | `BILLING_MODE=test` with matching `sk_test_...`, `whsec_...`, HTTPS origin (loopback HTTP allowed only for local tests). Configure a signed webhook at `/api/billing/webhook`. Exercise real Stripe test checkout, network retries, duplicates, refunds, portal and delayed payment notification before live use. |
| Stripe live | Approved business identity, bank account, prices/tax/invoices, refund/dispute procedure and completed terms. Only then use `BILLING_MODE=live`, matching live keys and `BILLING_LIVE_APPROVED=true`, `BILLING_LEGAL_READY=true`. These flags are operator attestations, not an automated compliance certification. |
| Optional integration subscription | Leave `IG_INTEGRATION_AVAILABLE=false` and `STRIPE_IG_PRICE_ID` blank until the contracted integration is deliverable. The displayed price is £299 per club per year. The configured Stripe Price must be active, fixed £299 GBP (`unit_amount=29900`), per-unit/licensed, recurring yearly with interval count 1, and match the current test/live mode. Checkout verifies it again before purchase. Recurring checkout/portal code alone is not an Intelligent Golf login adapter. |
| Privacy and terms | Complete the three service drafts, controller/processor roles, subprocessors and transfer review, retention and erasure workflow, subject-request contact, incident procedure, LIA/DPIA screening, pricing and consumer-law applicability. Publish final documents, implement recorded versioned acceptance and durable order confirmation before enabling paid self-service. |
| Operations | Off-host backup/restore evidence, disk and queue alerts, monitored support/incident channel, patching, capacity test and production account-preservation check. No production load test or independent penetration test has been claimed. |

`.env.example` lists the variable names without secrets. Development pickup is explicitly configured with `EMAIL_TRANSPORT=development`, an absolute `EMAIL_DEVELOPMENT_DIRECTORY` outside `wwwroot`, and `EMAIL_FROM`; it is rejected in Production. No real mail was sent during the local tests.

Stripe webhook events: `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.expired`, `charge.refunded`, applicable `charge.dispute.*`, `customer.subscription.*`, `invoice.paid`, `invoice.payment_failed`. Checkout and subscription state is retrieved from Stripe rather than trusting a browser return. Webhook signatures cover raw bytes and are time limited. Refunds and disputes stop new AI spending for review without deleting work. Document an operator reconciliation procedure before launch; financial holds are deliberately not automatically cleared from a late event.

The price schedule uses integer pence. A ten-credit pack adds ten paid credits in addition to the first proof. A 10-to-50 upgrade costs £165 and adds 40; prior usage remains used. Photos and AI attempts have documented limits. The owner can review interrupted jobs from trophy credits; acknowledgement records that the previous attempt remains counted and allows a fresh request within the allowance.

## Honours board rollout and Intelligent Golf

Each club begins private, including existing clubs without a publication record. Before releasing this change to existing customers, arrange owner review and publication of any board they intend to keep public. Confirming an inscription is no longer permission to publish it. This change restricts public visibility; it does not remove private archive records.

The owner opens **Honours board**, reviews records and name policy, previews the actual renderer, and explicitly publishes. Descriptions and junior trophies default to excluded; manually approved directory identities require a separate public-name choice. Private evidence, birth years, membership numbers and matching rationale never enter the public projection. Publication is a frozen version, including selected logo/illustration bytes. New edits require a new preview. Withdrawal gates HTML, JSON and assets; open pages recheck on visibility and periodically. Visitors’ existing copies cannot be recalled.

The sharing panel produces a link, an iframe snippet and the loader snippet. A typical centrally maintained install is:

```html
<script src="https://YOUR-SERVICE-ORIGIN/embed/v1.js" data-club="CLUB_ID" defer></script>
```

Set the club’s exact HTTPS embedding origins and publish that setting. If mirroring the loader to a CDN hostname, add `data-service-origin="https://YOUR-SERVICE-ORIGIN"`. One shared renderer and loader support all clubs. A CMS permitting iframe HTML can avoid JavaScript; a CMS permitting only links can use the hosted URL.

The public endpoint stays public inside a protected CMS page. For members-only Intelligent Golf access, first obtain a supported OIDC/SAML flow or server-issued signed member assertion, or use an explicitly approved protected-content synchronisation arrangement. Reading `window.userID`, page text or member names in browser JavaScript is not proof of authentication. Do not embed a signing secret or privileged API key in a page/CDN file. The supplied BOTGC page already uses centrally hosted scripts and a member ID for personalisation. The supplied BOTGC files are examples of member-facing functionality, not a dependency to port or an old-service audit request. The new plugin must remain read-only, with creation/editing/deletion/member matching inside the authenticated archive app and server-enforced permissions. No reusable private or administrative credential should be delivered in the page/CDN script. See [service security audit](SECURITY-AUDIT-2026-09-06.md). The proposed members-only pilot instead places an approved snapshot inside a restricted CMS page, including protected artwork, and must verify logged-out access is denied. Snapshot withdrawal must update the installed CMS copy too. See [Intelligent Golf integration design](INTELLIGENT-GOLF-INTEGRATION.md) for the reference audit and remaining implementation work.

The £299 optional card appears beneath the core prices on the homepage and in the signed-in trophy credits dialog. `/integrations/intelligent-golf/` explains planned features and the centrally maintained installation. Both public and signed-in pages use server offer metadata; purchasing remains disabled until the integration and payment gates pass. Public sharing and archive member matching remain core features.

Annual checkout keeps one persistent pending order per club and reuses the Stripe session across browser retries and restarts. An unknown session-creation outcome older than 23 hours requires support reconciliation before another session can be created; do not clear the order merely to enable a second payment. Check Stripe using the stored order metadata, customer and idempotency key first. Subscription state does not prove an installation is configured or that private records have been approved for release.

## Repeatable verification

```text
dotnet test Tests/Trophy.Catalogue.Tests.csproj -c Release --nologo
```

Tests create their own temporary data directories. The dependency vulnerability check reported no vulnerable NuGet packages from the configured source. Coverage includes original account/password/session preservation, token replay, owner/editor boundaries, frozen publication and HTTP asset withdrawal, last-credit concurrency, duplicate payment events, upgrades, refund ordering, bounded AI attempts, interrupted jobs, same-origin checks and verified backup/restore.

`Tests/browser-smoke.cjs` requires an explicitly isolated loopback app with a `trophy-launch-qa-*` data directory, Development email pickup, no AI key and disabled payments. Supply `PLAYWRIGHT_MODULE`, `QA_DATA_PATH`, `QA_OUTPUT_PATH` and optional `QA_BASE_URL`. It rejects non-loopback destinations and never uses the real account. Browser checks cover signup, verification replay, private/public access, actual preview, disabled checkout, desktop/mobile screens and withdrawal with record retention.

Runtime planning: the current app targets .NET 9, whose support ends 10 November 2026. Schedule and test a move to .NET 10 LTS before that date, and keep current security patches installed. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). This change deliberately leaves the running archive’s runtime and disk topology intact.

`Tests/integration-browser-smoke.cjs` uses the same isolated QA environment to check the £299 annual option, pricing/detail pages, the real honours-board preview, signed-in deep links, desktop/mobile layouts and subscription controls. Provider calls in automated annual-subscription tests are simulated; a genuine Stripe test-mode end-to-end payment remains a release gate.

## September security hardening

The implementation closes the audited mixed-case verification bypass using matched-endpoint metadata. Uploads, AI work and member imports require verification (the trusted original owner remains supported). Onboarding logo uploads remain possible before verification, but share the four-operation concurrency limit. Dynamic requests also have a 32-operation server limit; resource work (including member-matching reads and writes) is limited to 20 requests/account/minute, ordinary writes to 90, and public reads to 600 per account/observed IP. Existing authentication limits remain 12/minute. JSON body limits are 16 KiB for authentication and 128 KiB normally; publication selection and upload endpoints declare their own bounded sizes. Validate proxy IP behaviour before deployment.

The archive/login no longer loads analytics JavaScript. Script CSP no longer permits unrestricted inline scripts; public structured-data blocks receive a per-response nonce. The recovery page remains isolated. CSV export neutralises spreadsheet formula prefixes without altering stored names.

Member imports accept at most 10,000 members, 64 columns, 250,000 cells, 2,048 characters per field and 4 Mi characters of table text; names are limited to 200 characters and membership numbers to 80. Uploads are limited to 15 MiB, total XLSX expansion to 24 MiB and 256 archive entries; XML depth/node counts are bounded. Only one member import can parse across the service at once. Invalid imports leave the previous directory in place.

Storage defaults are 256 MiB per free club, 2 GiB per paid club, 4 GiB across the data directory, and 128 MiB physical free-space reserve. Configure these with the `ARCHIVE_*` values in `.env.example` alongside provisioned disk capacity, customer expectations and monitoring. Trophy drafts are bounded by retained credit capacity plus four draft slots; reaching a limit never deletes records. Ordinary saved-file writes check limits under a shared process gate. Existing over-limit records remain readable and can be reduced. The legacy owner's unlimited credit entitlement is retained; shared physical safety limits still apply.

These changes do not configure production MFA, hosting permissions, certificate-wrapped session keys, off-host backup scheduling, WAF rules or an Intelligent Golf member authentication provider. Those remain explicit deployment/product work; do not mark them complete based on the local checks. All public records remain copyable by their audience.

Publication artwork is streamed and capped at 64 MiB per frozen version before image content is read. Re-approving identical content creates no extra files. A successful new publication keeps its current and immediately previous recognised image-copy revision; original source images and unknown folders are never removed by this cleanup. Failed writes preserve the last good version. Withdrawal remains available when a club is over quota, using a bounded control-state write, and still requires a successful on-disk commit.

Final local validation on 6 September 2026: **150/150 automated tests passed**, plus `Tests/security-browser-smoke.cjs` and the existing `Tests/browser-smoke.cjs` against a new, separate loopback QA archive. These checked HTTP case/trailing-slash verification, known-length/chunked body rejection, CSV formula protection, tenant isolation, publication/withdrawal, password reset/session revocation, private CSP, country/language/timezone guide selection and desktop/mobile rendering. Four original gallery PNG files were not edited. The three original archive files were rechecked against their preservation manifest with no differences. No live payment, email, AI provider or production attack traffic was used.
