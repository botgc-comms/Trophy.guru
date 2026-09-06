# Historical observations outside the requested scope

The user supplied the three BOTGC files as functionality/UX examples only. These observations resulted from an over-broad review. They are not vulnerabilities attributed to the new Trophy.guru service, not requirements to reproduce old functionality and not part of the current remediation scope. No further old-service investigation or changes are authorised by the reference files. No live deployment was tested. Retained solely to preserve the prior audit record and its correction.

**Older BOTGC Intelligent Golf page:** `trophy-winners.js:4` contains a deployment placeholder for a shared website access key; lines 36–47 attach it to API requests. It does not contain a literal member password or bearer-token implementation. The deployed key value was not inspected or disclosed. If substituted into the downloadable script, it can be recovered by anyone able to obtain that script. It cannot prove which member is signed in.

## Urgent findings in the older BOTGC.API integration

### IG-1 — Critical: CMS publishing routes lack authentication in the inspected source

`../Services/BOTGC.API/Program.cs:351` excludes the entire `/api/cms/pages` prefix from `AuthKeyMiddleware`. `CmsController.cs:237` (Markdown publishing) and `:309` (HTML publishing) have no authorisation requirement. There is no global fallback authorisation policy; `ClubWebsiteMiddleware` applies only to actions bearing its attribute. These two actions lack it.

The HTML action uses the server's Intelligent Golf connection to save supplied page content (`Services/QueryHandlers/UpdateCmsPageHandler.cs:69–81`). The Markdown action also accepts public/restricted settings (`CmsController.cs:280–302`). If this source is deployed without an independent upstream restriction, an unauthenticated caller could alter club pages, introduce malicious content or change their visibility. No mutation or live probe was attempted.

**Action:** verify deployed code and restrict publishing immediately if this route is reachable. Require explicit authenticated server-to-server publisher permissions, deny by default, and test unauthenticated/ordinary-member rejection. Keeping a key in the CI request is insufficient when the receiving route does not require it.

**Correction:** the previous integration note said the CMS publisher had API-key protection. That missed the middleware exemption and was incorrect; the note and launch runbook have now been corrected.

### IG-2 — High: a browser-shared key permits trophy mutation and directory harvesting

`ClubWebsiteMiddleware.cs:59–80` checks Origin plus the shared website key, not a verified member session. `TrophiesController.cs:366–388` permits trophy winner creation/overwrite and `:95–118` deletion without administrator identity checks. The member search endpoint at `:163–186` returns directory names, membership numbers and player IDs under the same weak boundary. Restricting the CMS page or hiding edit buttons does not restrict these independent API calls.

**Impact:** a holder of the delivered browser key can make direct requests to read data or change records if the deployed service matches this source. A non-browser client can supply an Origin header; CORS is a browser policy, not proof of identity. [OWASP REST security guidance](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html).

**Action:** authenticate the member server-side and enforce administrator permissions for writes. Do not provide membership-directory access through browser-shared credentials. Rotate the old key after replacing the trust model; rotation alone does not fix it.

### IG-3 — High: CMS document permissions trust a supplied member ID

`CmsController.cs:454–481` permits permission lookup using a query member ID. `CmsDocumentAuthorisationService.cs:94–110` compares that supplied ID to administrator records without establishing caller identity. Configuration and document operations accept similar submitted IDs (`CmsController.cs:135–170,514–519,624–637`).

**Action:** derive identity from verified authentication on the server, never from request fields or `window.userID`. A page-local approved snapshot protected by Intelligent Golf remains a potential alternative for read-only honours, but only after the publisher and restricted-page protections are verified.

