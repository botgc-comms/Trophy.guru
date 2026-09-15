# Writesonic → Zapier → Trophy Guru

Uses the existing Trophy Guru application, persistent blog database and site templates.

## Two-field Zap configuration

Trigger: **Writesonic → New Copy Published**.
Action: **Webhooks by Zapier → POST**.

- URL: https://trophy.guru/api/webhooks/writesonic
- Payload type: JSON
- Data: `mode` = literal `publish`; `text` = the dynamic **text** output from step 1.
- Wrap request in array: No. Unflatten: No.
- Headers: Content-Type = application/json; Authorization = Bearer followed by the existing BLOG_PUBLISH_TOKEN.
- No document ID, timestamp, Code step or additional service is required for text exports.

The copy/paste instruction for Zapier Copilot is in [docs/ZAPIER-PUBLISH-PROMPT.txt](docs/ZAPIER-PUBLISH-PROMPT.txt).

Use a real article intended for publication, including its title and body. A publish-mode action test publishes it immediately. Short dummy samples are rejected. Use `mode=validate` to check a real article without saving it, or `{"mode":"test"}` to check authentication alone.

Writesonic's documented export route is **Share → Export → Zapier → Send**. Sending the article starts automatic delivery through the enabled Zap; generating a draft alone does not publish it.
Official instructions: https://docs.writesonic.com/docs/integrate-with-zapier
Zapier action documentation: https://help.zapier.com/hc/en-us/articles/8496326446989-Send-webhooks-in-Zap-workflows

## Text handling

HTML, Markdown and plain text are accepted in `text`. The title comes from the main heading, document title or a short first paragraph/line. Include the title as the first line of plain-text exports. The receiver generates the slug and description, removes unsafe HTML and document styling, and renders the article through the existing blog template. Body text must contain at least 80 visible characters after cleaning to reject short test samples; this is not an editorial quality check.

Identical text deliveries are deduplicated using a content hash. A changed export with the same title/slug returns 409 instead of replacing the existing article. For deliberate revisions, use the advanced contract below. Text-only exports do not carry a stable provider document identity.

Posts appear at /blog/{slug}, with the existing canonical URL, BlogPosting metadata and sitemap. Inline images are omitted unless a cover URL is supplied through the optional fields below.

## Optional advanced contract

Existing integrations can still send `documentId`, `updatedAt` (source ISO timestamp with timezone), `title` and `contentHtml`, with `mode=publish` or `validate`.
A stable document ID and source timestamp support revisions; do not substitute Zap execution time.
Optional fields for either contract: `slug`, `metaDescription` (up to 320 characters), `heroImageUrl` (public HTTPS) and `heroImageAlt`.
An explicit `title` can override the extracted title in a text export.
When a document ID is explicitly supplied with text, a source `updatedAt` is required.

IDs use a separate negative integer range from AutoSEO. The full source identity is retained and checked before writing. Retries do not duplicate posts; older explicit revisions are ignored. Later explicit revisions preserve the original URL and publication date. A different document cannot overwrite another post's slug.

## Hosting and verification

Set BLOG_PUBLISH_TOKEN (at least 32 characters) on the existing app and use the same key in Zapier. Never include it in public URLs or source control. Missing configuration returns 503; incorrect authentication returns 401. Maximum JSON body size is 2 MiB.

Validation saves no article or image. Cover images use the existing protected downloader and local storage. Image download failure returns 500 without replacing a previous post. Include the existing blog directory in backups.

Checks:
- `dotnet test Tests/Trophy.Catalogue.Tests.csproj`
- `node Tests/blog-publishing-smoke.cjs` after a Debug build. This publishes fixtures only to an isolated local app.
- `scripts/test-blog-publishing.ps1` checks the live connection using the key from the environment.

Repository tests and live receiver checks do not prove a hosted Zap is enabled. Confirm the actual Zap state and one real Writesonic delivery before calling the whole integration complete.
