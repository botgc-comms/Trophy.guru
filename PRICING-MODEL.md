# Trophy Archive AI — pricing model

Recommended launch pricing:

| Pack | Price | Trophies | Customer price per trophy |
| --- | ---: | ---: | ---: |
| Free proof | £0 | 1 | £0 |
| Single | £7.50 | 1 | £7.50 |
| Club | £60 | 10 | £6.00 |
| Heritage | £225 | 50 | £4.50 |
| Cabinet | £875 | 250 | £3.50 |

These are non-expiring credits, not a recurring subscription. The core job is a finite archive project; subscriptions should be introduced later for genuinely recurring value such as hosted public pages, additional administrators, backups, API access and website publishing.

## Unit economics

Stripe currently lists standard UK-card pricing at 1.5% + 20p per transaction. The table below applies that rate to one purchase of each pack. It then uses a deliberately conservative £1.50 per-trophy envelope for all variable AI, short-term storage and delivery costs. That is a planning ceiling, not a statement of the actual OpenAI invoice.

| Pack | Stripe fee | Net after Stripe per trophy | Contribution after £1.50 variable cost | Contribution margin |
| --- | ---: | ---: | ---: | ---: |
| Single | £0.31 | £7.19 | £5.69 | 75.8% |
| Club | £1.10 | £5.89 | £4.39 | 73.2% |
| Heritage | £3.58 | £4.43 | £2.93 | 65.1% |
| Cabinet | £13.33 | £3.45 | £1.95 | 55.6% |

At a £1.00 variable cost, the corresponding contribution margins are approximately 82.5%, 81.5%, 76.2% and 69.9%. These figures exclude VAT, corporation tax, refunds, disputes, foreign-card/FX costs, fixed hosting, development, advertising and human support.

OpenAI prices `gpt-image-2` by input/output image tokens rather than a single flat per-image fee. Record the returned usage for every illustration and engraving job, convert it using the current OpenAI price sheet, and update a rolling p50/p90/p99 cost dashboard before changing prices. The published model pricing and Stripe rate are volatile inputs:

- https://developers.openai.com/api/docs/pricing
- https://developers.openai.com/api/docs/models/gpt-image-2
- https://stripe.com/gb/pricing

## Commercial safeguards

- Limit the free proof to one verified organisation, two evidence images, one inscription analysis and one illustration.
- Reserve a credit when a trophy is created; consume it on the first successful billable AI result, with idempotent release on provider failure.
- Include one illustration generation per trophy. Price extra regenerations separately or include a very small support allowance.
- Allow normal evidence volume, but place a documented fair-use cap and require confirmation before an unusually large rerun.
- Put account-level daily limits and a project-level OpenAI budget alert above every server endpoint that can spend money.
- Do not advertise uncapped assisted onboarding for the Cabinet pack. At £3.50 per trophy, human labour must be self-service, tightly bounded or sold as a separate service.
- Review pricing after the first 25 paid clubs using real conversion, support minutes, p90 AI cost, refund and acquisition-cost data.

The £7.50 single price is useful as a low-friction proof, while the £60 pack should be the principal conversion target. A 250-trophy club can now buy one Cabinet pack rather than combining smaller packs. The £875 price is £3.50 per trophy and retains a 55.6% contribution margin even under the deliberately conservative £1.50 variable-cost envelope. Large collections should remain self-service; bespoke handling belongs in a separately quoted service.
