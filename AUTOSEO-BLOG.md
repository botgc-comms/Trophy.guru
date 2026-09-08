# AutoSEO blog

Public pages: `/blog` and `/blog/{slug}`. Webhook: `POST /api/webhooks/autoseo`.

## Configuration

In the existing Render service's environment settings, set:

- `AUTOSEO_WEBHOOK_TOKEN` to the exact bearer token supplied in AutoSEO (without the `Bearer ` prefix).
- `PUBLIC_SITE_URL=https://trophy.guru` (or your actual HTTPS custom domain).
- Optional: `AUTOSEO_REQUIRE_SIGNATURE=true` to require the HMAC header on all requests. A supplied signature is always verified, even when this is false.

Set AutoSEO's destination to `https://trophy.guru/api/webhooks/autoseo` and its authorization token to that same value. Deploy the application on the existing service with its existing persistent disk. No existing database migration is needed for the blog.

For local development, set those environment variables in the terminal before `dotnet run`; `.env.example` is documentation and is not loaded automatically. Use a separate temporary `DATA_PATH` when testing so existing accounts and archives are never involved. Keep PUBLIC_SITE_URL set to an HTTPS origin: webhook responses intentionally never derive URLs from untrusted request headers.

## Delivery behaviour

- Missing/wrong authorization: 401. No token configured: 503 (fails closed).
- The exact `Bearer <token>` value is checked in constant time. Optional HMAC-SHA256 is checked over the original request bytes; use a 64-character hexadecimal digest.
- Authenticated `{"event":"test"}` returns `{"url":"https://trophy.guru/test"}` without saving anything. This is a connection-test response, not a published page.
- `article.published` and `article.updated` upsert by the AutoSEO integer ID. Both can create the first version. Duplicate/older timestamps are acknowledged without replacing newer content.
- First publication reserves a permanent slug. Later slug changes preserve that URL. A slug already belonging to another ID gets an ID suffix, so translated articles cannot overwrite each other.
- Malformed JSON/invalid article fields or events return 400; wrong content type returns 415; requests over 2 MiB return 413. Storage, image fetch, and other publication failures return 500 for retry. A failed download never replaces the previous post.
- Hero and infographic images are downloaded into persistent local storage (PNG, JPEG, WebP or GIF; maximum 12 MiB each; 30-second download timeout). HTTPS redirects are bounded and each destination is checked. Private networks, credentials in URLs, nonstandard ports and non-HTTPS URLs are rejected. File signatures and MIME types must agree.
- Matching images embedded in article HTML are rewritten to their local paths. Other embedded remote images are omitted, so article content never hotlinks. Original HTML, Markdown and all supplied metadata remain in the stored payload. Rendered HTML uses a strict sanitizer with no scripts, event handlers, styles, forms or embedded frames.
- Blog pages include canonical URLs, social metadata, BlogPosting structured data and visible FAQs with FAQ structured data when supplied. Blog and article URLs are included in `/sitemap.xml`.

## Data preservation

Only `DATA_PATH/blog/autoseo.sqlite` and `DATA_PATH/blog/images/` are used by the blog. The new database uses `CREATE TABLE IF NOT EXISTS autoseo_posts`. Its upsert updates only blog rows introduced by this feature. No existing databases, tables, columns, records or RLS policies are migrated, altered or deleted. Old media files are retained; include the new `blog` directory in full-directory backups and storage monitoring. Existing application startup behaviour is unchanged; use an isolated DATA_PATH for all smoke tests.

The blog is visible before its first delivery and shows an empty state. Production publishing starts after deployment and environment configuration; a local code change alone does not activate the hosted endpoint.
