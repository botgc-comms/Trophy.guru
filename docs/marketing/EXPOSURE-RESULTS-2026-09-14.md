# Trophy Guru exposure work — verified results, 14 September 2026

The website changes below are deployed and have been checked against **https://trophy.guru/**. Social posts and outreach emails are prepared, not published or sent. No ranking improvement is claimed from these implementation checks.

## Live changes

| Work | Result |
|---|---|
| Public product demonstration | [Explore the demo](https://trophy.guru/demo): actual honours-board interface, three browsing views, no account required, clearly fictional example records. |
| Practical committee resource | [Trophy archive project plan](https://trophy.guru/trophy-archive-project-plan): pilot, photography, review roles, work estimate and handover checklist. |
| Commercial landing pages | Stronger product evidence, pricing and trial links on [electronic honours boards](https://trophy.guru/electronic-honours-boards), [golf clubs](https://trophy.guru/for-golf-clubs), [transcription](https://trophy.guru/digitise-trophy-records) and [workflow](https://trophy.guru/how-it-works). |
| Existing page with Google impressions | Kept the [UK guide’s URL](https://trophy.guru/uk/how-to-catalogue-trophy-winners/) and connected its useful method to the demo, product page and committee plan. |
| Seven existing articles | Replaced unsupported claims with practical, checked content at the same URLs. Removed fabricated accuracy, model-training, customer and fixed-speed claims. Original public JSON is backed up; later CMS revisions remain eligible to supersede these versions. |
| Blog rendering | Existing FAQ answers are not duplicated; FAQ structured data uses the visible answer. Blog pagination receives distinct titles. |
| Sharing and attribution | Product social preview image plus consent-based demo, pricing and signup-click tracking. Campaign tags are bounded and personal query fields remain excluded. |
| Image loading | Deferred a 1,893,158-byte decorative trophy PNG previously downloaded immediately. Losslessly reduced the logo from 304,598 to 200,632 bytes and the integration background from 230,506 to 98,474 bytes. Original images retained. |
| IndexNow | Active ownership proof verified. Production reports automatic notifications enabled/configured. All 22 public URLs received **HTTP 200 at 09:00:25 UTC on 14 September**. [Actual receipt](indexnow-receipt-2026-09-14.json). |

The deployment uses an IndexNow key supplied by its hosting environment, different from the repository default. The manual submitter now discovers and verifies the active public proof from `/health`. Empty hosting placeholders have a canonical-site fallback; an explicit `INDEXNOW_ENABLED=false` is still respected.

## Verification evidence

- **255 .NET tests passed.** Includes version-specific article migration, original-content backup, preservation of future CMS revisions, FAQ rendering, and indexing defaults/disable behaviour.
- The minified release and live site both passed an audit of **22 sitemap pages**: HTTP 200, canonical and indexable public HTML, unique concise titles and descriptions, one H1, valid JSON-LD, readable content, and at least two incoming public-page links per page. Fifteen shared CSS/JS assets were minified.
- Live browser checks passed for the seven revised article URLs, modification dates, real demonstration navigation, 390px/1440px layout, image loading and call-to-action contrast.
- Existing crawler-access and MCP/WebMCP checks passed on the isolated release. Googlebot, Bingbot and OAI-SearchBot user-agent requests are permitted; this does not prove a visit from a genuine provider IP or actual indexing.
- Analytics checks used mocked networks: no real GA events were sent. Consent, withdrawal, bounded campaign tags, demo/pricing/signup intent and private-page exclusions passed.
- A single controlled mobile-browser sample (390px viewport, 150 ms latency, 1.6 Mbps download, 4× CPU slowdown) showed the homepage load event moving from about **15.3 seconds to 4.7 seconds** after the image fixes. This is a limited lab comparison, not a Core Web Vitals field result. Google’s public PageSpeed API returned a quota error, so no PageSpeed score is claimed.

Local screenshots and machine-readable audits are under `outputs/exposure-2026-09-14/`. They are excluded from Git and production image builds. Customer data was not used for the demonstration or browser tests.

## Ready for distribution

[Open the launch pack](EXPOSURE-LAUNCH-2026-09-14.md): three LinkedIn posts, a Facebook/community version, a club introduction, personalised pitches for The Golf Business and Club Mirror, an association enquiry, a newsletter article, company-profile copy and five verified outreach routes.

[Open the keyword and publishing brief](EDITORIAL-AND-KEYWORD-BRIEF.md): target search themes mapped to existing landing pages, plus an exact brief for the upstream article publisher. Search volumes and keyword difficulty scores have not been invented.

## Account actions that remain

The desktop browser connection repeatedly failed before it could expose the signed-in browser. No connected Search Console, Bing Webmaster, email or social account tool was available. These account actions were therefore not performed:

1. **Google Search Console:** in the verified trophy.guru property, check the sitemap is successful at `https://trophy.guru/sitemap.xml`. Inspect `/demo`, `/electronic-honours-boards` and the existing UK guide, and request indexing after a successful live test. In **Settings → Search generative AI**, check the site is included. Google says inclusion is the default, but the actual account setting has not been inspected. [Official control documentation](https://support.google.com/webmasters/answer/16908024).
2. **Bing Webmaster Tools:** if the site is not already present, import the verified Search Console property and submit the same sitemap. IndexNow submission is complete independently of that account setup. [Bing’s setup guidance](https://blogs.bing.com/webmaster/June-2025/Start-Using-Bing-Webmaster-Tools-to-Improve-Your-Site-Visibility).
3. **Distribution:** publish the first prepared LinkedIn post with the launch image and send the prepared, personally selected club/editorial introductions from Simon’s accounts. Nothing has been sent on Simon’s behalf. Apply the supplied editorial brief in the upstream AutoSEO account before commissioning further automated content; the external publisher’s settings were not changed by committing the brief.

A genuine, authorised club pilot is the next useful proof asset. The current demonstration does not pretend to be a customer case study. A relevant independent mention and a real club trying a trophy are more informative next milestones than adding more generic blog volume.

## Limits of this result

An IndexNow receipt confirms notification, not crawling, indexing, ranking or an AI recommendation. Google Search Console’s supplied screenshot showed 29 impressions and one click; it is not a verified 28-day baseline. The screenshot had three months selected and displayed data for 6–11 September.

The production root emits HSTS, but the hosting edge’s `www.trophy.guru` redirect previously lacked that header. Hosting-edge configuration and a new Semrush account audit were not available in this session; a zero-issue Semrush score is not claimed.
