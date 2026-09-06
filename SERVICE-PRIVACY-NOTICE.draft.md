# Trophy.guru service privacy notice — unpublished launch draft

**Status: NOT FOR PUBLICATION.** Prepared on 6 September 2026 for an initial launch to UK organisations. This is proposed launch wording, not a description of all current behaviour. In particular, the current app has no separate publication approval, working payments or complete account-deletion/retention workflow. Do not replace wwwroot/privacy.html with this draft until those controls exist and the outstanding details below are resolved.

## Editor's launch requirements

- Insert the legal operator name, legal/contact address, privacy email and effective date. Add company and ICO registration details where applicable; do not invent them.
- Confirm controller/processor responsibilities, club publication basis and the accompanying data processing agreement.
- Complete the retention schedule and test the deletion/backup-expiry behaviour it promises.
- Confirm the actual hosting, email, AI, payment and analytics suppliers, processing regions, contractual roles and any international-transfer safeguards. Stripe is proposed, not currently connected.
- Implement explicit publication/display-name approval and prevent automatic member suggestions from becoming public identities.
- Implement appropriate access, export, correction, account closure and rights-request procedures.
- Verify the proposed no-advertising/no-training commitment against operator practices and supplier agreements.
- Have a UK privacy practitioner review the completed notice and the AI/member-matching DPIA. Also prepare service terms and a DPA; this notice does not replace them.

---

# Privacy at Trophy.guru

Effective date: [EFFECTIVE DATE]

Trophy.guru is operated by [LEGAL BUSINESS NAME], of [BUSINESS/CONTACT ADDRESS]. You can contact us about personal information at [PRIVACY EMAIL].

Trophy.guru helps clubs and other organisations create digital trophy archives. It reads photographs of inscriptions and other historical records, suggests winner information, creates trophy illustrations and lets organisations share approved honours records.

## Who is responsible for your information?

If you create or administer an account, contact support or pay for the service, [LEGAL BUSINESS NAME] is responsible for the information we use to manage that relationship and protect the service.

If your name or information appears in an organisation's trophy archive or member directory, that organisation normally decides why it is used, what records are accurate and whether they should be published. We process that information on its instructions under our data processing agreement. The organisation should also explain this use in its own privacy information.

For a question about a particular honours record, contact the organisation shown on the board or [PRIVACY EMAIL]. We will help route the request appropriately. We remain responsible for our own obligations as a service provider.

## Information the service handles

Depending on how the service is used, this can include:

- **Account and organisation information:** administrator name, email, password hash, club name, logo, website and account settings.
- **Archive material:** trophy details, photographs, inscriptions, winner names or initials, years, results, historical descriptions, corrections and review decisions. Material comes from the organisation and the records it uploads, rather than necessarily from the people named in it.
- **Optional member matching information:** member identifiers, names, gender and relevant year information supplied by the organisation. The service is designed to replace readable dates of birth with a birth year and a derived matching code, and to reduce joining dates to a year. These derived records can still relate to identifiable people. The original member spreadsheet is not retained by the application after import.
- **Illustrations and analysis results:** generated trophy images, suggested text, confidence information and source references.
- **Billing information:** [CONFIRM AT LAUNCH: invoice/contact details, purchased credits, usage, subscription state and payment-provider references]. Payment card details are entered with the payment provider; [CONFIRM FINAL INTEGRATION] Trophy.guru does not receive or store complete card numbers or security codes.
- **Service and security information:** [CONFIRM LOGGING CONFIGURATION: IP address, device/browser information, access records, error details, relevant account and security events].
- **Website-integration information, if enabled:** [CONFIRM ADAPTER: organisation identifier, necessary verified viewer identifier and access entitlement]. We do not ask members to give Trophy.guru their Intelligent Golf password. Reading a name displayed on another website is not treated as proof of identity.

Organisations should only upload information they are entitled to use for the archive. Please avoid unrelated personal information in photographs, notes and spreadsheets.

