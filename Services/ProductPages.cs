using System.Net;
using System.Text.Json;

namespace Trophy.Catalogue.Services;

// Curated public information only. Never read a club store from this module.
public static class ProductPages
{
    public record Section(string Heading, string Text);
    public record Page(string Path, string Title, string Heading, string Description, Section[] Sections);
    public const string Overview = "Trophy Guru helps clubs read names and years from photos of trophies, honours boards and old winner lists. AI suggests the readings. Your club checks them, fixes errors and builds a searchable archive. Export a CSV copy or share an online honours board.";
    public static readonly Section[] Faqs = [
        new("What is an electronic honours board?", "It is an online list of a club's past winners. Members can browse by year, trophy or person. The club checks each record before sharing it. The working archive stays private."),
        new("What is Trophy Guru?", Overview),
        new("Who is Trophy Guru intended for?", "It is for clubs that want to preserve their trophy history. Golf clubs can start with old cups, medals and engraved plates. Each trophy may hold many years of names and results."),
        new("How can old trophy records be digitised?", "Take clear photos and add them to a trophy in your archive. Check the suggested names and years against the photos. Use club records to fill gaps. Confirm the results before you share the board."),
        new("Can engraved trophy plates be used?", "Yes. Take photos of the trophy and its engraved plates. Use several views to reduce glare and show text that curves around a cup. A person must still check unclear letters."),
        new("What happens to the historical information once digitised?", "Your club can check and edit the winners, share an online honours board and export a CSV copy. The source photos and member details stay in the private archive."),
        new("Is Trophy Guru only for golf clubs?", "No. It also supports other sports clubs, schools and historic collections. Choose your sport when you set up the club. The same steps apply: add trophies, check names and years, then share the results."),
        new("Will every engraving be read correctly?", "No. Glare, wear and unusual letters can lead to errors. Check each suggested reading against the photos and other club records. Keep a name marked as uncertain until you have enough evidence."),
        new("Can we add missing winners or export our records?", "Yes. You can edit winners, add missing years and export records as CSV. Check gaps against other club sources. Leave a gap open if the name is not known.")
    ];
    public static readonly Page[] All = [
        new("/demo", "Digital Honours Board Demo for Golf Clubs | Trophy Guru", "Explore an electronic honours board", "Try Trophy Guru’s public digital honours board demo. Browse example winners by year, trophy and person, with no account or payment card needed.", [
            new("From your trophy cabinet to a searchable club history", "Start with photos of cups, plates, shields or old winner lists. Trophy Guru suggests names and years. Your club checks and corrects them before sharing the board. Source photos and private member details stay in the working archive."),
            new("What the demo shows", "Browse a sample honours board by year, trophy or person. The demo uses fictional records. It does not read your photos or create an account. To try your own engravings, start an archive and add your first trophy free."),
            new("Keep the original history and a digital copy", "Keep the trophies and original club records. The online board gives members another way to explore them. Check unclear entries and export a CSV copy to pass on to the next person who looks after the archive.")]),
        new("/trophy-archive-project-plan", "Trophy Archive Project Plan for Club Committees | Trophy Guru", "Plan your club’s trophy archive project", "A practical club committee checklist for a trophy archive: choose a pilot, photograph engravings, assign a reviewer, estimate the work and hand over the records.", [
            new("Agree the first useful result", "Choose a clear goal for the first stage. It might be a checked list of winners for one cup. Start with one trophy, review its names and make a sample board to show your committee."),
            new("Give the evidence and the decisions an owner", "Choose someone to lead the work and someone to check old names. In a small club, this may be the same person. Agree who can approve the public board. Keep notes in club files so the next volunteer can find them."),
            new("Choose a pilot that teaches you something", "Pick a trophy with several decades of clear names. Include a worn inscription if it reflects the rest of your collection. Gather old result sheets before taking photos. Use this first trophy to work out how to handle glare, initials and gaps."),
            new("Protect the physical collection", "Ask before moving a trophy and place it on a stable surface. Try a new camera angle to read worn letters. Follow your club's handling rules for fragile or valuable items. Seek expert advice before cleaning or polishing them.")]),
        new("/electronic-honours-boards", "Electronic Honours Boards for Golf Clubs | Trophy Guru", "Electronic honours boards for golf clubs", "Create an electronic honours board from historic trophy records. Review winners, share your club’s history online and retain a CSV archive.", [
            new("What an electronic honours board contains", "An online honours board lists the winners of your club's past events. Trophy Guru lets members search checked records by year, trophy and person. They can explore the names behind the cups while the original trophies stay in the club."),
            new("Build the board from evidence", "Start with photos of cups, plates or old winner lists. Check the suggested names and years. If a word is worn or a year is missing, look at other club sources. Add a correction when you can support it, and keep doubt visible."),
            new("Choose what to share", "Source photos and member matching stay in the private archive. Your club decides when to share its checked honours board and can take it down. The public board does not give visitors access to the club's management screens."),
            new("Keep a usable archive", "Use the online board to share the history with members. Export a CSV file to keep a separate copy. Start with one trophy and agree how to check names, years and events before adding the rest.")]),
        new("/for-golf-clubs", "Golf Club Trophy Archives and Honours Boards | Trophy Guru", "Preserve your golf club’s trophy history", "Digitise golf club trophy winners from engraved cups and plates, review historic names and years, and create a searchable electronic honours board.", [
            new("Bring the competition history together", "Old winners may be recorded on cups, medals, boards and result sheets. Bring the photos and records together in Trophy Guru. Your club can check each name before sharing its history online."),
            new("Start with a manageable collection", "Choose one trophy with clear names and take several photos. Ask someone who knows the club's events to review the readings. Agree how to handle initials and doubt. Use the same checks as you add more trophies."),
            new("Check historical names carefully", "Two similar names may belong to different people. Old engravings may use initials or short forms. Check member matches and edits against club records. A missing name does not prove that an event was never held."),
            new("Make the history available to members", "Share checked winners on an online honours board. Members can browse by year, trophy or person. Keep source photos private and export a CSV copy when needed. The Intelligent Golf page explains the optional service for club websites.")]),
        new("/digitise-trophy-records", "Trophy Engraving Transcription & Digital Records | Trophy Guru", "Digitise trophy engravings and historic winner records", "Turn photographs of engraved trophies and trophy plates into reviewed digital winner records, preserving names and years for an online honours board.", [
            new("Gather the original evidence", "Take a photo of the whole trophy, then close views of each engraved area. Include the trophy name. Overlap photos where text curves around a cup or base. Keep the trophy and original club records as sources."),
            new("Make the lettering visible", "Glare and curved metal can hide letters. Try another angle and take more than one photo of a hard-to-read line. Photograph plates on their own where useful. Old boards and result sheets may help fill gaps."),
            new("Separate transcription from confirmation", "Trophy Guru suggests names and years from your photos. Check each reading against the image. Look at other club records if a name is unclear. You can edit a record or add a missing year when you find better evidence."),
            new("Preserve and share the result", "Confirm the checked winners and export a CSV copy. Share an honours board when the club is ready. Work through a large collection one trophy at a time. The quality of the archive depends on the sources and the checks your club makes.")]),
        new("/how-it-works", "How Trophy Guru Works: Photograph, Review and Share", "From trophy photographs to an honours board", "Photograph engraved trophies, review proposed names and years, correct the records and publish a digital honours board with Trophy Guru.", [
            new("1. Set up your club archive", "Create an account and add your club's name, sport, country and logo. Your private collection starts empty. Add the first trophy and its photos to begin."),
            new("2. Add photographs of the records", "Upload photos of the trophy, its plates or old winner lists. Several views help with unclear letters. Trophy Guru uses AI to suggest names and years from the images. Check those readings against the originals."),
            new("3. Review names, years and gaps", "Check each suggested reading against the photo. Correct errors and add missing winners from club records. If you use a member directory, check the proposed matches too. Mark records as reviewed once the evidence supports them."),
            new("4. Publish and export", "Share checked winners on an honours board with views by year, trophy and person. Source photos and member details stay private. You can export a CSV copy too. Try the public demo to see how a board works.")]),
        new("/faq", "Trophy Digitisation and Honours Board FAQ | Trophy Guru", "Questions about trophy digitisation", "Answers about engraved trophy plates, historical winners, human review, digital honours boards and exporting your club’s Trophy Guru archive.", Faqs),
        new("/about", "About Trophy Guru: Club History by Marabou Stork Limited", "About Trophy Guru", "Trophy Guru is a Marabou Stork Limited service for digitising historic trophy and competition records and creating electronic honours boards.", [
            new("A service from Marabou Stork Limited", "Marabou Stork Limited makes Trophy Guru. We help clubs turn old trophy records into a digital archive and an online honours board. The service is available at https://trophy.guru/."),
            new("Preserving the names behind the trophies", "Old cups and plates hold years of club history. It can be hard to find one name across a large collection. Use photos to read the names and years, check them, and bring the records into one archive."),
            new("Built around a club’s review process", "Golf clubs, other sports clubs and historic collections can use the service. AI suggests readings from photos. Your club edits and checks the results. Some engravings will remain unclear, and the people reviewing the sources decide what can be confirmed."),
            new("Explore the service", "See the homepage for prices and an example honours board. Read the how-it-works guide for each step, or the FAQ for common questions. You can browse the demo without an account. Try your first trophy free with no payment card.")]),
        new("/privacy-and-security", "Archive Privacy and Publication Controls | Trophy Guru", "Your private archive and your public honours board", "Understand Trophy Guru’s private club archive, publication controls and optional analytics before sharing a digital honours board.", [
            new("Private working records", "Your club's trophy records, source photos and member details sit behind account access. Each club has its own archive. A public honours board shows the records chosen for sharing. It does not open the private management screens."),
            new("Publication is a club decision", "Preview the board and check its contents before sharing it. Your club can publish it or take it down. The public board can show confirmed names and results. Source photos and private member matching stay out of that view."),
            new("Account controls", "Use email verification, password recovery and session controls to manage account access. Review who can use your club archive. These are features of the service; they do not imply an external security certificate."),
            new("Optional public-page analytics", "Google Analytics loads only if you consent. Account and archive pages are excluded. Public-page measurements include safe page paths, referring domains and selected campaign tags. The privacy and cookies page explains these choices and how to withdraw consent."),
            new("Public agent tools", "AI tools can read our public product guide and page links. They cannot read club archives, upload photos, publish boards or change records. They do not submit contact details, create accounts or make purchases.")])
    ];

