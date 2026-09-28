# Writesonic AI traffic analytics

Uses the existing Render web service; no additional service or subscription is created.

- Set `WRITESONIC_ANALYTICS_API_KEY` in Render Environment (never commit it).
- Optional kill switch: `WRITESONIC_ANALYTICS_ENABLED=false`, then redeploy.
- Fixed destination: `https://ingestion.writesonic.com/api/v1/analytics/ingest`, header `x-api-key`, JSON array. Redirect following is disabled.
- Source: https://docs.writesonic.com/docs/integrating-with-custom-log-drain

The server observes GET/HEAD requests identifying as supported AI/search crawlers on allowlisted public marketing/discovery pages and blog slugs. It records actual response status including errors. User-agent identity is a claim, not verified ownership. Requests blocked at Render's edge or served from an upstream cache cannot be observed here. Private/account/archive/honours/upload/API routes are excluded.

Human public-page visits are sent only after browser analytics consent. Consent version 2 explains the addition of Writesonic and asks previous visitors again. The browser sends only a public path and referring origin; the server adds request metadata. Human beacons describe successfully loaded pages, not conversions or revenue. No Writesonic key reaches the browser.

Data: IP address, user-agent, available proxy country, public page URL, referring origin, method and status. Page queries/fragments and referrer paths/queries are excluded. Render proxy metadata is used only for reporting, never access control. No cookie, authorization header, account or archive content is forwarded.

A bounded memory queue holds up to 512 events, batches up to 50 every five seconds, and uses a ten-second timeout. On provider failure it backs off 30 seconds. Failed/overflow events and events remaining at restart can be lost; this is best-effort analytics, not an audit log. No event payloads or secrets are logged.

Verification: `/health` exposes `writesonicAnalytics.enabled`, `state` and `lastAcceptedAt`. `ok` indicates the ingestion endpoint accepted a batch, not proof of an AI recommendation or completed dashboard processing. In Writesonic click **Verify Integration** after a batch is accepted. Do not manufacture AI bot requests as evidence of real crawler traffic.

Approved by site owner on 28 September 2026: send the listed metadata to Writesonic for identified crawlers and consenting human visitors.