## Why the information is used

We use account and contact information to provide the service, administer access and respond to enquiries. Where you are personally party to our agreement, this is necessary for that contract. Where you act for an organisation, our basis is our legitimate interest in managing the organisation's account and service relationship.

We use billing records to administer payments and meet applicable accounting obligations. We use proportionate security records to prevent misuse, investigate failures and protect accounts, relying on legitimate interests or a legal obligation where applicable. We will explain our specific interests and how to object if they apply to your information.

We process the organisation's archive and member material to provide the functions it requests. The organisation determines its lawful basis for those purposes and for publication; using Trophy.guru does not automatically provide permission to publish someone's information.

Optional analytics uses consent. You can decline it without losing access to the service.

[CONFIRM OPERATOR AND SUPPLIER PRACTICES BEFORE PUBLICATION] We do not sell archive or member information or use customer material for advertising or training our own AI models.

## AI processing and human review

When an organisation requests image analysis or illustration generation, the selected photographs, relevant instructions and, for record analysis, existing winner records (including names, years, review information and descriptions) are sent to our AI service provider, [CONFIRM CONTRACTING ENTITY/SERVICE]. Photographs may contain personal information visible in the records.

AI can make mistakes. An extraction result or member match is a suggestion. The organisation is responsible for reviewing names, identities and results before approving them for publication. The service does not use those suggestions to decide a person's eligibility for membership, credit or another service.

[CONFIRM SUPPLIER TERMS] Describe the applicable AI-provider retention, abuse-monitoring, training controls and international processing arrangements here. An API setting requesting that a response is not stored must not be described as a guarantee of zero provider retention.

## What appears on an honours board?

[PUBLICATION GATE REQUIRED] An organisation decides whether its board is private, public or available only to authorised viewers, using the access methods supported for its account. It approves the display names and information shown. The publication process is separate from reviewing an inscription or suggesting a member match.

Approved board information may include the organisation's identity, trophy names and illustrations, winner display names, years and approved descriptions. Private member-directory fields, dates of birth, membership numbers, uploaded evidence and internal matching notes are not published as part of the board.

A public board can be viewed or shared by anyone. Initials can still identify a person in context. Search-engine instructions cannot guarantee that public information will never be indexed or copied. Withdrawing a record stops future access through the service's controlled publication routes and caches [VERIFY IMPLEMENTATION], but cannot recall copies someone has already made.

Where a board is embedded or synchronised into an organisation's website, its access arrangements must protect the underlying records as well as the page displaying them. The organisation remains responsible for its website and any copies held there.

## Who receives information?

We use suppliers only for the relevant service functions, subject to the arrangements applicable to each supplier. Publish the completed supplier register at [SUBPROCESSOR/PROVIDER LIST URL]. At launch it should identify:

| Function | Provider to confirm | Information involved |
| --- | --- | --- |
| Application hosting and storage | [RENDER CONTRACTING ENTITY; ANY DATABASE/OBJECT STORAGE PROVIDER] | Information stored or processed in the service |
| AI image reading and illustration | [OPENAI CONTRACTING ENTITY/SERVICE] | Selected images, prompts, existing winner records and generated results |
| Payments and subscriptions | [STRIPE ENTITY, IF ADOPTED] | Billing/payment information and payment references |
| Transactional account email | [PROVIDER] | Necessary recipient/account information and message content |
| Optional website analytics | [GOOGLE ENTITY/SERVICE] | Consented technical/usage events, excluding archive contents from our custom events |
| Font delivery (unless fonts are hosted by Trophy.guru at launch) | [GOOGLE FONTS OR FINAL DELIVERY ARRANGEMENT] | Browser network requests for typography; verify associated technical data and provider terms |
| Supported website integration | [CUSTOMER CMS/INTEGRATION ARRANGEMENT] | Approved published records and necessary access information |

