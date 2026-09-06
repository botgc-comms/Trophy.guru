# Trophy Archive — data processing agreement (unpublished draft)

Prepared 6 September 2026. Proposed terms for review, not an executed agreement. Complete the parties, schedules and operational commitments before customer acceptance.

## Parties and scope

Controller: the customer organisation identified in the accepted order, contact [CUSTOMER PRIVACY CONTACT]. Processor: [OPERATOR LEGAL NAME AND ADDRESS], contact [OPERATOR PRIVACY CONTACT]. This agreement covers archive material processed for the customer. The operator’s separate controller activities for account administration, billing and service security are described in its service privacy notice.

Subject matter: a hosted trophy and awards archive. Duration: the service term and the agreed return/deletion period. Operations include receiving, storing, extracting, matching, editing, generating illustrations, exporting and, only on instruction, publishing selected records. The customer chooses purposes, lawful bases, access and publication. It supplies lawful instructions and required notices and considers data accuracy, minimisation, children and individual rights.

People concerned may include present and former members, award winners, junior winners, donors and other people appearing in supplied historical records. Data may include names or initials, awards and years, club association, photographs and descriptions. Optional directory matching may involve membership identifiers, gender, birth/joining years and a keyed identity fingerprint. Special-category and criminal-offence information are not required; the customer must avoid uploading them unless a separately approved arrangement covers them. Pseudonymised matching data remains protected personal information.

## Processor commitments

The processor acts only on documented customer instructions, including for transfers, unless law requires otherwise. It informs the customer of a legal requirement where permitted and raises instructions it considers unlawful. Staff and contractors with access must be bound to confidentiality. Access is limited to authorised needs.

The processor maintains proportionate technical and organisational measures and helps the customer address individual requests, security incidents, impact assessments and regulatory enquiries. It promptly passes requests relating to customer-controlled records to the customer and does not independently decide their substantive outcome without authority or legal obligation.

The processor notifies the customer without undue delay after becoming aware of a personal-data breach affecting its archive. It supplies available facts, likely consequences, containment steps and further updates as investigation develops. [Confirm monitored incident contact, escalation cover and contractual response target.] This does not replace the controller’s own statutory reporting duties.

The processor provides information reasonably necessary to demonstrate compliance and permits appropriate audits or inspections, including where evidence is insufficient or an incident warrants them. Practical arrangements must protect other customers’ information without frustrating the customer’s statutory rights.

At the customer’s choice when processing ends, the processor returns or deletes the archive and deletes copies unless law requires retention. [Complete tested export/deletion procedure, active-data deadline and backup expiry.] Backups awaiting expiry remain access-restricted and are not put back into ordinary use; a recovery must reapply recorded deletions and publication withdrawals. The processor keeps required evidence of its actions.

## Subprocessors and transfers

The customer gives [SPECIFIC AUTHORISATION, or GENERAL AUTHORISATION WITH NOTICE PERIOD] for listed subprocessors. The processor gives agreed advance notice of additions or replacements and a meaningful objection procedure. It imposes equivalent relevant data-protection obligations and remains responsible for its subprocessors’ performance. [Set resolution/termination terms for a justified unresolved objection.]

International transfers require a lawful UK mechanism and assessment of any necessary protections. Do not assume that an EU hosting region prevents support or AI processing elsewhere. [Record adequacy, approved IDTA/Addendum or other applicable mechanism for each transfer and complete any required transfer assessment.]

## Schedule A — processing instructions

The authenticated owner’s settings, uploads, editing actions, requests and reviewed publication approvals are ordinary documented instructions. Editors can administer the archive within their assigned role. The public board uses a frozen selected version; AI identity suggestions do not authorise public identification. Descriptions and junior trophies start excluded. Support access, exceptional exports and deletion require a verified customer instruction. [Define verification method and audit retention.]

No private archive material may be reused for marketing or model training merely because it was uploaded. [Confirm provider contracts, settings and any exceptions before making this an operational commitment.]

## Schedule B — security measures to verify before contract signature

Implemented application controls include account password hashing, expiring single-use recovery tokens, secure production cookies, revocable sessions, owner/editor controls, tenant-scoped archive access, origin checks on browser mutations, explicit public snapshots, controlled embedding, signed payment webhooks, a transactional credit ledger and a single-writer data-directory lock.

Deployment controls still require operator evidence: TLS and trusted proxy configuration; infrastructure and backup encryption; restricted staff/production access; provider agreements; patching; monitored alerts; independent backup storage and restore drills; incident response; retention and erasure; capacity planning. The application’s offline backup/restore commands preserve archive files, identity credentials, operational ledger and key-ring together. A successful local test does not establish a production backup service.

## Schedule C — complete subprocessor register

| Provider | Purpose | Data | Contracting entity, locations, retention and transfer mechanism |
|---|---|---|---|
| Hosting provider [confirm Render contract] | Application and persistent storage | Archive, accounts, operational data | [Complete] |
| OpenAI [confirm contracted entity and settings] | Requested reading and illustration | Submitted images and required trophy/record context | [Complete] |
| Transactional email provider [choose] | Verification, recovery and invitations | Recipient and necessary message/link | [Complete] |
| Backup provider [choose] | Restricted recovery copies | Archive, identity, keys and ledger | [Complete] |

Stripe’s role for payments and any independent controller processing must be described accurately in the service notice and payment terms; do not automatically label every provider a processor. Review analytics separately and keep it absent from recovery links and embedded boards.

Research: [ICO controller–processor contracts](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/contracts-and-liabilities-between-controllers-and-processors-multi/what-needs-to-be-included-in-the-contract/). This draft applies the required contract topics to the proposed service; the schedules and commitments require verification. ICO notes that parts of its guidance are being reviewed following the Data (Use and Access) Act.
