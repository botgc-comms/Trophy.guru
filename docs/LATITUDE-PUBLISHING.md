# Latitude / LinkArtemis publishing

This integration pulls completed articles directly into Trophy Guru's existing blog. It runs inside the existing web service: no Zapier, extra server, worker subscription or public publishing webhook is required.

## Configuration

On the existing Render service connected to `botgc-comms/Trophy.guru`:

- `LINKARTEMIS_API_KEY`: the workspace key from Latitude, stored only as a secret environment variable.
- `LINKARTEMIS_ENABLED=true`: enable automatic publication. Missing/false disables all provider requests.
- Optional `LINKARTEMIS_ARTICLE_IDS`: comma-separated source UUIDs. Empty means all completed English articles in the workspace. Use this if the workspace later contains content for other sites.

Restart/redeploy after changing environment variables. Removing the key or setting enabled=false stops future imports and retains already published posts.

## What happens

1. After startup, the site waits ten seconds, then fetches completed article summaries from `GET https://app.linkartemis.com/api/v1/articles` with `X-API-Key` authentication.
2. It paginates summaries and retrieves the full detail for each unseen selected source ID. A summary alone is never published.
3. It checks the ID, title, date, English language, slug and article body. HTML is sanitised with the existing blog pipeline. Scripts, styles, forms, frames and duplicate title headings are removed. Markdown is supported when HTML is absent.
4. It publishes articles at `/blog/{slug}` using Trophy Guru's layout, metadata, canonical URLs, related links and structured data. A supplied cover image is downloaded through the existing safe image pipeline and displayed on the article with intrinsic dimensions. Inline provider images are removed and blog listing cards remain text-only. An unavailable cover does not prevent article publication. The normal blog index and sitemap include the post automatically; the existing IndexNow monitor handles public discovery notifications where configured.
5. It checks again every 15 minutes. Up to 25 unseen details are handled per batch; larger backlogs continue on later runs. Requests are spaced at least 1.1 seconds apart, below the provider's 60/minute limit. HTTP 429 stops that cycle and its Retry-After is respected before retrying.

**All existing completed English articles are eligible on first activation.** Simon confirmed this workspace contains only Trophy Guru articles and authorised their automatic publication on 22 September 2026.

The documented API has no revision timestamp. Each source ID's article text is imported once, including across app restarts. Missing covers are backfilled on subsequent polls when Latitude supplies one, preserving the published text, title, URL and publication date. Existing covers are retained. Provider edits do not overwrite later website/editorial edits. A distinct provider ID cannot replace an existing slug. Deleting/revoking an article in Latitude does not remove the public website article. Landing-page creation is not supported by this importer; it publishes blog posts only.

## Status

The public `/health` response includes `latitude.configured`, `enabled`, `state`, `lastCheckedAt`, and the most recent completed batch's imported/skipped/imagesUpdated counts. It never includes keys, provider bodies, article identifiers or account details.

- `ok`: provider connection succeeded, including an empty article list.
- `completed_with_skips`: one or more articles failed validation or collided with another slug.
- `batch_limit`: batch reached 25 unseen detail responses; remaining work continues later.
- `scan_limit`: maximum 2,000 summaries reached; inspect workspace size/selection.
- `api_401`: invalid or revoked key. Replace the environment value and redeploy.
- `api_429`: provider rate limit; retry scheduled.
- `sync_failed`: temporary network, malformed data, payload-size or storage failure; see the redacted Render log category `LinkArtemisSync`.
- `disabled`: missing key or disabled setting.

A malformed/conflicting article is skipped and retried on later runs; it is not automatically renamed or allowed to overwrite another article. If a large run repeatedly hits its limit with rejected articles, fix those articles or use the source-ID allowlist to process the intended posts.

## Verification

Tests cover HTML cleanup, local cover downloads and dimensions, text-only listing cards, failed-cover retries, cover backfills preserving editorial changes, source/detail identity matching, retry idempotency across restart, later editorial preservation, collisions, pagination, source selection, language/date/body validation, Markdown fallback, 429 Retry-After and concurrent delivery. Only isolated fixture posts are created during tests.

The supplied key was successfully checked on 22 September 2026: the API returned an empty completed-article list. A subsequent completed article was imported; cover-image support was added later that day. This API fetches existing completed content; it does not create articles, configure Latitude's generation schedule or buy placements.

API contract: https://docs.linkartemis.com/article/17-linkartemis-api (retrieved 22 September 2026).
