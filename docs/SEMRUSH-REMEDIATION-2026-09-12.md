# Semrush remediation - 12 September 2026

Status: implemented and tested locally; not deployed or re-crawled by Semrush.

The supplied nine-page report lists issue totals and explanations, but does not include affected URL lists or per-field structured-data validation results. A read-only crawl of the live sitemap found 18 public URLs. Counts have changed since the report snapshot: for example, four current article titles exceed 70 characters, compared with three in the report.

| Report finding | Evidence and change | Verification / remaining action |
| --- | --- | --- |
| 13 invalid structured-data items | Twelve marketing pages received an incomplete WebApplication; the homepage also had its own WebApplication. Google software rich results require a genuine rating or review, and the shared entry also lacked offers. Replaced these with the existing service description, removed the dangling software reference, retained Organization/WebSite/Breadcrumb/FAQ data and real visible homepage prices. No reviews or ratings invented. | Local JSON parsing, service/breadcrumb checks, and visible-price agreement pass. The software-app count matched 13 entries, making it the likely cause; the report lacks field-level evidence. Confirm with Semrush after deployment. |
| 64 unminified JS/CSS occurrences | Forty local assets now pass through pinned esbuild during publish. Docker uses the same script in a dedicated build stage. The Google Fonts stylesheet is now a local CSS input; Google still serves the same WOFF2 font files. Source assets remain readable. | 434,959 bytes reduced to 343,648 bytes (21%). All 15 unique stylesheets/scripts referenced by public pages passed the HTTP minification check. Minified private scripts passed the account/publication browser suite. |
| 3 long titles | The live crawl found four long blog titles. Search titles now retain the brand and use a concise primary clause or word-boundary shortening. Full editorial headings and BlogPosting headlines are preserved. | Tests cover all five live article titles and an unusually long future title. Titles are at most 70 characters and differ from the H1. |
| 1 low-word-count page | The report does not identify its URL. The blog listing previously depended on article cards for meaningful content. Added practical photography/review guidance, including links to both regional guides, so the empty index is useful too. | All 13 public non-article pages exceed 200 words in their main content. Five additional isolated article fixtures exercise the article templates. Live articles already contain substantial text. |
| 4 pages with one incoming link | Live integration and several blog articles had only one linking page. Added integration/privacy links to the shared footer and three rotating article links below each article. The related-post query cycles through published slugs so older posts are not starved of links. | All 18 local sitemap URLs have at least two distinct incoming pages. A 25-article regression fixture proves older articles still receive three article links after leaving the first blog listing page. |
| 3 pages blocked from crawling | Account, security and demo/utility pages are deliberately excluded. The report does not identify its three URLs. | Private exclusions and authentication checks are retained. Review the exact Semrush URL export before classifying every notice; do not unblock private routes to chase a score. |
| 2 hosts without HSTS | Neither https://trophy.guru nor https://www.trophy.guru returned HSTS during the live check. Production responses now emit max-age=31536000 when the configured public origin is HTTPS, including alternate-host redirects. This accounts for Render terminating TLS before Kestrel. | Local production HTTP tests pass for canonical responses and alternate-host 301s. Confirm both actual edge responses after deployment. includeSubDomains is deliberately omitted so this does not change the TLS policy of unrelated subdomains. |
| 2 orphaned sitemap pages | Both UK and US catalogue guides had zero ordinary incoming HTML links. Regional script navigation did not provide crawlable anchors. | Both guides are now in the shared server-rendered footer and the blog guidance. Sitemap URLs and regional language alternates remain intact. |

## Validation completed

- 251 .NET tests passed, with no new compiler/analyser warnings.
- Release publish succeeded and its MSBuild target minified all 40 JS/CSS assets automatically.
- Existing 18 search-discovery checks passed; the final AEO suite passed sitemap, metadata, robots, redirects, internal links, desktop/mobile layouts, WebMCP and MCP tests.
- New read-only SEO smoke test passed first with 13 public pages and an empty blog, then with 18 pages including five isolated articles using the live article titles. It checks titles, descriptions, main-content length, canonicals, schema shape, incoming links, minification, HSTS and retained private exclusions. This is not a substitute for Semrush or Google's rich-result validator.
- All 18 pages checked at 390px and 1440px: no horizontal overflow or JavaScript errors; DM Sans and Newsreader loaded successfully.
- Existing account/publication browser suite passed against the minified release: signup/local email verification, club isolation, publication/withdrawal, billing, desktop/mobile archive and security, password reset/session revocation. Test data and email files were isolated under outputs/aeo-semrush; no real email or payments were sent.
- Docker was updated but a Docker engine was unavailable here. The equivalent .NET release publish and Node minifier were run directly; verify the Docker build in deployment.

## Release and acceptance

1. Review and deploy the source changes through the existing Render Docker workflow. No application data migration is required. The code is currently local only.
2. Check the root and www HTTPS response headers through the actual hosting edge. Run the read-only SEO smoke test against the deployed origin and use Google's Rich Results Test on representative pages.
3. Re-run Semrush with the same crawl settings. Export affected URLs and structured-data fields for any remaining findings. Confirm all actionable public-page errors and warnings are cleared; retain appropriate private-page exclusions.

## Sources

- Supplied file: Semrush-Site_Audit__Issues-trophy_guru-12th_Sep_2026.pdf, generated 12 September 2026.
- Live public sitemap/pages inspected on 12 September 2026; baseline and local QA output are in the ignored outputs/aeo-semrush directory.
- [Google software app rich-result requirements](https://developers.google.com/search/docs/appearance/structured-data/software-app): an eligible app needs pricing and a rating or review.
- [Schema.org Service](https://schema.org/Service): appropriate descriptive service properties, provider and offers.
- [esbuild minification](https://esbuild.github.io/api/#minify): the release asset transformation uses its parser, rather than regex whitespace removal.
- [ASP.NET Core HTTPS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-9.0): HSTS/HTTPS behaviour behind a reverse proxy.
