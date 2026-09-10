using System.Net;
using System.Text.Json;

namespace Trophy.Catalogue.Services;

// Curated public information only. Never read a club store from this module.
public static class ProductPages
{
    public record Section(string Heading, string Text);
    public record Page(string Path, string Title, string Heading, string Description, Section[] Sections);
    public const string Overview = "Trophy Guru provides AI-assisted transcription of engraved trophies, existing honours boards and historic competition records from photographs. Golf clubs and other sporting organisations can recover winner names and years, review the readings and build a searchable historical archive. Records can be exported or shared as an electronic honours board.";
    public static readonly Section[] Faqs = [
        new("What is an electronic honours board?", "An electronic honours board is a digital record of competition winners. Trophy Guru lets a club share confirmed winners online, with views by year, trophy and winner, while retaining a private working archive."),
        new("What is Trophy Guru?", Overview),
        new("Who is Trophy Guru intended for?", "Trophy Guru is intended for clubs and organisations preserving trophy and competition history. Golf clubs are a principal use case: historic cups, medals and engraved plates can hold many years of winner records."),
        new("How can old trophy records be digitised?", "Photograph the records, add the images to a trophy in the archive, review the proposed names and years, and correct or add information from club records. Confirm the results before publishing an honours board."),
        new("Can engraved trophy plates be used?", "Yes. Photographs of engraved trophies and trophy plates can be used as sources. Several views can help with reflections or lettering around a curved surface. Ambiguous text still needs human review."),
        new("What happens to the historical information once digitised?", "The club can edit and confirm winner records, publish a searchable honours board and export its archive as CSV. Source evidence and member information remain in the private archive rather than the public board."),
        new("Is Trophy Guru only for golf clubs?", "No. The archive supports club setup with a sport and is also presented for cricket, rugby, bowls and tennis clubs, schools and historic collections. The shared workflow records trophies, years and winners; golf is a principal use case."),
        new("Will every engraving be read correctly?", "No. Reflections, wear and unusual lettering can make a record ambiguous. Check the proposed reading against the source photographs and other club records. Do not treat a proposed reading as a confirmed historical fact."),
        new("Can we add missing winners or export our records?", "Yes. The archive supports manual winner editing and missing-year entry, plus CSV export. A gap in the archive should be checked against other sources rather than filled with a guessed name.")
    ];
    public static readonly Page[] All = [
        new("/electronic-honours-boards", "Electronic Honours Boards for Golf Clubs | Trophy Guru", "Electronic honours boards for golf clubs", "Create an electronic honours board from historic trophy records. Review winners, share your club’s history online and retain a CSV archive.", [
            new("What an electronic honours board contains", "An electronic honours board records the people who won a club’s competitions over time. Trophy Guru turns confirmed trophy and competition records into a searchable online board, with views by year, trophy and winner. It gives members a way to explore the history behind the silverware without replacing the original trophies."),
            new("Build the board from evidence", "Start with photographs of engraved trophies, plates or other winner records. Review the proposed names and years before publishing. If an engraving is worn or a year is missing, compare it with other club sources and add a correction manually. The purpose is to preserve what the evidence supports, including uncertainty where a record cannot yet be confirmed."),
            new("Choose what to share", "The working archive and the published honours board serve different purposes. Source photographs and member matching belong in the private archive. The club controls publication of its confirmed honours board and can withdraw it. Publication does not turn the private management screens into public pages."),
            new("Keep a usable archive", "An online board makes the history available to members; CSV export provides a separate copy of the recorded information. Start with one trophy to establish how your club will check names, years and competition details before extending the work to the rest of the collection.")]),
        new("/for-golf-clubs", "Preserve Golf Club Trophy History | Trophy Guru", "Preserve your golf club’s trophy history", "Digitise golf club trophy winners from engraved cups and plates, review historic names and years, and create a searchable electronic honours board.", [
            new("Bring the competition history together", "A golf club’s history can be spread across cups, medals, engraved bases, honours boards and old result sheets. Trophy Guru provides a working archive for bringing trophy photographs and winner records together, so the club can review the evidence before presenting its history online."),
            new("Start with a manageable collection", "Choose one trophy with legible engravings and gather several photographs. Ask someone familiar with the club’s competitions to check the proposed names and years. Decide how to record initials and uncertain entries consistently. This establishes a review process that can be repeated across a much older collection."),
            new("Check historical names carefully", "Historic engravings may use initials or abbreviations, and similar names do not always refer to the same person. Trophy Guru supports manual corrections and member matching, but your club should confirm the historical interpretation. A missing entry is a reason to consult club records, not proof that a competition was never held."),
            new("Make the history available to members", "Publish confirmed records as an electronic honours board that members can explore by year, trophy or winner. Keep the source evidence in the private archive and export a CSV copy when needed. Clubs considering website embedding can also read the existing Intelligent Golf service page for its specific scope and pricing.")]),
        new("/digitise-trophy-records", "How to Digitise Historic Trophy Records | Trophy Guru", "How to digitise historic trophy records", "Turn photographs of engraved trophies and trophy plates into reviewed digital winner records, preserving names and years for an online honours board.", [
            new("Gather the original evidence", "Start with the trophy and any related records your club already holds. Photograph the complete object and the individual engraved areas. Include the trophy name and overlapping views of lettering that wraps around a cup or base. Keep the physical trophy and original records: a digital transcription is another way to use the information."),
            new("Make the lettering visible", "Reflections and curved surfaces can hide letters. Try another angle and include more than one photograph where a line is difficult to read. Trophy plates can be photographed individually. Photos of honours boards or written results can provide additional evidence when an engraving is incomplete or damaged."),
            new("Separate transcription from confirmation", "Trophy Guru proposes winner names and years from uploaded images. Check those readings against the photographs. Correct a name only when the evidence supports it, and consult other club records when an entry remains ambiguous. Manual editing and missing-year entry let the club improve its archive as more evidence becomes available."),
            new("Preserve and share the result", "Confirm the reviewed winners, export the archive as CSV, and publish an electronic honours board when the club is ready. For a collection covering many decades, work trophy by trophy and keep a clear review process. Digitisation helps make historical information accessible; its quality still depends on the sources and the checks made by the club.")]),
        new("/how-it-works", "How Trophy Guru Works: Photos to Honours Board", "From trophy photographs to an honours board", "Photograph engraved trophies, review proposed names and years, correct the records and publish a digital honours board with Trophy Guru.", [
            new("1. Set up your club archive", "Create an account and complete your club details, including its name, sport, country and logo. New clubs start with an empty private collection. Add a trophy and its photographs to begin building the archive around the evidence your club holds."),
            new("2. Add photographs of the records", "Upload photographs of the trophy, its engraved plates or other winner records. Several views help provide context for difficult lettering. Trophy Guru compares the image set and proposes names and years for review. Reading is assisted by AI and does not replace checking the original evidence."),
            new("3. Review names, years and gaps", "Check proposed readings against the source images. Edit incorrect details and add missing winners from other club records. Member matching can assist reconciliation where a member directory is available, but the club should confirm the result. Mark records as reviewed only when the evidence supports them."),
            new("4. Publish and export", "Publish the confirmed winners as a searchable digital honours board, with views by year, trophy and winner. The private archive retains the working evidence and member information. You can also export records as CSV. The homepage includes an illustrative board to explore before starting your own archive.")]),
        new("/faq", "Trophy Digitisation and Honours Board FAQ | Trophy Guru", "Questions about trophy digitisation", "Answers about engraved trophy plates, historical winners, human review, digital honours boards and exporting your club’s Trophy Guru archive.", Faqs),
        new("/about", "About Trophy Guru and Marabou Stork Limited", "About Trophy Guru", "Trophy Guru is a Marabou Stork Limited service for digitising historic trophy and competition records and creating electronic honours boards.", [
            new("A service from Marabou Stork Limited", "Trophy Guru is produced by Marabou Stork Limited. Its purpose is to help clubs digitise historic trophy and competition records and create electronic honours boards. The canonical website is https://trophy.guru/."),
            new("Preserving the names behind the trophies", "Engraved cups and plates preserve winner information, but a collection can be difficult to consult when its records are spread across many physical objects. Trophy Guru helps a club use photographs to capture names and years, check the proposed readings and bring confirmed records into a digital archive."),
            new("Built around a club’s review process", "Golf clubs are a principal use case, alongside other sporting organisations and historic collections already described on this site. The workflow combines source photographs, editable records and human confirmation. It does not guarantee that every engraving can be read, and it leaves historical interpretation with the people reviewing the evidence."),
            new("Explore the service", "The homepage shows the workflow, current public pricing and an illustrative honours board. The how-it-works guide explains the steps from photographs to publication, and the FAQ covers common questions. You can use the existing account signup to start an archive. A separate public demo-request or contact-submission service is not currently offered on this site.")]),
        new("/privacy-and-security", "Archive Privacy and Publication Controls | Trophy Guru", "Your private archive and your public honours board", "Understand Trophy Guru’s private club archive, publication controls and optional analytics before sharing a digital honours board.", [
            new("Private working records", "Trophy management, uploaded source evidence and member records sit behind account access. Each club has its own archive. A published honours board is a separate presentation of the information selected for publication; it is not access to the club’s working management screens."),
            new("Publication is a club decision", "The service supports previewing, publishing and withdrawing an honours board. Check the information you intend to share before publishing. Confirmed names and competition records can be public on the board; source evidence and private member matching are not part of that public presentation."),
            new("Account controls", "The application supports email verification, password recovery and session management. Club users should use those controls and review who has access to their archive. These are implemented service controls, not a claim of an external security certification."),
            new("Optional public-page analytics", "Google Analytics loads only after consent. Account and archive pages are excluded from the analytics script. Public page measurement records safe page paths, referring website domains and selected campaign tags so the service can understand discovery traffic. The privacy and cookies page explains the choice and how to withdraw consent."),
            new("Public agent tools", "The site’s agent tools return curated product explanations and public page links. They do not read customer archives, upload photographs, publish boards, change records or submit contact details. Reading product information through an agent does not create an account or initiate a purchase.")])
    ];

