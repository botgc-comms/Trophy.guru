# Read-only Google Search Console connection

This is a local owner tool, not a website feature or an installed Codex plugin. Codex can run it from this repository to analyse Trophy Guru's Google search performance. It uses Node 20+ and Windows user encryption; no extra npm packages are needed.

## Current state

Connected and verified on 14 September 2026 using Google Cloud project `trophy-guru-analytics`. Google confirmed read-only access to `https://trophy.guru/`. The first live performance report was successfully retrieved, and individual URL inspections returned Google index status. Credentials and private reports remain under the local storage paths below; they are not committed. The eight synthetic tests also pass. Access remains subject to Google consent and token expiry; run `status` to verify the current connection.

## One-time Google setup

The configured project is **Trophy Guru Analytics** (`trophy-guru-analytics`). The following steps document initial setup or reconnection with a replacement client. The project does not have to have the same name as the Search Console property.

1. Select that project in [Google Cloud Console](https://console.cloud.google.com/).
2. Open the API Library, search for **Google Search Console API**, and enable it if necessary.
3. In **Google Auth Platform**, configure the app's branding/support details if the project has no OAuth consent configuration. For a personal external app in Testing, add Simon's Google account under **Audience → Test users**. That account must already have access to the verified trophy.guru Search Console property.
4. Under **Data Access**, use only `https://www.googleapis.com/auth/webmasters.readonly` for this connection.
5. Under **Clients**, create an OAuth client with application type **Desktop app**, named **Trophy Guru Search Console reader**. Download its JSON to a local folder outside the repository, such as Downloads. An existing suitable Desktop client can be reused. Do not use the public website's Web application client.
6. Tell Codex the downloaded file's local path. The file contents do not need to be pasted into chat. Codex runs the auth command below; Simon completes Google's consent in the system browser.

Google's [Desktop OAuth instructions](https://developers.google.com/identity/protocols/oauth2/native-app) and [Search Console authorisation instructions](https://developers.google.com/webmaster-tools/v1/how-tos/authorizing) describe the underlying process. External apps in Testing can have refresh tokens expire after seven days for this scope; rerun authorisation when needed. This is not a promise of permanent unattended access.

## Connect and verify

From the Trophy.guru repository:

```powershell
node scripts/search-console.cjs auth --client "C:\Users\simon\Downloads\YOUR-DESKTOP-CLIENT.json"
node scripts/search-console.cjs status
node scripts/search-console.cjs report
node scripts/search-console.cjs inspect --url https://trophy.guru/demo
```

The authorisation listener binds only to 127.0.0.1 on a random port, checks state, uses PKCE and closes after consent or five minutes. Only the read-only Search Console scope is requested. The tool has no sitemap-submit, indexing-request, property-edit or other Google account write commands. URL Inspection retrieves Google's recorded index status; it does not run a live URL test or request indexing.

The connector selects the readable `sc-domain:trophy.guru` property first and otherwise `https://trophy.guru/`. An explicit `--property` can choose between those two. It never queries unrelated site properties. Google's OAuth grant itself is account-level Search Console read access; the local tool enforces the narrower property choice.

Credentials are encrypted with Windows CurrentUser DPAPI at `%LOCALAPPDATA%\TrophyGuru\SearchConsole\google-readonly.dpapi`. They are not placed in the repository, website deployment or report files. Access tokens are held in memory. Reports are saved locally under the same directory's `reports` folder. To revoke Google access, remove the application's connection in [Google Account connections](https://myaccount.google.com/connections); deleting the local encrypted file alone does not revoke Google's grant.

## Reports and interpretation

```powershell
node scripts/search-console.cjs report --days 28 --end 2026-09-11
```

Each report contains current and previous totals, daily data, queries, pages, countries, devices, query/page pairs and sitemap information. The default is 28 days ending three days before today's Pacific date to allow reporting lag, plus the immediately preceding 28 days. Requests use final Web Search data. Totals are requested separately from query tables because privacy filtering means query rows cannot reliably be summed to recover totals.

Dimension tables are limited to 1,000 rows and are not guaranteed complete exports. Empty rows are not proof of zero traffic. Average position is an aggregate, not a fixed rank. These data measure Google Web Search and do not establish visibility across every AI assistant.

Codex should verify `status`, fetch a first report, then explain the actual date range and property before treating it as the baseline. Compare clicks, impressions, pages gaining visibility and relevant commercial queries; do not claim a site change caused a movement from a single comparison.

## Verification

```powershell
node --test Tests/search-console.test.cjs
```

Tests use synthetic data and a local callback. They cover date boundaries, scope restrictions, Desktop client validation, allowed properties and URLs, separate totals, API error handling, state rejection and Windows encryption round-trip. No Google account data or live tokens are used by the tests.
