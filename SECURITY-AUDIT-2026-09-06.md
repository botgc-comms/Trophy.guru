# Trophy Archive security audit — 6 September 2026

## Scope and decision

Scope clarified by the user: assess the new Trophy.guru service and its proposed read-only Intelligent Golf member integration. The three supplied BOTGC files are examples of the desired experience, not a request to audit that older service or reproduce its administration features. The earlier review went beyond this scope; those observations are retained separately in REFERENCE-SERVICE-NOTES-2026-09-06.md and are not findings against Trophy.guru or prerequisites for its launch.

This report records the original source review and the subsequent local remediation in Trophy.guru, including uncommitted changes. No live service, hosting dashboard, deployment credentials or WAF was tested. Remediation changes application source only; the original account, trophy data and illustrations are preserved.

**The five confirmed source findings below have now been addressed locally.** The finding descriptions preserve the original evidence and refer to the pre-remediation code; their line numbers are historical. This is not production security approval. No evidence of an actual intrusion was sought or found by this source review. Deployed exposure remains unknown.

## Remediation status

| Finding | Local change |
|---|---|
| TA-1: resource exhaustion | Bounded free/paid/shared storage, credit-backed draft limits, serialised quota checks, bounded metadata and streaming uploads; verified resource endpoints; strict CSV/XML/XLSX import limits and one concurrent import. Rejected writes preserve existing records and image files. |
| TA-2: anonymous lock growth | A fixed pool of 128 publication locks replaces an unbounded per-ID dictionary. Public reads and expensive operations now have rate/concurrency limits. |
| TA-3: independent legacy password | The original-archive login now checks the existing account’s current password hash. `APP_PASSWORD` can bootstrap a missing original owner, but cannot bypass an existing owner’s password or create a passwordless Development login. Startup preserves existing identity-file bytes. |
| TA-4: verification bypass | Verification follows matched endpoint metadata, so route case and trailing slashes do not bypass it. |
| TA-5: CSV formulas | Export prefixes formula-shaped cells safely while retaining original archive text. |
| Publication storage | Streamed artwork is capped at 64 MiB per version; identical approval creates no extra copies. Only recognised obsolete publication copies are cleaned after commit, retaining current/previous. Withdrawal remains available over quota. |
| Browser hardening | Removed analytics execution from private archive/login pages; removed unrestricted inline scripts from CSP, using a fresh nonce for public structured data. |

Storage allowances are deployment-configurable. Existing over-limit records remain readable; reaching a limit does not delete them. The original owner’s unlimited credit entitlement is retained. See `LAUNCH-RUNBOOK.md` for exact defaults and remaining production checks. Public-name copying, member authentication for Intelligent Golf, owner MFA, edit history and verified off-host recovery remain separate matters below.

## Direct answer: browser credentials

**New Trophy.guru:** no hard-coded password, OpenAI key, Stripe secret or reusable bearer/JWT credential was found in the reviewed browser code. A redacted pattern scan of 42 public text assets found no credential-shaped literals. Pattern scanning is not a guarantee against every possible secret. The analytics measurement ID is a public identifier, not an authentication secret.

Authentication uses an ASP.NET encrypted/authenticated session cookie. Source configures HttpOnly, SameSite=Strict and Secure outside Development, with a 30-day sliding lifetime (`EntryPoint.cs:40–52`). Page JavaScript cannot read the HttpOnly cookie. It is still a credential: theft through a compromised device/browser can permit impersonation, and injected same-origin JavaScript can make authenticated requests without reading it. Browser storage holds consent/preferences and checkout retry IDs, not login tokens. Reset/verification/invitation links contain short-lived capability tokens; the recovery page removes the fragment from the address bar and holds it in memory, and the server stores a hash and checks purpose/expiry/single use.

## Intended plugin boundary

The Intelligent Golf member page is read-only: browse approved honours, search winners and trophy histories, and show “My trophies” using approved member links. Trophy creation, edits, deletion, member matching and publication decisions remain in the authenticated Trophy Archive administration app. Enforce this distinction in server permissions; hiding controls is insufficient. The page/CDN script must not contain reusable private API secrets or credentials that grant archive-write access. Private read access still requires a verified member connection or a genuinely restricted page-local snapshot.

## Findings in the new Trophy.guru service

### TA-1 — High: a free account can exhaust shared storage or import memory

Trophy creation is unbounded (`EntryPoint.cs:520`; `CatalogueStore.cs:50–74`). Whole-trophy photo uploads have per-file/batch and per-trophy limits (`EntryPoint.cs:682–714`) but no aggregate per-club quota or bound on trophies awaiting payment. They are available after ordinary club setup without email verification. Repeated new trophies can therefore consume the shared disk. The per-trophy count check also occurs outside the store write lock, so concurrent requests can exceed that nominal cap. `SecondaryName` is also missing the length validation applied to other trophy fields (`EntryPoint.cs:988–994`). A bounded probe confirmed a 4 KB secondary name is accepted; no large payload was submitted.