    public static string Links => string.Join("", All.Select(p => $"<a href=\"{p.Path}\">{E(p.Heading)}</a>"));
    private static string E(string value) => WebUtility.HtmlEncode(value);
    public static string Graph(string origin) => JsonSerializer.Serialize(new Dictionary<string, object> {
        ["@context"] = "https://schema.org", ["@graph"] = new object[] {
            new Dictionary<string, object> { ["@type"] = "Organization", ["@id"] = origin + "/#organization", ["name"] = "Marabou Stork Limited", ["url"] = origin + "/about" },
            new Dictionary<string, object> { ["@type"] = "WebSite", ["@id"] = origin + "/#website", ["name"] = "Trophy Guru", ["url"] = origin + "/", ["publisher"] = Ref(origin + "/#organization") },
            new Dictionary<string, object> { ["@type"] = "WebApplication", ["@id"] = origin + "/#software", ["name"] = "Trophy Guru", ["url"] = origin + "/", ["applicationCategory"] = "BusinessApplication", ["operatingSystem"] = "Web", ["description"] = Overview, ["publisher"] = Ref(origin + "/#organization") },
            new Dictionary<string, object> { ["@type"] = "Service", ["@id"] = origin + "/#service", ["name"] = "Trophy record digitisation and electronic honours boards", ["description"] = Overview, ["provider"] = Ref(origin + "/#organization"), ["url"] = origin + "/how-it-works", ["isRelatedTo"] = Ref(origin + "/#software") }
        }});
    private static Dictionary<string, string> Ref(string id) => new() { ["@id"] = id };