Authorised support staff may access information where needed to resolve a problem, maintain the service or investigate misuse. [CONFIRM SUPPORT ACCESS/AUDIT CONTROLS.] We may disclose information where required by law or necessary to handle legal claims, subject to applicable safeguards. Payment providers may also process some information as independent controllers; link to their applicable notices.

## Where information is processed

[COMPLETE BEFORE PUBLICATION] State the actual storage/processing locations and any access from other countries. Explain the relevant UK adequacy arrangements or other safeguards for restricted transfers, and how someone can obtain information about them. A hosting-region setting alone does not establish where every supplier processes data.

## How long information is kept

The following schedule must be completed with actual periods or clear retention criteria before publication. Retention includes controlled backups and supplier arrangements where relevant.

| Information | Retention period/criteria |
| --- | --- |
| Active organisation archive and illustrations | [SERVICE-TERM AND INACTIVITY RULE] |
| Source photographs | [ORGANISATION CHOICE / DEFAULT AND MAXIMUM RULE] |
| Optional member directory and matching information | [RETENTION/REVIEW RULE; REMOVAL PROCESS] |
| Closed account/archive data | [EXPORT WINDOW AND DELETION DEADLINE] |
| Backup copies | [EXPIRY PERIOD AND RESTORE/RE-DELETION RULE] |
| Billing and legally required financial records | [APPLICABLE ACCOUNTING RETENTION REQUIREMENT] |
| Security/access logs | [PERIOD AND EXCEPTION CRITERIA] |
| Support correspondence | [PERIOD/CRITERIA] |
| Analytics | [CONFIRMED PROVIDER RETENTION SETTING] |

Where a law or a legal claim requires specific records to be kept longer, we limit retention to the information needed for that purpose. Non-expiring trophy credits are a commercial entitlement; they do not mean all personal information is kept indefinitely.

## Your choices and rights

Depending on the circumstances and applicable law, you may request access to your information, correction, erasure, restriction or portability, and you may object to uses based on legitimate interests. Where processing relies on consent, you can withdraw it for the future. Some rights have conditions or exceptions, which we explain when responding.

For information controlled by an organisation, contact that organisation or [PRIVACY EMAIL] so we can help it respond. For our own account, billing, support or security processing, contact [PRIVACY EMAIL]. We may need proportionate information to confirm your identity; please do not send sensitive identity documents unless requested through an appropriate channel.

You can complain to the UK Information Commissioner's Office. See [the ICO's complaints information](https://ico.org.uk/make-a-complaint/). We would also welcome the opportunity to address your concern directly.

## Cookies and analytics

Essential storage supports sign-in, security and your privacy preferences. Optional Google Analytics loads only after you accept it. You can change your choice using the Privacy & cookie settings control. See [COOKIE NOTICE URL] for the current storage/cookie inventory, purposes and durations.

[CONFIRM FONT DELIVERY AT LAUNCH] Current pages request fonts from Google independently of optional analytics. If retained, describe those requests and their privacy implications here; alternatively host fonts with the service. Declining analytics does not itself disable all non-analytics third-party requests.

Our analytics events are designed to exclude archive contents, winner and member names, private source images and internal club/member identifiers. Analytics providers can still receive technical information such as network/device data; this is why optional analytics remains subject to your choice.

## Changes to this notice

We update this notice when the service or its processing changes and show the effective date above. We will bring material changes to affected customers' attention in an appropriate way. A notice update does not by itself authorise new uses of an organisation's archive information.

---

## Review references (not customer-facing copy)

This draft combines the verified local data flows with proposed launch controls. Check the completed controller/processor allocation against [ICO guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/controllers-and-processors/controllers-and-processors/), supplier contracts against [ICO Article 28 guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/contracts-and-liabilities-between-controllers-and-processors-multi/responsibilities-and-liabilities-for-controllers-using-a-processor/), and transfers against [ICO transfer guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/international-transfers/). Use PRODUCTION-READINESS-2026-09.md for outstanding implementation gates.