    public static string Links => string.Join("", All.Select(p => $"<a href=\"{p.Path}\">{E(p.Heading)}</a>"));
    private static string E(string value) => WebUtility.HtmlEncode(value);
    public static string Graph(string origin) => JsonSerializer.Serialize(new Dictionary<string, object> {
        ["@context"] = "https://schema.org", ["@graph"] = new object[] {
            new Dictionary<string, object> { ["@type"] = "Organization", ["@id"] = origin + "/#organization", ["name"] = "Marabou Stork Limited", ["url"] = origin + "/about" },
            new Dictionary<string, object> { ["@type"] = "WebSite", ["@id"] = origin + "/#website", ["name"] = "Trophy Guru", ["alternateName"] = new[] { "Trophy.guru" }, ["inLanguage"] = "en-GB", ["url"] = origin + "/", ["publisher"] = Ref(origin + "/#organization") },
            new Dictionary<string, object> { ["@type"] = "Service", ["@id"] = origin + "/#service", ["name"] = "Trophy record digitisation and electronic honours boards", ["description"] = Overview, ["provider"] = Ref(origin + "/#organization"), ["url"] = origin + "/" }
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
        <meta property="og:image" content="{{origin}}/marketing/trophy-guru-social.png"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630"><meta property="og:image:alt" content="Trophy Guru online honours board with fictional example records">
        <meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{{E(page.Title)}}"><meta name="twitter:description" content="{{E(page.Description)}}"><meta name="twitter:image" content="{{origin}}/marketing/trophy-guru-social.png">
        <link rel="icon" href="/favicon.svg"><link rel="stylesheet" href="/fonts.css"><link rel="stylesheet" href="/marketing.css"><link rel="stylesheet" href="/product-pages.css"><link rel="stylesheet" href="/analytics.css">
        <script src="/analytics.js" defer></script><script src="/webmcp.js" defer></script>
        <script type="application/ld+json">{{breadcrumbs}}</script>{{faq}}<link rel="stylesheet" href="/branding.css"></head>
        <body class="guide-body product-page"><a class="product-skip" href="#main">Skip to content</a>
        <header class="product-header"><a href="/" aria-label="Trophy Guru home"><img src="/images/brand/trophy-guru-logo-transparent.webp" width="240" height="64" alt="Trophy Guru"></a>
        <nav aria-label="Main navigation"><a href="/how-it-works">How it works</a><a href="/for-golf-clubs">For golf clubs</a><a href="/demo">Demo</a><a href="/#pricing">Pricing</a><a href="/archive.html#signup">Try one trophy free</a></nav></header>
        <main id="main"><div class="product-intro"><nav aria-label="Breadcrumb"><a href="/">Home</a> / <span aria-current="page">{{E(page.Heading)}}</span></nav>
        <p class="eyebrow">Trophy Guru</p><h1>{{E(page.Heading)}}</h1><p>{{E(page.Description)}}</p>{{ProductEvidence.IntroActions(page.Path)}}</div>
        <div class="product-copy">{{ProductEvidence.Showcase(page.Path)}}{{content}}{{ProductEvidence.Extra(page.Path)}}{{ProductEvidence.Pricing(page.Path)}}
        <section><h2>Explore the service</h2><p><a href="/demo">Explore the public honours board demo</a>, <a href="/#pricing">read the current pricing</a> or <a href="/archive.html#signup">start your club archive</a>.</p><nav class="product-links" aria-label="Related guides">{{Links}}</nav></section></div></main>
        {{SiteBranding.Footer}}</body></html>
        """;
    }

    public static string Llms(string origin) => "# Trophy Guru\n\n> " + Overview + "\n\nProduced by Marabou Stork Limited. Proposed readings require human review.\n\n## Product and company\n\n- [Trophy Guru](" + origin + "/)\n" + string.Join("\n", All.Select(p => $"- [{p.Heading}]({origin}{p.Path}): {p.Description}")) + $"\n\n## Further reading\n\n- [Blog]({origin}/blog)\n- [Privacy and cookies]({origin}/privacy.html)\n\n## Public agent access\n\nRead-only MCP endpoint: {origin}/mcp. Public product facts only; no customer records or transactions.\n";
}
