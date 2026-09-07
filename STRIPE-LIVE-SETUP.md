# Connect Trophy.guru to live Stripe payments

Prepared 7 September 2026. Website deployment is separate from payment activation. The live billing endpoint currently returns `payments_unavailable`.

## Completed verification

- 168 automated tests pass, including simulated live-mode Stripe Checkout for 250, 300 and 500 trophies.
- The server sends £625, £750 and £1,250 respectively; the browser cannot choose an arbitrary amount.
- Opening Checkout does not grant credits. A signed notification is verified against the Stripe Checkout session before credits are added.
- Checkout retries reuse the existing session; duplicate payment notifications do not add credits twice.
- No real payment or real-account activation has been verified yet.

## Stripe account

Complete live account activation, including any outstanding business/identity verification and payout-bank details. Confirm payments and payouts are enabled in Stripe.

The app creates one-off trophy-credit line items itself. Do not create a separate product catalogue for the credit packs. The optional annual Intelligent Golf subscription stays disabled.

## Live webhook

In Stripe Workbench → Webhooks, add an event destination for your own account (not connected accounts):

- URL: `https://trophy.guru/api/billing/webhook`
- Payload: snapshot events, not thin events
- API version: `2025-02-24.acacia` where selectable, matching the integration's API requests
- Events for the one-off trophy-credit launch:
  - `checkout.session.completed`
  - `checkout.session.async_payment_succeeded`
  - `checkout.session.expired`
  - `charge.refunded`
  - `charge.dispute.created`
  - `charge.dispute.updated`
  - `charge.dispute.closed`
  - `charge.dispute.funds_withdrawn`
  - `charge.dispute.funds_reinstated`

Copy this live destination's signing secret into Render. Do not use a Stripe CLI forwarding secret or a test-mode destination's secret.

## Render environment settings

Open the existing Trophy.guru service → Environment. Do not create a replacement service or change its persistent disk.

| Name | Value |
| --- | --- |
| PUBLIC_SITE_URL | https://trophy.guru |
| STRIPE_SECRET_KEY | The live secret key, starting sk_live_ |
| STRIPE_WEBHOOK_SECRET | The live webhook signing secret, starting whsec_ |
| BILLING_MODE | live, when the customer information below is complete |
| BILLING_LIVE_APPROVED | true, when live activation is confirmed |
| BILLING_LEGAL_READY | true, only after the customer information below is complete |
| IG_INTEGRATION_AVAILABLE | false |

A publishable Stripe key is not needed for this server-hosted Checkout implementation. Enter secrets directly in Render, never in a commit, screenshot or chat message. Save and deploy the environment update.

## Customer information still required

- Seller's legal name/legal form; company number if applicable.
- Public business address and customer-support email.
- VAT registration is confirmed; all agreed prices include VAT. Obtain the VAT number. Set up and verify tax calculation/reporting in the sandbox: the integration explicitly marks its prices inclusive, but this setting alone does not calculate or report VAT. Never add VAT on top.
- Refund/cancellation policy. Proposed commercial policy for approval: refunds of unused credits requested within 14 days, without restricting statutory remedies. The terms must also explain how starting paid processing affects any applicable cancellation rights.
- Finish the customer-facing service terms, privacy notice and relevant data-processing information using the actual operator details; add the terms/acceptance and durable order-confirmation path before enabling the legal-readiness flag.

The existing service-terms draft is not a published customer agreement. The readiness flags must not be used as substitutes for completing these items.

## Final live verification

- Confirm the site is healthy and billing opens a Stripe-hosted live checkout with the expected price.
- Check Stripe's endpoint-delivery log and verify a successful signed event reaches the app.
- A real purchase/refund costs money: obtain the operator's choice of order and authorization before making it. Never use a real card as an automated test without that authorization.
- Verify exactly the purchased number of credits was granted and that the customer received a receipt/order confirmation.
- Use a separate QA account for payment checks; preserve the original unlimited club account and its trophy records.

References: https://docs.stripe.com/keys ; https://docs.stripe.com/checkout/fulfillment ; https://www.gov.uk/online-and-distance-selling-for-businesses/online-selling