XLSX import is especially risky: `MemberDirectoryStore.cs:390–395` loads shared strings without the expanded-size guard used for other workbook parts. `:383` allocates a row array from an unchecked column reference computed at `:409–414`. A safe calculation-only probe showed an invalid column could request over 2 GB of references. The allocation was not executed. The checked-in deployment specifies one 512 MB worker and a shared 5 GB disk (`render.yaml`).

**Impact:** one abusive or compromised account could make every club's archive unavailable, and disk exhaustion could prevent saves. This is not a demonstrated cross-club read/edit bypass.

**Action:** bound storage per club, unprocessed trophy counts, every metadata field, expanded XLSX parts, total cells/rows/columns and import CPU/concurrency. Require verification for resource-consuming uploads/imports. Apply request and account quotas before expensive work; keep off-host backups and disk/memory alerts.

### TA-2 — Medium: anonymous invalid-club requests accumulate memory

`HonoursPublicationStore.cs:15,23` retains one semaphore for every distinct valid club ID requested, including nonexistent clubs. Anonymous `/honours/{clubId}`, `/embed/{clubId}` and the public JSON endpoint reach this code (`HonoursEndpoints.cs:86–113`). There is no bounded eviction and no rate-limit policy on these routes.

**Action:** avoid retaining a lock for nonexistent publication roots, or use a bounded/ref-counted lock strategy. Add public endpoint abuse limits. No request flood or memory exhaustion was attempted.

### TA-3 — High when enabled: the original-archive password remains a separate login route

`LegacyArchiveAccess.cs:10–29` validates the environment's `APP_PASSWORD`; `EntryPoint.cs:418–439` signs the caller into the original archive. A normal password change updates the account hash/security version, but does not retire this independent credential. A fake-account probe confirmed that the old normal login fails after a password change while the original-archive password still allows a fresh valid session.

**Impact:** someone knowing the old shared password can regain access after a normal password change or session revocation. The actual production value/configuration was not inspected. If Production has no configured `APP_PASSWORD`, this recovery route is unavailable. Development deliberately allows passwordless recovery when original data is present, so a Development server with real data must stay local.

**Action:** make recovery explicitly temporary/disabled after controlled migration and verify ordinary owner access first. Preserve `archive@botgc.test`, its account ID, original password hash, club, records and keys; do not delete/reseed the account to close this route. Coordinate credential retirement with a safe recovery method.

### TA-4 — Medium: route casing bypasses the email-verification gate for AI work

`EntryPoint.cs:284–288` identifies AI-start routes with case-sensitive suffix comparisons, while ASP.NET routes match case-insensitively. The handlers do not independently check email verification.

An isolated loopback test with a fictional unverified account and no AI key confirmed lowercase paths were rejected with 403, while equivalent uppercase paths reached the upload/AI handlers. No real trophy/provider job was run. This bypasses the verification prerequisite, not the session/club checks or credit ledger.

**Action:** apply verification as an endpoint policy/filter, with mixed-case/trailing-slash regression coverage, rather than infer authorisation from URL spelling.

### TA-5 — Medium: exported CSV can retain attacker-supplied formulas

The CSV formatter quotes fields but does not neutralise spreadsheet formulas (`EntryPoint.cs:913–939,1011`). A harmless `=1+1` probe remains a formula-shaped value. An editor or imported name could put formula text into an export that an owner later opens in spreadsheet software. What executes depends on that software and its security settings.

**Action:** protect spreadsheet exports against formula injection while preserving the original archive text.

## Public names: an intentional exposure that needs a clear choice

A published board's selected names and years are available as a complete public JSON snapshot (`HonoursEndpoints.cs:107–113`). Anyone who can see a public board can copy or automate collection of those records. A difficult-to-guess URL, noindex header, iframe domain allowlist or protected page surrounding a public embed does not make the underlying record private.

The new implementation does keep private archives inaccessible until explicit publication, filters selected confirmed winners, defaults descriptions/junior trophies to excluded, keeps member-directory/private evidence details out of the public projection, and checks publication on HTML/JSON/published-asset access. Withdrawal prevents future service access; it cannot erase a visitor's prior copy. Rate limits reduce mass abuse but cannot make public records uncopyable. Use a genuinely member-authenticated delivery mode for clubs that do not want public disclosure; authorised members can still copy what they see.

**Static artwork exception:** `wwwroot/catalogue` contains 100 original PNG illustrations, served anonymously and cached for seven days; legacy JPG URLs redirect to them. These are outside publication withdrawal. Six are used by the fictional demo. This audit did not establish whether identifying real engravings remain in those images, so review the public asset set before privacy sign-off. `Data/trophies.json` contains 102 trophy definitions without winner/year/member fields and is outside `wwwroot`; no direct HTTP route to it was found. The 42 demo winner records are explicitly fictional.

## Additional hardening and recovery gaps

