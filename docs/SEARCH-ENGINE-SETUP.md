# Search engine setup

Implementation: 10 September 2026. These account and production steps have **not** been completed by this change.

## Deploy and inspect

Deploy the normal Docker/Render service with `PUBLIC_SITE_URL=https://trophy.guru`. The existing Render configuration already sets that origin. Marketing pages are initial HTML, not client-only content. Check `/`, all seven new guides, `/privacy.html`, `/blog`, `/robots.txt`, `/sitemap.xml`, `/llms.txt` and `/mcp` on the actual deployed host. Verify public HTML has no `X-Robots-Tag: noindex`; account/API/board routes should retain it. Unknown pages and missing blog posts should return 404. Inspect hosting/CDN settings too: local tests cannot prove production firewall or TLS behaviour.

## Google Search Console

Use a Domain property for trophy.guru and add the exact DNS TXT value supplied by Google, or use a URL-prefix property with `GOOGLE_SITE_VERIFICATION` set to the supplied HTML-tag content value. Never invent verification tokens. The repository contains an existing homepage verification tag; it is not evidence that the current owner account is verified. Confirm ownership in the console, submit `https://trophy.guru/sitemap.xml`, and inspect the five priority pages in AEO-PAGE-METADATA.md. Watch coverage, canonical selection and Core Web Vitals after deployment. The general site is not an appropriate use of Google's specialised Indexing API.

## Bing Webmaster Tools

Import the verified Search Console property or complete Bing's DNS verification. The application also accepts the supplied HTML-tag value as `BING_SITE_VERIFICATION`. Submit the sitemap and inspect indexing/crawl errors. Monitor Bing AI Performance where available in the account; independent citation and indexing decisions remain with the provider.

## IndexNow

The previous checked-in key has been removed from the deployed source. Generate a new key once outside source control (for example `[guid]::NewGuid().ToString('N')` in PowerShell) and store it in Render as `INDEXNOW_KEY`. Allowed format is 8–128 letters, digits or hyphens. The application serves its contents at `/{key}.txt`; this ownership proof is necessarily public but must not contain any unrelated credential.

Set `INDEXNOW_ENABLED=true` to enable the hosted publisher. It waits 30 seconds after startup, verifies the public ownership file and sitemap, then checks each page's response, canonical and indexability. It submits up to 10,000 allowlisted public URLs when the rendered content digest changes. It rechecks after about 15 minutes, so both deployments and published blog edits are detected. A digest in DATA_PATH avoids resubmitting unchanged content after a restart. CSP nonces are excluded from the digest. An unsuccessful check or notification is logged without the key and retried at the next interval; it does not stop the website. Removed URLs leave the sitemap; explicit deleted-URL notification is not implemented.

The monitor submits the current public set when a change is detected; it is intended for this small site, not a large publishing platform. Review its polling/submission strategy before scaling to thousands of pages. Rotate the key by replacing the environment value and redeploying. To submit manually after deployment, run `node scripts/submit-search-indexing.cjs` with the same `INDEXNOW_KEY` in the environment. No real submission was made during QA. HTTP 200/202 confirms receipt or pending verification, not indexing. Google setup remains separate. [Protocol reference](https://www.indexnow.org/documentation).

## OpenAI and other crawlers

Robots explicitly permits OAI-SearchBot, ChatGPT-User, GPTBot, Googlebot and Bingbot on public pages, using the same exclusions as the wildcard group. Keep required JS/CSS/images accessible. Check the hosting firewall against the current published crawler IP ranges; do not trust the user-agent string as proof of identity. OAI-SearchBot controls search access; GPTBot is separate training access. ChatGPT-User represents user-initiated retrieval, which may not follow robots. Authentication must continue to protect private records. [Official crawler reference](https://developers.openai.com/api/docs/bots).

## GA4 reports

Keep the existing GA4 property (`G-8GMHWE0WLH`); no new analytics platform is required. After consent, public page views retain bounded `utm_source`, `utm_medium` and `utm_campaign` labels. The browser URL is not rewritten. Arbitrary query parameters, tokens and search terms are excluded. External referrers retain their origin only. Never put personal information in campaign labels.

In GA4 Traffic acquisition, filter Session source for `chatgpt.com` and inspect Session source/medium. Create an Exploration with Session source/medium, Landing page + query string and Event name. Compare ChatGPT with observed sources such as Bing, Copilot, Perplexity or Claude; add only sources that actually occur in your data. Use `signup_click` as a public signup-intent event and optionally mark it as a key event. It measures the click, not successful registration or a booked demo. Account/archive pages remain unmeasured; this change does not claim end-to-end private conversion attribution. Consent denial also means a visit is not measured.

`llms.txt` is a compact supplementary public guide, not a ranking mechanism or a promise that a provider will ingest it. No llms-full.txt was added because the existing concise guides already supply the useful public material.