    public static string Render(Page page, string origin)
    {
        var breadcrumbs = JsonSerializer.Serialize(new Dictionary<string, object> {
            ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new object[] {
                new { @type = "ListItem", position = 1, name = "Home", item = origin + "/" },
                new { @type = "ListItem", position = 2, name = page.Heading, item = origin + page.Path }
            }}).Replace("\"type\":", "\"@type\":");
        var faq = page.Path == "/faq" ? "<script type=\"application/ld+json\">" + JsonSerializer.Serialize(new Dictionary<string, object> {
            ["@context"] = "https://schema.org", ["@type"] = "FAQPage", ["@id"] = origin + "/faq#faq",
            ["mainEntity"] = Faqs.Select(f => new Dictionary<string, object> { ["@type"] = "Question", ["name"] = f.Heading, ["acceptedAnswer"] = new Dictionary<string, string> { ["@type"] = "Answer", ["text"] = f.Text } }) }) + "</script>" : "";
        var sections = page.Sections.Select(s => $"<section><h2>{E(s.Heading)}</h2><p>{E(s.Text)}</p></section>");
        var content = page.Path == "/how-it-works" ? "<ol class=\"product-steps\">" + string.Join("", sections.Select(s => "<li>" + s + "</li>")) + "</ol>" : string.Join("", sections);
        return $$"""
        <!doctype html><html lang="en-GB"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{E(page.Title)}}</title><meta name="description" content="{{E(page.Description)}}">
        <meta name="robots" content="index,follow,max-image-preview:large"><link rel="canonical" href="{{origin}}{{page.Path}}">
        <meta property="og:type" content="website"><meta property="og:site_name" content="Trophy Guru"><meta property="og:url" content="{{origin}}{{page.Path}}">
        <meta property="og:title" content="{{E(page.Title)}}"><meta property="og:description" content="{{E(page.Description)}}">
        <meta property="og:image" content="{{origin}}/images/brand/trophy-guru-logo.png"><meta property="og:image:width" content="1983"><meta property="og:image:height" content="793"><meta property="og:image:alt" content="Trophy Guru">
        <meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{{E(page.Title)}}"><meta name="twitter:description" content="{{E(page.Description)}}"><meta name="twitter:image" content="{{origin}}/images/brand/trophy-guru-logo.png">
        <link rel="icon" href="/favicon.svg"><link rel="stylesheet" href="/marketing.css"><link rel="stylesheet" href="/product-pages.css"><link rel="stylesheet" href="/analytics.css">
        <script src="/analytics.js" defer></script><script src="/webmcp.js" defer></script>
        <script type="application/ld+json">{{breadcrumbs}}</script>{{faq}}<link rel="stylesheet" href="/branding.css"></head>
        <body class="guide-body product-page"><a class="product-skip" href="#main">Skip to content</a>
        <header class="product-header"><a href="/" aria-label="Trophy Guru home"><img src="/images/brand/trophy-guru-logo-transparent.png" width="240" height="64" alt="Trophy Guru"></a>
        <nav aria-label="Main navigation"><a href="/how-it-works">How it works</a><a href="/for-golf-clubs">For golf clubs</a><a href="/faq">FAQ</a><a href="/archive.html#signup">Start your archive</a></nav></header>
        <main id="main"><div class="product-intro"><nav aria-label="Breadcrumb"><a href="/">Home</a> / <span aria-current="page">{{E(page.Heading)}}</span></nav>
        <p class="eyebrow">Trophy Guru</p><h1>{{E(page.Heading)}}</h1><p>{{E(page.Description)}}</p></div>
        <div class="product-copy">{{content}}
        <section><h2>Explore the service</h2><p><a href="/#member-experience">Explore the illustrative honours board</a>, <a href="/#pricing">read the current pricing</a> or <a href="/archive.html#signup">start your club archive</a>.</p><nav class="product-links" aria-label="Related guides">{{Links}}</nav></section></div></main>
        {{SiteBranding.Footer}}</body></html>
        """;
    }

    public static string Llms(string origin) => "# Trophy Guru\n\n> " + Overview + "\n\nProduced by Marabou Stork Limited. Proposed readings require human review.\n\n## Product and company\n\n- [Trophy Guru](" + origin + "/)\n" + string.Join("\n", All.Select(p => $"- [{p.Heading}]({origin}{p.Path}): {p.Description}")) + $"\n\n## Further reading\n\n- [Blog]({origin}/blog)\n- [Privacy and cookies]({origin}/privacy.html)\n\n## Public agent access\n\nRead-only MCP endpoint: {origin}/mcp. Public product facts only; no customer records or transactions.\n";
}
