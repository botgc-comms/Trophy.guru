# Writesonic → Zapier → Trophy Guru

This uses the existing Trophy Guru application, persistent blog database and blog templates. No separate hosting service, database or WordPress site is required. The AutoSEO receiver remains compatible.

## Current delivery route

Writesonic's documented trigger is **New Copy Published**. In its editor, Share → Export → Zapier → Send selects a publishing destination. This explicit send starts automatic delivery; generating a draft alone does not publish it.

Official instructions: https://docs.writesonic.com/docs/integrate-with-zapier
Zapier HTTP action: https://help.zapier.com/hc/en-us/articles/8496326446989-Send-webhooks-in-Zap-workflows

## Production configuration

Set `BLOG_PUBLISH_TOKEN` on the **existing** Render service to a newly generated secret of at least 32 characters. Store the same value in the Zap's Authorization header, prefixed with `Bearer `. Keep it out of source control, public URLs and article fields. Without the setting the receiver returns 503; wrong credentials return 401. There is no change to existing AutoSEO credentials.

The endpoint is `POST https://trophy.guru/api/webhooks/writesonic`.
Use `Content-Type: application/json`. Maximum request size: 2 MiB.

## Zap configuration

Name: **Writesonic → Trophy Guru blog**

1. Trigger app: Writesonic. Event: **New Copy Published**.
2. Connect the Writesonic account using its Zapier integration key.
3. Publishing destination: **Trophy Guru blog**.
4. Load an actual sent article as the trigger sample. Do not mistake a test sample, export-event ID or title for a stable document ID.
5. Action app: **Webhooks by Zapier**. Event: **POST**. URL: the endpoint above.
6. Payload type: **JSON**. Use the Data key/value fields below so Zapier escapes article HTML safely. Do not hand-interpolate HTML into raw JSON. Wrap request in array: No. Unflatten: No.
7. Header: `Authorization` = `Bearer <BLOG_PUBLISH_TOKEN>`. Content-Type: application/json.
8. Begin with `mode=validate`. A successful test must return `status=validated` and `published=false`. It saves neither articles nor images.
9. After checking the mapped fields, change mode to `publish`. This action immediately publishes; testing that mode publishes too. Use a real article intended for release, not Zapier's dummy sample.
10. Confirm the returned URL and the page in /blog and /sitemap.xml, then enable the Zap. Configure failure notifications/replay in Zapier; a failed or 429 delivery can be retried.

These are **our endpoint field names**, not claims about the names returned by Writesonic. Map them to the actual trigger sample:

| Data key | Value |
|---|---|
| mode | validate during setup; publish when activated |
| documentId | Stable Writesonic document identifier; required |
| updatedAt | Source document revision timestamp with timezone, e.g. 2026-09-15T12:00:00Z; required |
| title | Article title; required, up to 500 characters |
| contentHtml | Article HTML body; required |
| slug | Optional permanent URL slug; otherwise generated from title |
| metaDescription | Optional description, maximum 320 characters; derived from visible text if absent |
| heroImageUrl | Optional public HTTPS cover image URL |
| heroImageAlt | Optional description of the cover image |

Do not map the current Zap execution time into updatedAt: delayed/retried deliveries must retain their source revision timestamp. If the trigger does not include a stable document ID and revision time, field mapping is **not ready**; obtain them from its document data or explicitly supply a revision in a normalization step after inspecting the sample. Do not silently substitute an event ID or generate an identity from the title.

## Connection test

An authenticated POST body `{"mode":"test"}` returns `{"status":"connected","published":false}` without saving anything. This verifies authentication and routing, not content mapping.

`scripts/test-blog-publishing.ps1` performs this check. It reads the secret from the process environment and never prints it. Its optional ArticlePath runs validation only.

## Existing blog behavior

Published content uses /blog/{slug}, the site's existing layout, canonical URLs, BlogPosting metadata and sitemap. Document navigation, styles, scripts and duplicate article headings are removed. The article's cover image uses the existing protected download and local image storage. Other inline images are omitted with a warning; this first version does not import galleries or generated full-page designs.

Stable provider IDs map to a separate negative integer range in the existing blog table, leaving positive AutoSEO IDs intact. The full document ID is retained and checked for hash collision before writing. Same document and same revision are acknowledged without a duplicate. Older revisions are ignored. Different content with the same revision returns 409. A later revision preserves the original public URL and publication date. A new document requesting another article's slug also returns 409 instead of replacing it.

Validation does not download the image. An image failure during publication returns 500 and leaves the previous article intact. No account, trophy or billing store is changed. Include the existing persistent blog directory in the normal backups.

## Implementation checks

- `dotnet test Tests/Trophy.Catalogue.Tests.csproj`
- `node Tests/blog-publishing-smoke.cjs` after a Debug build; starts a loopback-only app with an isolated temporary data directory.

## Activation status

The repository supplies the receiver, tests and exact Zap recipe. This document is not an imported or activated Zap. Production still requires deployment, the secret setting, an authenticated Zapier/Writesonic connection and a real delivery test.
