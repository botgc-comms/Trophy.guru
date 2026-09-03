# trophy.guru

A mobile-first archive for recovering the names and years engraved on historic trophies.

The application now provides a complete customer entry journey:

- individual account signup and sign-in using protected, persistent ASP.NET Core sessions;
- required club onboarding with club name, sport, country, optional website and club logo;
- a private catalogue, member directory, uploads and illustrations for each club;
- a photo-first new-trophy wizard that creates the record, uploads one or more angles, starts inscription reading in the background and automatically generates the catalogue illustration;
- repeated photographs, batch uploads and complementary rubbings;
- editable winners, manual missing-year entry and confirmed human review;
- CSV, TSV, XML or XLSX member import with birth and joining dates reduced to year only;
- age-aware fuzzy member matching; and
- CSV archive export.

Every new club starts with an empty, isolated collection. Legacy single-club data is preserved on disk and is never assigned to an ordinary signup. The separate “Original club archive” login uses the former APP_PASSWORD to reconnect the original owner to that preserved catalogue.

## Run locally

Requires the .NET 9 SDK.

```powershell
$env:OPENAI_API_KEY = "your-project-key"
dotnet run
```

Open `http://127.0.0.1:5173` for the product page, or `http://127.0.0.1:5173/archive.html#signup` to create an account. Without an OpenAI key, accounts, club setup, uploads, imports and manual editing still work; AI reading and illustration generation remain disabled.

Local account records, protected session keys, club logos, catalogues, member matches, uploads and generated illustrations are stored under `data-store/`, which is deliberately ignored by Git.

## AI configuration

- `OPENAI_MODEL` selects the engraving reader model.
- `OPENAI_IMAGE_MODEL` selects the image model and defaults to `gpt-image-2`.
- `OPENAI_IMAGE_SIZE` and `OPENAI_IMAGE_QUALITY` control generated illustration output.
- `TROPHY_ILLUSTRATION_PROMPT` replaces the built-in museum-catalogue prompt. Include `{{trophy_name}}` if the name should be inserted.
- `ANALYSIS_DEBOUNCE_SECONDS` controls how long the background reader waits after the most recent upload so a phone user can add several photographs first.

Evidence uploads are sent to OpenAI only for engraving analysis or illustration generation. The engraving reader sets `store: false`. Human-confirmed winner records are not silently replaced by later automatic readings.

## Release to Render

This is the standalone trophy.guru repository. Its Render Blueprint is render.yaml at the repository root.

To preserve the existing accounts, uploaded evidence and confirmed winners, connect the existing Render service to this repository rather than creating a second service. Keep its existing /var/data persistent disk attached. A newly created Render service would start with a new, empty disk.

Render asks for one secret: `OPENAI_API_KEY`. The Blueprint builds the Docker image, deploys in Frankfurt, checks `/health`, and mounts a 5 GB persistent disk at `/var/data`. The persistent disk is essential: it stores account data, the ASP.NET Core data-protection key ring, club identities, evidence and generated output.

The current topology intentionally uses one Render instance and its attached disk. It is suitable for product validation and small-scale launch, but billing should remain disabled until the database/object-storage, email verification, password recovery, subscription entitlements, backups and account-deletion gates in [COMMERCIAL-LAUNCH.md](COMMERCIAL-LAUNCH.md) are complete.

Render supplies its RENDER_EXTERNAL_URL automatically for canonical links, Open Graph metadata, robots.txt and sitemap.xml. When a custom domain is connected, set PUBLIC_SITE_URL to that preferred HTTPS origin (for example, https://trophy.guru) so every search signal points to the custom domain rather than the retained onrender.com address.

## Security and privacy boundaries

- Passwords are hashed with ASP.NET Core's password hasher; plaintext passwords are never stored.
- Account cookies are HTTP-only, same-site and secure in production. Their data-protection keys persist on the Render disk so restarts do not invalidate every session.
- Catalogue, member and image paths are resolved from the authenticated club on the server; a trophy identifier alone cannot cross into another club.
- Full birth and joining dates are reduced to year only during import, and the uploaded member file is not retained.
- Keep Render disk snapshots and periodically download archive CSV exports.