- **Account takeover resistance:** no MFA/passkey support was found. Password hashing, generic login errors, rate limiting, owner/editor separation and session-version revocation exist, but a stolen owner password still authorises damaging edits. Owner MFA/passkeys and stronger credential-abuse controls are appropriate before scaling.
- **Browser script containment — remediated locally:** the original general archive/login CSP permitted `script-src 'unsafe-inline'` (`EntryPoint.cs:127`), reducing protection if an HTML injection is later found. No credible stored-XSS path was found in reviewed name/description rendering; this is a defence-in-depth gap, not proof of an existing injection exploit. Prefer nonce/hash policies and remove unnecessary inline execution. [OWASP CSP guidance](https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html).
- **Third-party scripts — remediated locally:** with consent, the original archive/login pages loaded Google Analytics JavaScript (`archive.html:18`, `analytics.js:138–162`). The current event wrapper excludes names and private parameters, but a third-party script runs with page privileges. Keep account/evidence screens free of third-party execution where practical; the recovery page already is. Consent is not a technical sandbox.
- **Recovery after malicious edits:** winner and evidence deletion is permanent in the current catalogue, with state replacement and no per-edit user-attributed revision/undo system (`CatalogueStore.cs:164–175,425–427,737–743`). Publication has a limited audit trail; that is not a general trophy-edit history. Offline backup/restore tooling exists, but scheduled encrypted off-host backups, restore drills and immutable edit history were not verified. Compromised editors are allowed to edit within their club, so detection and restoration matter even when tenant checks work.
- **Keys and server compromise:** account/catalogue/ledger files are not application-encrypted, and the session key ring is persisted to disk without an explicit wrapping/encryption provider (`EntryPoint.cs:36–38`). Microsoft notes that explicit file-system key persistence disables automatic at-rest key encryption unless separately configured. Hosting disk encryption and actual filesystem permissions were not checked. Restrict backup/key access and encrypt off-host backups/keys appropriately. [Microsoft key-storage documentation](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0).
- **Deployment:** Dockerfile has no explicit non-root user. Live TLS/HSTS, trusted proxy/IP rate-limit behaviour, hosting-account MFA, WAF limits, disk permissions, secrets and monitoring remain unverified. Do not infer they are absent solely because application code does not configure them. The package scan found no currently reported vulnerable NuGet dependencies; it does not scan the deployed .NET runtime, container OS or custom-code vulnerabilities.

## Original audit verification

- Read-only review of the current application; no application-source mutations or live attack traffic. Out-of-scope reference-service observations were separated after user clarification.
- Redacted secret-pattern scan of 42 browser text files: no matches.
- `dotnet list Trophy.Catalogue.csproj package --vulnerable --include-transitive`: no reported vulnerable packages from current NuGet sources.
- Bounded calculations/reflection with fictional fixtures: unchecked XLSX allocation size (no allocation), accepted secondary-name length, harmless CSV formula, legacy-password survival.
- Isolated loopback 5197 app with fictional data and no AI key: mixed-case verification bypass observed; test server stopped.
- Reviewed existing tenant/session/publication protections and prior regression coverage. No confirmed anonymous private-cabinet access, cross-club read/edit bypass or stored-XSS exploit found in the new app during this limited audit. That does not constitute a penetration-test certification.

## Original remediation priorities and remaining launch work

1. Keep the plugin read-only with a narrowly scoped read-data contract; keep archive administration and its credentials within the authenticated service.
2. Close the new service's resource-exhaustion paths, anonymous lock-cache growth and AI verification bypass.
3. Safely retire the independent legacy login credential while preserving and verifying original-account access.
4. Decide public versus member-restricted data delivery; review static artwork separately.
5. Add export protection, owner account hardening, edit recovery/audit and verified off-host backups; tighten browser/server settings.
6. Re-test the actual deployed build and edge configuration with isolated test clubs, including attempts to read and mutate another club's objects. Obtain an independent targeted penetration test before treating launch readiness as complete.


## Remediation verification — 6 September 2026

- Full Release regression suite: **150 passed, zero failed**. New coverage includes bounded imports/ZIP/XML parsing, storage/concurrent writes and cancellation rollback, original illustration preservation, current-password legacy login, metadata-based workload limits, CSV protection, publication retention/failure and over-quota withdrawal.
- Isolated HTTP/browser checks passed for route casing/trailing slashes, verification, fixed-length/chunked oversized requests, private access and tenant isolation, publication/withdrawal, password reset and revoked sessions. No JavaScript errors occurred on the tested desktop/mobile pages under the stricter CSP.
- All four homepage gallery illustrations remain; only their visible name captions and identifying alt text were removed. One regional guide link is selected, using a signed-in club country first and browser language/timezone fallback; explicit UK/US guide URLs remain usable.
- The three original data files still match the preservation manifest byte for byte. No original account, catalogue record or illustration was altered during remediation testing.
- No production deployment, live penetration test, MFA implementation, hosting configuration or backup scheduling is included in these results. Public records remain copyable; the protected Intelligent Golf delivery mechanism remains subject to its documented release gate.
