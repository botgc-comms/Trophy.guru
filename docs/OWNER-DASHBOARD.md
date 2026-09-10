# Owner dashboard

Visit https://trophy.guru/admin. This unlisted page is not linked from the public site or included in its sitemap. Knowing the URL grants no access to data.

## One-time setup

1. Choose your existing Trophy Guru account. The original archive account (`archive@botgc.test` by default) is supported with its existing archive password. Email verification is not required for admin access.
2. In the Trophy Guru Render service, set `SITE_ADMIN_EMAIL` to that account's exact email address. Only one address is supported. This setting is an allowlist, not a password.
3. Redeploy, then sign in at `/admin` using that account's existing email and password.

No configured address means no administrative access. Configure the address of an existing account you control. Admin access requires that exact configured account and its correct password; club-owner roles alone do not grant access. Do not configure an unregistered address that someone else could claim through signup. Changing/removing the setting revokes access after the deployment takes effect.

## Behaviour

The dashboard lists registrations newest first, including unfinished club setup, with name, email, date, verification status and club. Search by name, email or club. Open a club to see its trophies, engraving evidence and trophy photos, upload filenames and dates. Originals open through an authenticated route. Uploaded images are associated with a club; existing storage does not identify which individual club member uploaded each image.

There are no delete, edit or impersonation controls. The gallery includes archived trophies. It does not display generated illustrations or member directories. Missing image files show an unavailable message.

## Security and operations

The separate admin cookie is HTTP-only, SameSite Strict, secure in production, non-persistent and expires after 30 minutes without renewal. Ordinary archive cookies do not grant admin access. The configured account and its security version are checked on every admin data/image request. Password changes and session revocation invalidate the admin session. Login is rate limited, mutations enforce same-origin requests, and all admin pages/data/images have no-store and noindex headers. No analytics is loaded. Account passwords, reset/verification tokens and storage paths are never sent to the dashboard. Login and club-gallery access are logged using internal IDs.

Customer account and archive APIs retain their existing tenant checks. The admin gallery explicitly selects only an existing club and uses the existing catalogue and image retrieval methods; it never accepts a filesystem path from the browser.

Use a strong unique account password. This version uses the existing account password and explicit Render email allowlist; it does not add MFA. Review the site's privacy notice to ensure its description of authorised support/administrative access matches operations.

The site owner explicitly authorised the environment allowlist to replace email verification for admin access. This does not mark the account email verified or change verification requirements for customer archive operations.
