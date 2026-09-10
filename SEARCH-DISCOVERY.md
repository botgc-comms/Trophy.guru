> This is the historical 7 September release note. For current implementation and setup see [AEO checklist](docs/AEO-CHECKLIST.md), [audit](docs/AEO-SEO-AUDIT.md) and [search setup](docs/SEARCH-ENGINE-SETUP.md). The old four-URL count and checked-in IndexNow key no longer apply.

# Search discovery release — 7 September 2026

The four marketing pages are served as readable HTML without login or JavaScript. The homepage design and visible copy are retained. Metadata uses Trophy Guru, with Trophy.guru and the former Trophy Archive AI name linked in WebSite structured data. UK/US guide alternates point to the same regional set, and the planned integration is explicitly described as in development.

Public canonical paths and alternate hosts redirect to the configured PUBLIC_SITE_URL. Private routes and health checks are not redirected. The sitemap includes only the four marketing pages; container build timestamps are no longer reported as editorial last-modified dates. robots.txt permits all crawlers on public pages, including search/AI crawlers, and retains the private API exclusion. No private records are added to the sitemap.

## Validate

Run dotnet test Tests/Trophy.Catalogue.Tests.csproj. Run node Tests/search-discovery-smoke.cjs against an isolated local server with QA_BASE_URL and QA_PUBLIC_ORIGIN set to its origin. Set GOOGLE_SITE_VERIFICATION=qa-google-proof and BING_SITE_VERIFICATION=qa-bing-proof on that test server. For read-only live verification set QA_BASE_URL=https://trophy.guru and QA_PUBLIC_ORIGIN=https://trophy.guru.

The HTTP audit covers all four pages, JSON-LD, visible/structured pricing agreement, canonicals, language alternates, robots, sitemap, redirects, HEAD requests, private-page exclusion and eight crawler user agents. Sending a crawler user-agent from a test network does not prove access from the provider's own IP addresses.

## Submit the deployed pages

After deployment, run node scripts/submit-search-indexing.cjs. It verifies the live ownership file, sitemap, canonical URLs and indexability before sending the four public URLs to IndexNow. IndexNow shares submissions with participating search engines. HTTP 200 means received; 202 means ownership validation pending. Neither confirms indexing. It does not submit to Google or to a universal AI index.

## Google and Bing owner access

Open Google Search Console for https://trophy.guru/. An already-verified property needs no new verification. Otherwise use DNS verification for the domain property, or use the URL-prefix property's HTML-tag verification: set GOOGLE_SITE_VERIFICATION in Render to the content attribute supplied by Google, deploy, then verify. The application renders the escaped value in the marketing page head. Submit https://trophy.guru/sitemap.xml and request indexing of the four public pages using URL Inspection. Google does not offer an anonymous sitemap-ping replacement; its specialised Indexing API is not appropriate for these pages.

For Bing Webmaster Tools, import the verified Google property or set BING_SITE_VERIFICATION to the provided msvalidate.01 value and verify. Submit the same sitemap. Do not invent ownership tokens or mark properties verified without account evidence.

For ChatGPT, allow OAI-SearchBot and the published searchbot IP ranges through any hosting firewall. Current robots rules allow access. Check genuine crawler visits and errors in hosting logs. Training-bot access is separate from search inclusion. Other AI providers decide independently whether to crawl, retrieve or cite a page.

Official references:
- https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap
- https://developers.google.com/search/docs/appearance/ai-features
- https://developers.openai.com/api/docs/bots
- https://www.indexnow.org/documentation

## Release validation

7 September 2026: 179/179 .NET tests and 18/18 isolated HTTP discovery checks passed. The bodies of all four marketing pages are unchanged from the release base; all HTML edits are in the head. No CSS, application data or billing configuration is changed by this release.
